// イベントの型（要件定義書 6.1〜6.4）
// デバイスから届く JSON を DeviceMessage に、カメラから届く JSON を CameraMessage に、サーバーが出すものを ServerEvent にする。

package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"strconv"
)

// DeviceMessage はデバイスから届く 1 件（6.1 の共通ヘッダ + 6.2 の中身）
type DeviceMessage struct {
	// MAC アドレス
	Device string
	// デバイスの起動からのミリ秒
	Ts uint64
	// デバイスごとの連番。デバイスが再起動すると 1 から数え直す
	Seq   uint64
	Event DeviceEvent
}

// DeviceEvent は次のどれか。知らない種類は UnknownEvent になる
type DeviceEvent any

type Hello struct {
	Board int32
	IMU   bool
}

// Battery の percent は、取れないときに負の値が来ることがある
type Battery struct{ Percent int32 }

type Shake struct {
	Intensity float64
	Tilt      int32
}

// Knob の level は 0=切・1=弱・2=中・3=強
type Knob struct {
	Level uint8
	Angle int32
}

type Tilt struct{ Tilt int32 }

type Switch struct {
	Name string
	On   bool
}

// UnknownEvent は知らない種類（新しいファームが足したもの など）。読めないことで止まらないように受け流す
type UnknownEvent struct{}

// ParseDeviceMessage は 1 行の JSON を読む。共通ヘッダや、その種類に要る項目が無ければエラー
func ParseDeviceMessage(line string) (DeviceMessage, error) {
	f, err := objectFields(line)
	if err != nil {
		return DeviceMessage{}, err
	}
	var m DeviceMessage
	if m.Device, err = need[string](f, "device"); err != nil {
		return m, err
	}
	if m.Ts, err = need[uint64](f, "ts"); err != nil {
		return m, err
	}
	if m.Seq, err = need[uint64](f, "seq"); err != nil {
		return m, err
	}
	kind, err := need[string](f, "event")
	if err != nil {
		return m, err
	}
	m.Event, err = deviceEvent(kind, f)
	return m, err
}

func deviceEvent(kind string, f fields) (DeviceEvent, error) {
	var err error
	switch kind {
	case "hello":
		var e Hello
		if e.Board, err = need[int32](f, "board"); err != nil {
			return nil, err
		}
		e.IMU, err = need[bool](f, "imu")
		return e, err
	case "battery":
		var e Battery
		e.Percent, err = need[int32](f, "percent")
		return e, err
	case "shake":
		var e Shake
		if e.Intensity, err = need[float64](f, "intensity"); err != nil {
			return nil, err
		}
		e.Tilt, err = need[int32](f, "tilt")
		return e, err
	case "knob":
		var e Knob
		if e.Level, err = need[uint8](f, "level"); err != nil {
			return nil, err
		}
		e.Angle, err = need[int32](f, "angle")
		return e, err
	case "tilt":
		var e Tilt
		e.Tilt, err = need[int32](f, "tilt")
		return e, err
	case "switch":
		var e Switch
		if e.Name, err = need[string](f, "name"); err != nil {
			return nil, err
		}
		e.On, err = need[bool](f, "on")
		return e, err
	}
	return UnknownEvent{}, nil
}

// CameraMessage はカメラ（Python）から届く 1 件（6.1 の共通ヘッダ + 6.3 の中身）
type CameraMessage struct {
	// UNIX 時刻（ミリ秒）
	Ts int64
	// カメラのプログラムが起動してからの連番。起動し直すと 1 から
	Seq   uint64
	Event CameraEvent
}

// CameraEvent は次のどれか。知らない種類は UnknownEvent になる
type CameraEvent any

// Position は物の位置（まな板の範囲で 0〜1）。包丁は tip・grip、それ以外は x・y・rot。見失ったら visible: false だけ
type Position struct {
	Item      string
	Tip, Grip *[2]float64
	X, Y, Rot *float64
	Visible   bool
}

// Cut は切った。count は手がどいたときに確定したピース数
type Cut struct {
	Item  string
	Count uint32
}

// Add はゾーン（pan・plate）に入った。count は増えた数
type Add struct {
	Item  string
	Zone  string
	Count uint32
}

// Swing は包丁を振り下ろした（演出用）
type Swing struct{ Item string }

// Hand は手が映っているか
type Hand struct{ Present bool }

func ParseCameraMessage(line string) (CameraMessage, error) {
	f, err := objectFields(line)
	if err != nil {
		return CameraMessage{}, err
	}
	var m CameraMessage
	if m.Ts, err = need[int64](f, "ts"); err != nil {
		return m, err
	}
	if m.Seq, err = need[uint64](f, "seq"); err != nil {
		return m, err
	}
	kind, err := need[string](f, "event")
	if err != nil {
		return m, err
	}
	m.Event, err = cameraEvent(kind, f)
	return m, err
}

func cameraEvent(kind string, f fields) (CameraEvent, error) {
	var err error
	switch kind {
	case "position":
		e := Position{Visible: true}
		if e.Item, err = need[string](f, "item"); err != nil {
			return nil, err
		}
		if e.Tip, err = point(f, "tip"); err != nil {
			return nil, err
		}
		if e.Grip, err = point(f, "grip"); err != nil {
			return nil, err
		}
		if e.X, err = maybe[float64](f, "x"); err != nil {
			return nil, err
		}
		if e.Y, err = maybe[float64](f, "y"); err != nil {
			return nil, err
		}
		if e.Rot, err = maybe[float64](f, "rot"); err != nil {
			return nil, err
		}
		// visible が無ければ見えている
		if _, ok := f["visible"]; ok {
			e.Visible, err = need[bool](f, "visible")
		}
		return e, err
	case "cut":
		var e Cut
		if e.Item, err = need[string](f, "item"); err != nil {
			return nil, err
		}
		e.Count, err = need[uint32](f, "count")
		return e, err
	case "add":
		var e Add
		if e.Item, err = need[string](f, "item"); err != nil {
			return nil, err
		}
		if e.Zone, err = need[string](f, "zone"); err != nil {
			return nil, err
		}
		e.Count, err = need[uint32](f, "count")
		return e, err
	case "swing":
		var e Swing
		e.Item, err = need[string](f, "item")
		return e, err
	case "hand":
		var e Hand
		e.Present, err = need[bool](f, "present")
		return e, err
	}
	return UnknownEvent{}, nil
}

// ServerEvent はサーバーが Unity に出すもの（6.4）。次のどれか
type ServerEvent any

// PositionOut は物の位置。カメラから来たものをそのまま流す（無い項目は出さない）
type PositionOut struct {
	Item      string
	Tip, Grip *[2]float64
	X, Y, Rot *float64
	Visible   bool
}

// CutOut は切った（count は今のピース数）
type CutOut struct {
	Item  string
	Count uint32
}

// AddOut はゾーンに入った（count は増えた数）
type AddOut struct {
	Item  string
	Zone  string
	Count uint32
}

// SwingOut は包丁を振り下ろした
type SwingOut struct{ Item string }

// SeasonOut は調味料が入った。item は役割の名前（salt・pepper）
type SeasonOut struct {
	Item    string
	AmountG float64
	TotalG  float64
}

// FireOut は火の強さが変わった
type FireOut struct{ Level uint8 }

// ResetOut は最初の状態に戻した
type ResetOut struct{}

// Stamp は 6.1 の共通ヘッダを付けた JSON にする。この形でログに書き、Unity に送る
// 項目の順は device・ts・seq・event・中身（Rust 版と同じ）
func Stamp(ev ServerEvent, ts int64, seq uint64) ([]byte, error) {
	body, err := json.Marshal(eventJSON(ev))
	if err != nil {
		return nil, err
	}
	head := fmt.Sprintf(`{"device":"server","ts":%d,"seq":%d,`, ts, seq)
	return append([]byte(head), body[1:]...), nil
}

func eventJSON(ev ServerEvent) any {
	switch e := ev.(type) {
	case PositionOut:
		return struct {
			Event   string     `json:"event"`
			Item    string     `json:"item"`
			Tip     *[2]jFloat `json:"tip,omitempty"`
			Grip    *[2]jFloat `json:"grip,omitempty"`
			X       *jFloat    `json:"x,omitempty"`
			Y       *jFloat    `json:"y,omitempty"`
			Rot     *jFloat    `json:"rot,omitempty"`
			Visible bool       `json:"visible"`
		}{"position", e.Item, pair(e.Tip), pair(e.Grip), one(e.X), one(e.Y), one(e.Rot), e.Visible}
	case CutOut:
		return struct {
			Event string `json:"event"`
			Item  string `json:"item"`
			Count uint32 `json:"count"`
		}{"cut", e.Item, e.Count}
	case AddOut:
		return struct {
			Event string `json:"event"`
			Item  string `json:"item"`
			Zone  string `json:"zone"`
			Count uint32 `json:"count"`
		}{"add", e.Item, e.Zone, e.Count}
	case SwingOut:
		return struct {
			Event string `json:"event"`
			Item  string `json:"item"`
		}{"swing", e.Item}
	case SeasonOut:
		return struct {
			Event   string `json:"event"`
			Item    string `json:"item"`
			AmountG jFloat `json:"amount_g"`
			TotalG  jFloat `json:"total_g"`
		}{"season", e.Item, jFloat(e.AmountG), jFloat(e.TotalG)}
	case FireOut:
		return struct {
			Event string `json:"event"`
			Level uint8  `json:"level"`
		}{"fire", e.Level}
	case ResetOut:
		return struct {
			Event string `json:"event"`
		}{"reset"}
	}
	panic(fmt.Sprintf("知らない ServerEvent %T", ev))
}

// jFloat は小数を Rust 版と同じ形で書く（整数でも 5.0 のように .0 を付ける）
type jFloat float64

func (f jFloat) MarshalJSON() ([]byte, error) {
	v := float64(f)
	if math.IsNaN(v) || math.IsInf(v, 0) {
		return []byte("null"), nil
	}
	b, _ := json.Marshal(v)
	if !bytes.ContainsAny(b, ".eE") {
		b = append(b, ".0"...)
	}
	return b, nil
}

func one(p *float64) *jFloat {
	if p == nil {
		return nil
	}
	v := jFloat(*p)
	return &v
}

func pair(p *[2]float64) *[2]jFloat {
	if p == nil {
		return nil
	}
	return &[2]jFloat{jFloat(p[0]), jFloat(p[1])}
}

// --- JSON を読むための小さな道具 ---

type fields map[string]json.RawMessage

// objectFields は 1 行の JSON（オブジェクト）を、項目名 → 中身に分ける
func objectFields(line string) (fields, error) {
	var f fields
	if err := json.Unmarshal([]byte(line), &f); err != nil {
		var syntax *json.SyntaxError
		if errors.As(err, &syntax) {
			return nil, fmt.Errorf("JSON として読めない（%d バイト目）", syntax.Offset)
		}
		return nil, errors.New("JSON のオブジェクトではない")
	}
	if f == nil {
		return nil, errors.New("JSON のオブジェクトではない")
	}
	return f, nil
}

func isNull(raw json.RawMessage) bool {
	return bytes.Equal(bytes.TrimSpace(raw), []byte("null"))
}

// need は要る項目を読む。無い・null・型が違うときはエラー
func need[T any](f fields, name string) (T, error) {
	var v T
	raw, ok := f[name]
	if !ok {
		return v, fmt.Errorf("%s が無い", name)
	}
	if isNull(raw) {
		return v, fmt.Errorf("%s が null", name)
	}
	if err := json.Unmarshal(raw, &v); err != nil {
		return v, fmt.Errorf("%s の型が違う（%s）", name, raw)
	}
	return v, nil
}

// maybe は無くてもよい項目を読む。無い・null なら nil
func maybe[T any](f fields, name string) (*T, error) {
	raw, ok := f[name]
	if !ok || isNull(raw) {
		return nil, nil
	}
	v, err := need[T](f, name)
	return &v, err
}

// point は [x, y] の 2 つの数を読む。無い・null なら nil
func point(f fields, name string) (*[2]float64, error) {
	v, err := maybe[[]float64](f, name)
	if err != nil || v == nil {
		return nil, err
	}
	if len(*v) != 2 {
		return nil, fmt.Errorf("%s は 2 つの数（%d 個あった）", name, len(*v))
	}
	return &[2]float64{(*v)[0], (*v)[1]}, nil
}

// formatNum は表示用に、小数をいちばん短い形で書く（0.5 → 0.5、5.0 → 5）
func formatNum(v float64) string {
	return strconv.FormatFloat(v, 'f', -1, 64)
}
