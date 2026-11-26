// BLE の入り口（要件定義書 5.9、6.6）
// 6.6 のサービスを持つデバイスを探して全台につなぎ、届いた JSON の行をチャンネルで本体に渡す。
// 1 台ごとに専用の goroutine でつなぎ、切れたら goroutine が終わる。次に見つけたときにつなぎ直す。
//
// tinygo.org/x/bluetooth を使う（Windows・macOS・Linux で動く）。OS ごとの違いで気をつけていること：
//   - macOS は、1 回の探す（スキャン）のうちに同じデバイスを 1 回しか知らせない。
//     なので、デバイスの goroutine が終わったら（切れた・つなげなかった）探し直す
//   - Windows は Disconnect の中で後始末（解放）をするので、1 台につき 1 回だけ呼ぶ

package main

import (
	"context"
	"errors"
	"fmt"
	"os"
	"runtime"
	"sync"
	"time"

	"tinygo.org/x/bluetooth"
)

var (
	serviceUUID = mustUUID("35f64d1a-5af0-4ec7-a321-3b2a485eae3f")
	eventUUID   = mustUUID("2a28760f-2c0e-4e00-b5cd-4ae7faa984cb")
)

const (
	// 電源が切れたデバイスにつなぎに行って待ち続けないため（10 秒だと、サーバーを再起動したあとの再接続に 11〜12 秒かかった。2026-10-13 の実測）
	connectTimeout = 3 * time.Second
	// 切断の通知が来ない OS があるので、つながっているかを自分でも確かめる間隔
	aliveCheck = 1 * time.Second
	// 念のため、この間隔でも探し直す
	scanRefresh = 10 * time.Second
	// 止めるときに、切断が終わるのを待つ長さ（macOS の切断は待たずに戻るため）
	disconnectWait = 2 * time.Second
	// 名前（omamagoto-5C3C）はスキャン応答に入っていて、最初の広告より少し遅れて届く。名前が来るまで待つ長さ
	nameWait = 1 * time.Second
)

type BleKind int

const (
	BleConnected BleKind = iota
	BleDisconnected
	BleLine
)

// BleMessage は BLE の入り口から本体に渡すもの
type BleMessage struct {
	Kind BleKind
	Name string
	Line string
}

type foundDevice struct {
	addr bluetooth.Address
	name string
}

// RunBLE はデバイスを探してつなぎ続ける。ctx が終わったら、つないでいるデバイスを切ってから戻る。
// Bluetooth が使えないときはエラーを返す
func RunBLE(ctx context.Context, out chan<- BleMessage) error {
	adapter := bluetooth.DefaultAdapter
	if err := adapter.Enable(); err != nil {
		return fmt.Errorf("Bluetooth が使えない：%w", err)
	}
	ctx, cancel := context.WithCancel(ctx)
	defer cancel()

	found := make(chan foundDevice, 64)
	onScan := func(_ *bluetooth.Adapter, r bluetooth.ScanResult) {
		// ここは OS の Bluetooth の流れの中で呼ばれるので、止まらないように渡すだけにする
		if !r.HasServiceUUID(serviceUUID) {
			return
		}
		select {
		case found <- foundDevice{addr: r.Address, name: r.LocalName()}:
		default:
		}
	}

	restart := make(chan struct{}, 1)
	scanErr := make(chan error, 1)
	scanDone := make(chan struct{})
	go func() {
		defer close(scanDone)
		scanLoop(ctx, adapter, onScan, restart, scanErr)
	}()

	var wg sync.WaitGroup
	running := map[string]bool{}
	// 名前がまだ届いていないデバイス → 見つけた時刻
	waiting := map[string]time.Time{}
	ended := make(chan string, 16)
	start := func(addr bluetooth.Address, name string) {
		key := addr.String()
		delete(waiting, key)
		if name == "" {
			name = key
		}
		running[key] = true
		wg.Add(1)
		go func() {
			defer wg.Done()
			watchDevice(ctx, adapter, addr, name, out)
			select {
			case ended <- key:
			case <-ctx.Done():
			}
		}()
	}
	waitTick := time.NewTicker(100 * time.Millisecond)
	defer waitTick.Stop()
	addrs := map[string]bluetooth.Address{}
	var result error
loop:
	for {
		select {
		case f := <-found:
			key := f.addr.String()
			if running[key] {
				continue
			}
			if f.name != "" {
				start(f.addr, f.name)
				continue
			}
			if _, ok := waiting[key]; !ok {
				waiting[key] = time.Now()
				addrs[key] = f.addr
			}
		case <-waitTick.C:
			// 待っても名前が来なかったデバイスは、アドレスを名前にしてつなぐ
			for key, t := range waiting {
				if time.Since(t) >= nameWait {
					start(addrs[key], "")
				}
			}
		case key := <-ended:
			delete(running, key)
			// macOS は探し直さないと、同じデバイスをもう一度知らせない
			select {
			case restart <- struct{}{}:
			default:
			}
		case err := <-scanErr:
			result = fmt.Errorf("デバイスを探せない：%w", err)
			break loop
		case <-ctx.Done():
			break loop
		}
	}
	// 切らずに終わると、デバイスは前の接続が続いていると思って広告を止めたままになり、
	// 次に起動したサーバーがつなげるまで 10 秒以上かかる（2026-10-13 の実測）
	cancel()
	<-scanDone
	wg.Wait()
	return result
}

// scanLoop は探し続ける。restart が来たら（または scanRefresh ごとに）探し直す
func scanLoop(ctx context.Context, adapter *bluetooth.Adapter, onScan func(*bluetooth.Adapter, bluetooth.ScanResult), restart <-chan struct{}, errc chan<- error) {
	for {
		done := make(chan error, 1)
		go func() { done <- adapter.Scan(onScan) }()

		var err error
		select {
		case err = <-done:
			// 止めていないのに終わった
			if err == nil {
				err = errors.New("探すのが止まった")
			}
		case <-restart:
			err = stopScan(adapter, done)
		case <-time.After(scanRefresh):
			err = stopScan(adapter, done)
		case <-ctx.Done():
			_ = stopScan(adapter, done)
			return
		}
		if err != nil {
			errc <- err
			return
		}
	}
}

// stopScan は探すのを止めて、Scan が戻るのを待つ。Scan が始まる前なら、始まるまで待ってから止める
func stopScan(adapter *bluetooth.Adapter, done <-chan error) error {
	for {
		if adapter.StopScan() == nil {
			return <-done
		}
		select {
		case err := <-done:
			return err
		case <-time.After(50 * time.Millisecond):
		}
	}
}

// watchDevice は 1 台につないで、切れるか ctx が終わるまで通知を受ける
func watchDevice(ctx context.Context, adapter *bluetooth.Adapter, addr bluetooth.Address, name string, out chan<- BleMessage) {
	dev, err := connect(ctx, adapter, addr)
	if err != nil {
		// 電源が切れているデバイスは毎回ここに来るので、本体には渡さず画面に出すだけにする
		fmt.Fprintf(os.Stderr, "%s: %v\n", name, err)
		return
	}
	connected, err := listen(ctx, dev, name, out)
	if err != nil {
		fmt.Fprintf(os.Stderr, "%s: %v\n", name, err)
	}
	disconnect(dev)
	if connected {
		send(ctx, out, BleMessage{Kind: BleDisconnected, Name: name})
	}
}

// connect は connectTimeout まで待ってつなぐ。OS が戻ってこないときのために、少し長めに自分でも待つ
func connect(ctx context.Context, adapter *bluetooth.Adapter, addr bluetooth.Address) (bluetooth.Device, error) {
	type result struct {
		dev bluetooth.Device
		err error
	}
	ch := make(chan result, 1)
	go func() {
		dev, err := adapter.Connect(addr, bluetooth.ConnectionParams{ConnectionTimeout: bluetooth.NewDuration(connectTimeout)})
		ch <- result{dev, err}
	}()
	select {
	case r := <-ch:
		if r.err != nil {
			return bluetooth.Device{}, fmt.Errorf("接続できない：%w", r.err)
		}
		return r.dev, nil
	case <-time.After(connectTimeout + 2*time.Second):
	case <-ctx.Done():
	}
	// あとからつながったら、すぐ切る
	go func() {
		if r := <-ch; r.err == nil {
			disconnect(r.dev)
		}
	}()
	return bluetooth.Device{}, errors.New("接続がタイムアウトした")
}

// listen はサービスを探して通知を受け続ける。一度でもつながったら true
func listen(ctx context.Context, dev bluetooth.Device, name string, out chan<- BleMessage) (bool, error) {
	svcs, err := dev.DiscoverServices([]bluetooth.UUID{serviceUUID})
	if err != nil {
		return false, fmt.Errorf("サービスが見つからない：%w", err)
	}
	chars, err := svcs[0].DiscoverCharacteristics([]bluetooth.UUID{eventUUID})
	if err != nil {
		return false, fmt.Errorf("イベントのキャラクタリスティックが無い：%w", err)
	}

	notes := make(chan []byte, 256)
	done := make(chan struct{})
	defer close(done)
	err = chars[0].EnableNotifications(func(buf []byte) {
		// buf は呼び出しのあとで書き換わることがあるので写す
		b := append([]byte(nil), buf...)
		select {
		case notes <- b:
		case <-done:
		}
	})
	if err != nil {
		return false, fmt.Errorf("通知を受けられない：%w", err)
	}
	send(ctx, out, BleMessage{Kind: BleConnected, Name: name})

	var buf LineBuffer
	alive := time.NewTicker(aliveCheck)
	defer alive.Stop()
	for {
		select {
		case b := <-notes:
			for _, line := range buf.Push(b) {
				send(ctx, out, BleMessage{Kind: BleLine, Name: name, Line: line})
			}
		case <-alive.C:
			ok, err := dev.Connected()
			if err != nil {
				return true, err
			}
			if !ok {
				return true, nil
			}
		case <-ctx.Done():
			return true, nil
		}
	}
}

// disconnect は 1 台につき 1 回だけ呼ぶ（Windows は 2 回呼ぶと壊れる）
func disconnect(dev bluetooth.Device) {
	_ = dev.Disconnect()
	// macOS の Disconnect は待たずに戻るので、切れるまで少し待つ（すぐ終わるとデバイスが切断に気づかないことがある）
	if runtime.GOOS != "darwin" {
		return
	}
	deadline := time.Now().Add(disconnectWait)
	for time.Now().Before(deadline) {
		if ok, err := dev.Connected(); err != nil || !ok {
			return
		}
		time.Sleep(50 * time.Millisecond)
	}
}

// send は本体に渡す。止めるところなら渡さない（本体はもう受け取っていない）
func send(ctx context.Context, out chan<- BleMessage, m BleMessage) {
	select {
	case out <- m:
	case <-ctx.Done():
	}
}

func mustUUID(s string) bluetooth.UUID {
	u, err := bluetooth.ParseUUID(s)
	if err != nil {
		panic(err)
	}
	return u
}
