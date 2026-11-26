package main

import (
	"encoding/json"
	"reflect"
	"testing"
)

func parseDevice(t *testing.T, line string) DeviceMessage {
	t.Helper()
	m, err := ParseDeviceMessage(line)
	if err != nil {
		t.Fatalf("%s: %v", line, err)
	}
	return m
}

func parseCamera(t *testing.T, line string) CameraMessage {
	t.Helper()
	m, err := ParseCameraMessage(line)
	if err != nil {
		t.Fatalf("%s: %v", line, err)
	}
	return m
}

func eq(t *testing.T, got, want any) {
	t.Helper()
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("\n got  %#v\n want %#v", got, want)
	}
}

func f64(v float64) *float64 { return &v }

func xy(x, y float64) *[2]float64 { return &[2]float64{x, y} }

func TestParsesRequirementsExamples(t *testing.T) {
	// 要件定義書 6.2 の例と、今のファームが出す形
	m := parseDevice(t, `{ "device": "AA:BB:CC:DD:EE:FF", "ts": 120340, "seq": 41, "event": "shake", "intensity": 0.8, "tilt": 72 }`)
	eq(t, m.Device, "AA:BB:CC:DD:EE:FF")
	eq(t, [2]uint64{m.Ts, m.Seq}, [2]uint64{120340, 41})
	eq(t, m.Event, Shake{Intensity: 0.8, Tilt: 72})

	m = parseDevice(t, `{"event":"knob","level":2,"angle":82,"device":"AC:27:6E:D3:5C:3C","ts":20410,"seq":4}`)
	eq(t, m.Event, Knob{Level: 2, Angle: 82})

	m = parseDevice(t, `{"event":"battery","percent":63,"device":"x","ts":1,"seq":2}`)
	eq(t, m.Event, Battery{Percent: 63})

	m = parseDevice(t, `{"event":"hello","board":26,"imu":true,"device":"x","ts":765,"seq":1}`)
	eq(t, m.Event, Hello{Board: 26, IMU: true})

	m = parseDevice(t, `{ "device": "x", "ts": 8000, "seq": 9, "event": "switch", "name": "door", "on": true }`)
	eq(t, m.Event, Switch{Name: "door", On: true})
}

func TestUnknownEventDoesNotFail(t *testing.T) {
	m := parseDevice(t, `{"event":"sparkle","color":"red","device":"x","ts":1,"seq":1}`)
	eq(t, m.Event, UnknownEvent{})
	// 知らない種類なら、ほかの項目の型は見ない
	m = parseDevice(t, `{"event":"sparkle","level":"high","device":"x","ts":1,"seq":1}`)
	eq(t, m.Event, UnknownEvent{})
}

func TestMissingHeaderFails(t *testing.T) {
	if _, err := ParseDeviceMessage(`{"event":"battery","percent":1}`); err == nil {
		t.Fatal("共通ヘッダが無いのに読めた")
	}
}

func TestBrokenDeviceLinesFail(t *testing.T) {
	for _, line := range []string{
		`壊れた行`,
		`[1, 2]`,
		`null`,
		// 種類に要る項目が無い・null・型が違う
		`{"event":"shake","intensity":0.8,"device":"x","ts":1,"seq":1}`,
		`{"event":"shake","intensity":null,"tilt":1,"device":"x","ts":1,"seq":1}`,
		`{"event":"knob","level":300,"angle":1,"device":"x","ts":1,"seq":1}`,
		`{"event":"knob","level":1.5,"angle":1,"device":"x","ts":1,"seq":1}`,
		`{"event":"battery","percent":1,"device":"x","ts":-1,"seq":1}`,
		`{"event":"battery","percent":1,"device":5,"ts":1,"seq":1}`,
		`{"percent":1,"device":"x","ts":1,"seq":1}`,
	} {
		if _, err := ParseDeviceMessage(line); err == nil {
			t.Errorf("読めてしまった：%s", line)
		}
	}
}

func TestParsesCameraExamples(t *testing.T) {
	// 要件定義書 6.3 の例と、camera/logic.py が出す形
	m := parseCamera(t, `{ "device": "camera", "ts": 1760000000123, "seq": 5001, "event": "position", "item": "knife", "tip": [0.31, 0.12], "grip": [0.55, 0.05], "visible": true, "cam": 0 }`)
	eq(t, m.Ts, int64(1760000000123))
	eq(t, m.Seq, uint64(5001))
	eq(t, m.Event, Position{Item: "knife", Tip: xy(0.31, 0.12), Grip: xy(0.55, 0.05), Visible: true})

	m = parseCamera(t, `{ "device": "camera", "ts": 1, "seq": 2, "event": "position", "item": "carrot", "x": 0.42, "y": 0.50, "rot": 15, "visible": true }`)
	eq(t, m.Event, Position{Item: "carrot", X: f64(0.42), Y: f64(0.5), Rot: f64(15), Visible: true})

	m = parseCamera(t, `{ "device": "camera", "ts": 1, "seq": 3, "event": "position", "item": "knife", "visible": false }`)
	eq(t, m.Event, Position{Item: "knife", Visible: false})

	m = parseCamera(t, `{ "device": "camera", "ts": 1, "seq": 4, "event": "cut", "item": "carrot", "count": 2 }`)
	eq(t, m.Event, Cut{Item: "carrot", Count: 2})
	m = parseCamera(t, `{ "device": "camera", "ts": 1, "seq": 5, "event": "add", "item": "carrot", "zone": "pan", "count": 1 }`)
	eq(t, m.Event, Add{Item: "carrot", Zone: "pan", Count: 1})
	m = parseCamera(t, `{ "device": "camera", "ts": 1, "seq": 6, "event": "swing", "item": "knife" }`)
	eq(t, m.Event, Swing{Item: "knife"})
	m = parseCamera(t, `{ "device": "camera", "ts": 1, "seq": 7, "event": "hand", "present": false }`)
	eq(t, m.Event, Hand{Present: false})
	m = parseCamera(t, `{ "device": "camera", "ts": 1, "seq": 8, "event": "sparkle" }`)
	eq(t, m.Event, UnknownEvent{})
}

func TestCameraPositionDetails(t *testing.T) {
	// visible が無ければ見えている。null の項目は無いのと同じ
	m := parseCamera(t, `{"ts":1,"seq":1,"event":"position","item":"carrot","x":0.1,"y":null}`)
	eq(t, m.Event, Position{Item: "carrot", X: f64(0.1), Visible: true})
	// tip は 2 つの数でないと読めない
	for _, line := range []string{
		`{"ts":1,"seq":1,"event":"position","item":"knife","tip":[0.1]}`,
		`{"ts":1,"seq":1,"event":"position","item":"knife","tip":[0.1,0.2,0.3]}`,
		`{"ts":1,"seq":1,"event":"position","item":"knife","visible":null}`,
		`{"ts":1,"seq":1,"event":"position"}`,
	} {
		if _, err := ParseCameraMessage(line); err == nil {
			t.Errorf("読めてしまった：%s", line)
		}
	}
}

// sameJSON は、項目の順を気にせずに同じ中身か比べる
func sameJSON(t *testing.T, got []byte, want string) {
	t.Helper()
	var g, w any
	if err := json.Unmarshal(got, &g); err != nil {
		t.Fatalf("%s: %v", got, err)
	}
	if err := json.Unmarshal([]byte(want), &w); err != nil {
		t.Fatalf("%s: %v", want, err)
	}
	if !reflect.DeepEqual(g, w) {
		t.Fatalf("\n got  %s\n want %s", got, want)
	}
}

func TestStampedEventsMatchRequirements(t *testing.T) {
	// 要件定義書 6.4 の例と同じ中身になる
	check := func(ev ServerEvent, want string) {
		t.Helper()
		b, err := Stamp(ev, 1, 9)
		if err != nil {
			t.Fatal(err)
		}
		var w map[string]any
		json.Unmarshal([]byte(want), &w)
		w["device"], w["ts"], w["seq"] = "server", 1, 9
		wb, _ := json.Marshal(w)
		sameJSON(t, b, string(wb))
	}
	check(PositionOut{Item: "knife", Tip: xy(0.31, 0.12), Grip: xy(0.55, 0.05), Visible: true},
		`{ "event": "position", "item": "knife", "tip": [0.31, 0.12], "grip": [0.55, 0.05], "visible": true }`)
	check(PositionOut{Item: "knife", Visible: false}, `{ "event": "position", "item": "knife", "visible": false }`)
	check(CutOut{Item: "carrot", Count: 2}, `{ "event": "cut", "item": "carrot", "count": 2 }`)
	check(AddOut{Item: "carrot", Zone: "pan", Count: 1}, `{ "event": "add", "item": "carrot", "zone": "pan", "count": 1 }`)
	check(SwingOut{Item: "knife"}, `{ "event": "swing", "item": "knife" }`)
	check(FireOut{Level: 2}, `{ "event": "fire", "level": 2 }`)
	check(ResetOut{}, `{ "event": "reset" }`)
}

func TestStampedServerEventMatchesRequirements(t *testing.T) {
	b, err := Stamp(SeasonOut{Item: "salt", AmountG: 0.5, TotalG: 3.2}, 1760000005000, 903)
	if err != nil {
		t.Fatal(err)
	}
	// 要件定義書 6.4 の例と同じ中身
	sameJSON(t, b, `{ "device": "server", "ts": 1760000005000, "seq": 903, "event": "season", "item": "salt", "amount_g": 0.5, "total_g": 3.2 }`)
}

func TestStampedTextIsSameAsRustVersion(t *testing.T) {
	// 項目の順と小数の書き方（5.0・15.0）も Rust 版と同じ
	b, _ := Stamp(SeasonOut{Item: "salt", AmountG: 0.5, TotalG: 5}, 1, 2)
	eq(t, string(b), `{"device":"server","ts":1,"seq":2,"event":"season","item":"salt","amount_g":0.5,"total_g":5.0}`)
	b, _ = Stamp(PositionOut{Item: "carrot", X: f64(0.42), Y: f64(0.5), Rot: f64(15), Visible: true}, 1, 3)
	eq(t, string(b), `{"device":"server","ts":1,"seq":3,"event":"position","item":"carrot","x":0.42,"y":0.5,"rot":15.0,"visible":true}`)
	b, _ = Stamp(PositionOut{Item: "knife", Tip: xy(0, 1), Visible: true}, 1, 4)
	eq(t, string(b), `{"device":"server","ts":1,"seq":4,"event":"position","item":"knife","tip":[0.0,1.0],"visible":true}`)
}
