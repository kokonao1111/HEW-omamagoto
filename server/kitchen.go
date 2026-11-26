// サーバーの本体：鍋の状態（要件定義書 5.9）
// デバイス・カメラからの 1 件を受けて状態を変え、Unity に出すイベントを返す。goroutine は使わない。
// 今あるのは調味料・火の強さ・切ったピース数・ゾーンに入った数・リセット。焼き加減・工程・採点は次の段階で足す。

package main

import (
	"math"
	"strings"
)

// HandledKind は 1 件を処理した結果の種類
type HandledKind int

const (
	// Accepted は受け付けた。Unity に出すイベントは Events（無いこともある）
	Accepted HandledKind = iota
	// Duplicate は同じものが 2 回届いた
	Duplicate
	// UnknownDevice は設定ファイルに無いデバイス。設定に MAC アドレスを足す
	UnknownDevice
	// NotForRole は役割に合わないイベント（塩のデバイスから来た knob など）
	NotForRole
)

type Handled struct {
	Kind   HandledKind
	Events []ServerEvent
	// NotForRole のときの役割
	Role Role
}

func accepted(events ...ServerEvent) Handled {
	return Handled{Kind: Accepted, Events: events}
}

type stamp struct{ ts, seq uint64 }

type zoneItem struct{ zone, item string }

type Kitchen struct {
	config Config
	// デバイスごとに、最後に受け付けた (ts, seq)
	last map[string]stamp
	// 調味料ごとの合計グラム数
	seasoning map[Role]float64
	// 火の強さ（0=切・1=弱・2=中・3=強）
	fire uint8
	// デバイスごとのバッテリー残量（%）。運営用画面で使う
	battery map[string]int32
	// カメラのつながり（ws の id）ごとに、最後に受け付けた seq
	cameraLast map[uint64]uint64
	// 野菜ごとの、まな板の上のピース数（最後の cut）
	pieces map[string]uint32
	// (ゾーン, 野菜) ごとに入った数の合計
	zones map[zoneItem]uint32
}

func NewKitchen(config Config) *Kitchen {
	return &Kitchen{
		config:     config,
		last:       map[string]stamp{},
		seasoning:  map[Role]float64{},
		battery:    map[string]int32{},
		cameraLast: map[uint64]uint64{},
		pieces:     map[string]uint32{},
		zones:      map[zoneItem]uint32{},
	}
}

// Handle はデバイスからの 1 件
func (k *Kitchen) Handle(msg DeviceMessage) Handled {
	role, ok := k.config.RoleOf(msg.Device)
	if !ok {
		return Handled{Kind: UnknownDevice}
	}
	if k.isDuplicate(msg) {
		return Handled{Kind: Duplicate}
	}
	device := strings.ToUpper(msg.Device)
	k.last[device] = stamp{msg.Ts, msg.Seq}

	switch e := msg.Event.(type) {
	case Hello, UnknownEvent:
		return accepted()
	case Battery:
		k.battery[device] = e.Percent
		return accepted()
	case Shake:
		if role != RoleSalt && role != RolePepper {
			break
		}
		amount := k.config.Season.GramsPerShake[role] * clamp01(e.Intensity)
		k.seasoning[role] += amount
		return accepted(SeasonOut{Item: string(role), AmountG: round2(amount), TotalG: round2(k.seasoning[role])})
	case Knob:
		if role != RoleKnob {
			break
		}
		// デバイスは段階が変わったときだけ送るが、つなぎ直したあとなどに同じ段階が来ても出さない
		if e.Level == k.fire {
			return accepted()
		}
		k.fire = e.Level
		return accepted(FireOut{Level: e.Level})
	}
	return Handled{Kind: NotForRole, Role: role}
}

// HandleCamera はカメラ（Python）からの 1 件。位置・切った・入れた・振り下ろしを Unity に流す。
// source はつながりの番号。カメラのプログラムが複数でも、起動し直しても、つながりごとに seq を見る
func (k *Kitchen) HandleCamera(source uint64, msg CameraMessage) Handled {
	if seq, ok := k.cameraLast[source]; ok && msg.Seq <= seq {
		return Handled{Kind: Duplicate}
	}
	k.cameraLast[source] = msg.Seq

	switch e := msg.Event.(type) {
	case Position:
		return accepted(PositionOut{Item: e.Item, Tip: e.Tip, Grip: e.Grip, X: e.X, Y: e.Y, Rot: e.Rot, Visible: e.Visible})
	case Cut:
		k.pieces[e.Item] = e.Count
		return accepted(CutOut{Item: e.Item, Count: e.Count})
	case Add:
		k.zones[zoneItem{e.Zone, e.Item}] += e.Count
		return accepted(AddOut{Item: e.Item, Zone: e.Zone, Count: e.Count})
	case Swing:
		return accepted(SwingOut{Item: e.Item})
	}
	// 手の有無（Hand）は、あとで無操作のリセットに使う。Unity には出さない
	return accepted()
}

// Reset は来場者が入れ替わったとき。調味料・ピース数・ゾーンを空にする。
// 火はつまみの実際の位置なので戻さない。ついていたら、画面に今の火をもう一度伝える
func (k *Kitchen) Reset() []ServerEvent {
	clear(k.seasoning)
	clear(k.pieces)
	clear(k.zones)
	return append([]ServerEvent{ResetOut{}}, k.Sync()...)
}

// Sync は画面がつながったとき（起動し直したときなど）に、今の状態に合わせるためのイベント
func (k *Kitchen) Sync() []ServerEvent {
	if k.fire > 0 {
		return []ServerEvent{FireOut{Level: k.fire}}
	}
	return nil
}

// isDuplicate：同じデバイスから同じ起動のうちに、受け付けた連番以下が来たら重複。
// デバイスが再起動すると seq は 1 から、ts は 0 から数え直すので、ts が戻っていたら再起動とみなして受け付ける
func (k *Kitchen) isDuplicate(msg DeviceMessage) bool {
	last, ok := k.last[strings.ToUpper(msg.Device)]
	return ok && msg.Ts >= last.ts && msg.Seq <= last.seq
}

func (k *Kitchen) SeasoningG(role Role) float64 { return k.seasoning[role] }

func (k *Kitchen) Fire() uint8 { return k.fire }

func (k *Kitchen) Battery() map[string]int32 { return k.battery }

func (k *Kitchen) Pieces(item string) uint32 { return k.pieces[item] }

func (k *Kitchen) InZone(zone, item string) uint32 { return k.zones[zoneItem{zone, item}] }

// round2 はログと Unity で読みやすいように小数 2 桁にする
func round2(v float64) float64 {
	return math.Round(v*100) / 100
}

func clamp01(v float64) float64 {
	return math.Max(0, math.Min(1, v))
}
