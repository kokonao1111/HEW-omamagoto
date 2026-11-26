package main

import (
	"bytes"
	"testing"
)

func TestWholeLineInOneNotification(t *testing.T) {
	var b LineBuffer
	eq(t, b.Push([]byte("{\"event\":\"shake\"}\n")), []string{`{"event":"shake"}`})
}

func TestJoinsLineSplitAcrossNotifications(t *testing.T) {
	var b LineBuffer
	eq(t, len(b.Push([]byte(`{"event":`))), 0)
	eq(t, len(b.Push([]byte(`"knob",`))), 0)
	eq(t, b.Push([]byte("\"level\":2}\n")), []string{`{"event":"knob","level":2}`})
}

func TestTwoLinesAndStartOfNextInOneNotification(t *testing.T) {
	var b LineBuffer
	eq(t, b.Push([]byte("a\nb\nc")), []string{"a", "b"})
	eq(t, b.Push([]byte("d\n")), []string{"cd"})
}

func TestJoinsMultibyteCharSplitAcrossNotifications(t *testing.T) {
	var b LineBuffer
	s := []byte("塩\n")
	eq(t, len(b.Push(s[:1])), 0)
	eq(t, b.Push(s[1:]), []string{"塩"})
}

func TestDropsOverlongDataWithoutNewline(t *testing.T) {
	var b LineBuffer
	eq(t, len(b.Push(bytes.Repeat([]byte("x"), maxLine+1))), 0)
	eq(t, b.Push([]byte("ok\n")), []string{"ok"})
}
