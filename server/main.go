// おままごとサーバー（要件定義書 3 章、5.9）
// 入り口（BLE・WebSocket）は goroutine で動かし、ここにはチャンネルで 1 件ずつ届く。
// 届いたものを本体（Kitchen）に渡し、出てきたイベントを表示してログに残し、つながっている画面に送る。
//
//	go run .                       # config.toml を読み、logs/ にログを書く
//	go run . 別の設定.toml
//	go run . --no-ble              # BLE を使わない（デバイスの無い PC で、カメラと画面だけ試す）
package main

import (
	"context"
	"encoding/json"
	"fmt"
	"net"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"
)

func main() {
	if err := run(); err != nil {
		fmt.Fprintf(os.Stderr, "エラー：%v\n", err)
		os.Exit(1)
	}
}

func run() error {
	configPath := "config.toml"
	useBLE := true
	for _, a := range os.Args[1:] {
		switch {
		case a == "--no-ble":
			useBLE = false
		case strings.HasPrefix(a, "--"):
			return fmt.Errorf("知らない指定 %s（使えるのは --no-ble）", a)
		default:
			configPath = a
		}
	}
	config, err := LoadConfig(configPath)
	if err != nil {
		return err
	}
	fmt.Printf("設定 %s：デバイス %d 台\n", configPath, len(config.Roles))
	kitchen := NewKitchen(config)
	log, err := CreateEventLog("logs")
	if err != nil {
		return err
	}
	defer log.Close()
	fmt.Printf("ログ %s\n", log.Path())

	ln, err := net.Listen("tcp", config.WS.Listen)
	if err != nil {
		return fmt.Errorf("%s で待てない。ほかのプログラムがこのポートを使っていないか確かめる：%w", config.WS.Listen, err)
	}
	printAddresses(ln)
	hub := NewHub()
	out := &Output{log: log, hub: hub}
	netCh := make(chan NetMessage, 1024)
	srv := &http.Server{Handler: NewWSHandler(hub, netCh)}
	srvErr := make(chan error, 1)
	go func() { srvErr <- srv.Serve(ln) }()
	defer srv.Close()

	bleCh := make(chan BleMessage, 256)
	bleCtx, stopBLE := context.WithCancel(context.Background())
	defer stopBLE()
	var bleDone chan error
	if useBLE {
		bleDone = make(chan error, 1)
		go func() { bleDone <- RunBLE(bleCtx, bleCh) }()
	} else {
		fmt.Println("BLE は使わない（--no-ble）")
	}
	warnedUnknown := map[string]bool{}

	sig := make(chan os.Signal, 1)
	signal.Notify(sig, os.Interrupt, syscall.SIGTERM)
	for {
		select {
		case msg := <-bleCh:
			if err := onBLE(msg, kitchen, out, warnedUnknown, configPath); err != nil {
				return err
			}
		case err := <-bleDone:
			// BLE の入り口が止まった（Bluetooth が使えない など）。カメラと画面だけで続ける
			bleDone = nil
			if err != nil {
				fmt.Printf("BLE が止まった：%v。デバイスなしで続ける\n", err)
			}
		case msg := <-netCh:
			if err := onNet(msg, kitchen, out); err != nil {
				return err
			}
		case err := <-srvErr:
			return fmt.Errorf("WebSocket の入り口が止まった：%w", err)
		case <-sig:
			fmt.Println("止めます（デバイスを切断）")
			stopBLE()
			if bleDone != nil {
				select {
				case err := <-bleDone:
					if err != nil {
						return err
					}
				case <-time.After(10 * time.Second):
					fmt.Println("デバイスの切断を待ちきれなかった")
				}
			}
			return nil
		}
	}
}

// Output は出したイベントに共通ヘッダを付けて、ログに残し、全部の画面に送り、表示する
type Output struct {
	log *EventLog
	seq uint64
	hub *Hub
}

func (o *Output) Emit(events []ServerEvent) error {
	for _, ev := range events {
		o.seq++
		stamped, err := Stamp(ev, time.Now().UnixMilli(), o.seq)
		if err != nil {
			return err
		}
		if err := o.log.Outgoing(stamped); err != nil {
			return err
		}
		o.hub.Broadcast(string(stamped))
		if text, ok := describe(ev); ok {
			fmt.Printf("%s → %s\n", now(), text)
		}
	}
	return nil
}

func onBLE(msg BleMessage, kitchen *Kitchen, out *Output, warnedUnknown map[string]bool, configPath string) error {
	t := now()
	switch msg.Kind {
	case BleConnected:
		fmt.Printf("%s %s 接続\n", t, msg.Name)
		return out.log.Note(msg.Name + " 接続")
	case BleDisconnected:
		fmt.Printf("%s %s 切断\n", t, msg.Name)
		return out.log.Note(msg.Name + " 切断")
	}
	if err := out.log.Incoming(msg.Name, msg.Line); err != nil {
		return err
	}
	m, err := ParseDeviceMessage(msg.Line)
	if err != nil {
		fmt.Printf("%s %s 読めない行（%v）：%s\n", t, msg.Name, err, msg.Line)
		return nil
	}
	h := kitchen.Handle(m)
	switch h.Kind {
	case Accepted:
		return out.Emit(h.Events)
	case Duplicate:
		fmt.Printf("%s %s 重複を捨てた：seq %d\n", t, msg.Name, m.Seq)
	case UnknownDevice:
		if !warnedUnknown[m.Device] {
			warnedUnknown[m.Device] = true
			fmt.Printf("%s %s 設定ファイルに無いデバイス %s。%s の [roles] に足すと使える\n", t, msg.Name, m.Device, configPath)
		}
	case NotForRole:
		// 役割に合わないもの（塩の knob など）はたくさん来るので表示しない。ログには残っている
	}
	return nil
}

func onNet(msg NetMessage, kitchen *Kitchen, out *Output) error {
	t := now()
	switch msg.Kind {
	case NetConnected:
		fmt.Printf("%s %s #%d 接続（%s）\n", t, msg.Peer.Name(), msg.ID, msg.Addr)
		return out.log.Note(fmt.Sprintf("%s #%d 接続（%s）", msg.Peer.Name(), msg.ID, msg.Addr))
	case NetDisconnected:
		fmt.Printf("%s %s #%d 切断\n", t, msg.Peer.Name(), msg.ID)
		return out.log.Note(fmt.Sprintf("%s #%d 切断", msg.Peer.Name(), msg.ID))
	case NetLagged:
		fmt.Printf("%s 画面 #%d の受け取りが遅れて、%d 件送れなかった\n", t, msg.ID, msg.Skipped)
		return out.log.Note(fmt.Sprintf("画面 #%d に %d 件送れなかった", msg.ID, msg.Skipped))
	}

	if msg.Peer == PeerCamera {
		if err := out.log.Incoming(fmt.Sprintf("camera#%d", msg.ID), msg.Line); err != nil {
			return err
		}
		m, err := ParseCameraMessage(msg.Line)
		if err != nil {
			fmt.Printf("%s カメラ #%d 読めない行（%v）：%s\n", t, msg.ID, err, msg.Line)
			return nil
		}
		h := kitchen.HandleCamera(msg.ID, m)
		switch h.Kind {
		case Accepted:
			return out.Emit(h.Events)
		case Duplicate:
			fmt.Printf("%s カメラ #%d 重複を捨てた：seq %d\n", t, msg.ID, m.Seq)
		}
		return nil
	}

	if err := out.log.Incoming(fmt.Sprintf("screen#%d", msg.ID), msg.Line); err != nil {
		return err
	}
	switch screenEvent(msg.Line) {
	case "hello":
		// 画面が起動し直したときなどに、今の状態（火）に合わせる
		return out.Emit(kitchen.Sync())
	case "reset":
		fmt.Printf("%s 画面 #%d からリセット\n", t, msg.ID)
		return out.Emit(kitchen.Reset())
	}
	return nil
}

// screenEvent は画面から届いた JSON の event を返す。読めなければ空
func screenEvent(line string) string {
	var v map[string]any
	if json.Unmarshal([]byte(line), &v) != nil {
		return ""
	}
	s, _ := v["event"].(string)
	return s
}

func now() string {
	return time.Now().Format("15:04:05.000")
}

// describe は表示する文。位置は 1 秒に何十回も来るので表示しない（ログには残る）
func describe(ev ServerEvent) (string, bool) {
	switch e := ev.(type) {
	case PositionOut:
		return "", false
	case CutOut:
		return fmt.Sprintf("%s を切った（%d 個）", e.Item, e.Count), true
	case AddOut:
		zone := e.Zone
		switch zone {
		case "pan":
			zone = "フライパン"
		case "plate":
			zone = "皿"
		}
		return fmt.Sprintf("%s を%sに +%d", e.Item, zone, e.Count), true
	case SwingOut:
		return "振り下ろし", true
	case SeasonOut:
		return fmt.Sprintf("%s +%sg（合計 %sg）", e.Item, formatNum(e.AmountG), formatNum(e.TotalG)), true
	case FireOut:
		names := []string{"切", "弱", "中", "強"}
		name := "?"
		if int(e.Level) < len(names) {
			name = names[e.Level]
		}
		return "火 " + name, true
	case ResetOut:
		return "リセット", true
	}
	return "", false
}

func printAddresses(ln net.Listener) {
	addr, ok := ln.Addr().(*net.TCPAddr)
	if !ok {
		return
	}
	fmt.Printf("画面は ws://localhost:%d/screen、カメラは ws://localhost:%d/camera でつなぐ\n", addr.Port, addr.Port)
	if addr.IP.IsUnspecified() {
		if ip := lanIP(); ip != nil {
			fmt.Printf("別の PC からは ws://%s:%d/screen（カメラは /camera）\n", ip, addr.Port)
		}
	}
}

// lanIP はこの PC の LAN のアドレス。UDP で行き先を決めるだけで、何も送らない
func lanIP() net.IP {
	c, err := net.Dial("udp", "8.8.8.8:80")
	if err != nil {
		return nil
	}
	defer c.Close()
	if a, ok := c.LocalAddr().(*net.UDPAddr); ok {
		return a.IP
	}
	return nil
}
