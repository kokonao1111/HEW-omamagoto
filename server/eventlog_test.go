package main

import (
	"encoding/json"
	"os"
	"strings"
	"testing"
)

func TestWritesOneJSONPerLine(t *testing.T) {
	log, err := CreateEventLog(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	defer log.Close()
	log.Note("omamagoto-5C3C 接続")
	log.Incoming("omamagoto-5C3C", `{"event":"shake","intensity":1,"tilt":70,"device":"x","ts":1,"seq":1}`)
	log.Incoming("omamagoto-5C3C", "壊れた行")
	stamped, _ := Stamp(FireOut{Level: 2}, 1, 1)
	log.Outgoing(stamped)

	text, err := os.ReadFile(log.Path())
	if err != nil {
		t.Fatal(err)
	}
	var lines []map[string]any
	for _, l := range strings.Split(strings.TrimSpace(string(text)), "\n") {
		var v map[string]any
		if err := json.Unmarshal([]byte(l), &v); err != nil {
			t.Fatalf("%s: %v", l, err)
		}
		lines = append(lines, v)
	}
	eq(t, len(lines), 4)
	eq(t, lines[0]["dir"], "note")
	eq(t, lines[1]["msg"].(map[string]any)["event"], "shake")
	eq(t, lines[2]["msg"], "壊れた行")
	eq(t, lines[3]["msg"].(map[string]any)["event"], "fire")
	eq(t, lines[3]["msg"].(map[string]any)["level"], 2.0)
}

func TestLogLineLooksLikeRustVersion(t *testing.T) {
	// 項目は名前の順。数はそのまま（0.46・大きな整数・5.0）残る
	log, err := CreateEventLog(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	defer log.Close()
	log.Incoming("omamagoto-5C3C", `{"device":"AC:27:6E:D3:5C:3C","event":"shake","intensity":0.46,"seq":4,"tilt":52,"ts":5262}`)
	stamped, _ := Stamp(SeasonOut{Item: "salt", AmountG: 0.5, TotalG: 5}, 1795665474173, 1)
	log.Outgoing(stamped)
	log.Note("a < b & c")
	log.Incoming("camera#2", `{"x":0.50,"rot":15,"r":15.0,"big":1760000000123,"e":1e3,"neg":-3,"list":[0.10,2]}`)

	text, _ := os.ReadFile(log.Path())
	lines := strings.Split(strings.TrimSpace(string(text)), "\n")
	cut := func(l string) string { return l[:strings.LastIndex(l, `,"t":`)] }
	eq(t, cut(lines[0]), `{"dir":"in","from":"omamagoto-5C3C","msg":{"device":"AC:27:6E:D3:5C:3C","event":"shake","intensity":0.46,"seq":4,"tilt":52,"ts":5262}`)
	eq(t, cut(lines[1]), `{"dir":"out","msg":{"amount_g":0.5,"device":"server","event":"season","item":"salt","seq":1,"total_g":5.0,"ts":1795665474173}`)
	eq(t, strings.HasPrefix(lines[2], `{"dir":"note","t":`) && strings.HasSuffix(lines[2], `"text":"a < b & c"}`), true)
	eq(t, cut(lines[3]), `{"dir":"in","from":"camera#2","msg":{"big":1760000000123,"e":1000.0,"list":[0.1,2],"neg":-3,"r":15.0,"rot":15,"x":0.5}`)
}
