package main

import (
	"math"
	"testing"
)

const (
	salt   = "AA:BB:CC:DD:EE:01"
	pepper = "AA:BB:CC:DD:EE:02"
	knob   = "AA:BB:CC:DD:EE:03"
)

func kitchen(t *testing.T) *Kitchen {
	t.Helper()
	return NewKitchen(mustConfig(t, `[roles]
"`+salt+`" = "salt"
"`+pepper+`" = "pepper"
"`+knob+`" = "knob"
[season]
grams_per_shake = { salt = 0.5, pepper = 0.2 }
`))
}

func msg(device string, ts, seq uint64, ev DeviceEvent) DeviceMessage {
	return DeviceMessage{Device: device, Ts: ts, Seq: seq, Event: ev}
}

func shake(intensity float64) DeviceEvent { return Shake{Intensity: intensity, Tilt: 70} }

func knobAt(level uint8) DeviceEvent { return Knob{Level: level, Angle: 0} }

func near(t *testing.T, got, want float64) {
	t.Helper()
	if math.Abs(got-want) > 1e-9 {
		t.Fatalf("got %v want %v", got, want)
	}
}

func isAccepted(t *testing.T, h Handled) {
	t.Helper()
	if h.Kind != Accepted {
		t.Fatalf("受け付けなかった：%#v", h)
	}
}

func TestShakesAddGramsByIntensity(t *testing.T) {
	k := kitchen(t)
	eq(t, k.Handle(msg(salt, 100, 1, shake(1.0))), accepted(SeasonOut{Item: "salt", AmountG: 0.5, TotalG: 0.5}))
	// 弱い振り（持ち替えの誤検知など）は少ししか入らない
	eq(t, k.Handle(msg(salt, 200, 2, shake(0.1))), accepted(SeasonOut{Item: "salt", AmountG: 0.05, TotalG: 0.55}))
	k.Handle(msg(pepper, 300, 1, shake(1.0)))
	near(t, k.SeasoningG(RoleSalt), 0.55)
	near(t, k.SeasoningG(RolePepper), 0.2)
}

func TestTenShakesMakeFiveGramsOfSalt(t *testing.T) {
	k := kitchen(t)
	for i := uint64(1); i <= 10; i++ {
		k.Handle(msg(salt, i*300, i, shake(1.0)))
	}
	near(t, k.SeasoningG(RoleSalt), 5.0)
}

func TestKnobChangesFireOnlyWhenLevelChanges(t *testing.T) {
	k := kitchen(t)
	eq(t, k.Handle(msg(knob, 100, 1, knobAt(2))), accepted(FireOut{Level: 2}))
	eq(t, k.Handle(msg(knob, 200, 2, knobAt(2))), accepted())
	eq(t, k.Handle(msg(knob, 300, 3, knobAt(0))), accepted(FireOut{Level: 0}))
	eq(t, k.Fire(), uint8(0))
}

func TestIgnoresEventsThatDoNotFitTheRole(t *testing.T) {
	// 振ると knob も出る（2026-10-13 の実機）。塩のデバイスの knob は火を変えない
	k := kitchen(t)
	eq(t, k.Handle(msg(salt, 100, 1, knobAt(3))), Handled{Kind: NotForRole, Role: RoleSalt})
	eq(t, k.Handle(msg(knob, 200, 1, shake(1.0))), Handled{Kind: NotForRole, Role: RoleKnob})
	eq(t, k.Fire(), uint8(0))
	eq(t, k.SeasoningG(RoleSalt), 0.0)
}

func TestDropsDuplicates(t *testing.T) {
	k := kitchen(t)
	k.Handle(msg(salt, 100, 5, shake(1.0)))
	eq(t, k.Handle(msg(salt, 100, 5, shake(1.0))), Handled{Kind: Duplicate})
	eq(t, k.Handle(msg(salt, 150, 4, shake(1.0))), Handled{Kind: Duplicate})
	near(t, k.SeasoningG(RoleSalt), 0.5)
}

func TestAcceptsEventsAfterDeviceRestart(t *testing.T) {
	k := kitchen(t)
	k.Handle(msg(salt, 90_000, 120, shake(1.0)))
	// 再起動：ts も seq も小さくなる
	isAccepted(t, k.Handle(msg(salt, 800, 1, shake(1.0))))
	isAccepted(t, k.Handle(msg(salt, 900, 2, shake(1.0))))
	near(t, k.SeasoningG(RoleSalt), 1.5)
}

func TestUnknownDeviceIsReported(t *testing.T) {
	k := kitchen(t)
	eq(t, k.Handle(msg("11:22:33:44:55:66", 1, 1, shake(1.0))), Handled{Kind: UnknownDevice})
}

func cam(ts int64, seq uint64, ev CameraEvent) CameraMessage {
	return CameraMessage{Ts: ts, Seq: seq, Event: ev}
}

func TestCameraEventsGoToUnity(t *testing.T) {
	k := kitchen(t)
	knife := Position{Item: "knife", Tip: xy(0.3, 0.1), Grip: xy(0.5, 0.0), Visible: true}
	eq(t, k.HandleCamera(1, cam(1000, 1, knife)),
		accepted(PositionOut{Item: "knife", Tip: xy(0.3, 0.1), Grip: xy(0.5, 0.0), Visible: true}))
	eq(t, k.HandleCamera(1, cam(1100, 2, Cut{Item: "carrot", Count: 3})), accepted(CutOut{Item: "carrot", Count: 3}))
	eq(t, k.HandleCamera(1, cam(1200, 3, Add{Item: "carrot", Zone: "pan", Count: 2})),
		accepted(AddOut{Item: "carrot", Zone: "pan", Count: 2}))
	k.HandleCamera(1, cam(1300, 4, Add{Item: "carrot", Zone: "pan", Count: 1}))
	eq(t, k.Pieces("carrot"), uint32(3))
	eq(t, k.InZone("pan", "carrot"), uint32(3))
	// 手の有無は画面に出さない
	eq(t, k.HandleCamera(1, cam(1400, 5, Hand{Present: true})), accepted())
}

func TestCameraDuplicatesArePerConnection(t *testing.T) {
	k := kitchen(t)
	swing := Swing{Item: "knife"}
	k.HandleCamera(1, cam(1000, 10, swing))
	eq(t, k.HandleCamera(1, cam(1000, 10, swing)), Handled{Kind: Duplicate})
	// 別のカメラのプログラム（つながりが別）は、seq が小さくても受け付ける
	isAccepted(t, k.HandleCamera(2, cam(1000, 3, swing)))
	isAccepted(t, k.HandleCamera(1, cam(1001, 11, swing)))
	isAccepted(t, k.HandleCamera(2, cam(1001, 4, swing)))
	// 起動し直してつなぎ直した（新しいつながり）：seq は 1 から
	isAccepted(t, k.HandleCamera(3, cam(5000, 1, swing)))
}

func TestResetClearsFoodButKeepsFire(t *testing.T) {
	k := kitchen(t)
	k.Handle(msg(salt, 100, 1, shake(1.0)))
	k.Handle(msg(knob, 100, 1, knobAt(2)))
	k.HandleCamera(1, cam(1000, 1, Cut{Item: "carrot", Count: 4}))
	k.HandleCamera(1, cam(1100, 2, Add{Item: "carrot", Zone: "pan", Count: 4}))
	// 火はつまみの位置のままなので、リセットのあとに今の火をもう一度伝える
	eq(t, k.Reset(), []ServerEvent{ResetOut{}, FireOut{Level: 2}})
	eq(t, k.SeasoningG(RoleSalt), 0.0)
	eq(t, k.Pieces("carrot"), uint32(0))
	eq(t, k.InZone("pan", "carrot"), uint32(0))
	eq(t, k.Fire(), uint8(2))
	// 次の人の塩は 0 から数える
	eq(t, k.Handle(msg(salt, 200, 2, shake(1.0))), accepted(SeasonOut{Item: "salt", AmountG: 0.5, TotalG: 0.5}))
	k.Handle(msg(knob, 300, 2, knobAt(0)))
	eq(t, k.Reset(), []ServerEvent{ResetOut{}})
}

func TestKeepsBatteryPerDevice(t *testing.T) {
	k := kitchen(t)
	k.Handle(msg(salt, 100, 1, Battery{Percent: 63}))
	eq(t, k.Battery()[salt], int32(63))
}
