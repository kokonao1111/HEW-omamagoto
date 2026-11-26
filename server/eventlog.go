// 受け取ったものと出したものをすべてファイルに残す（要件定義書 5.9。展示後の分析とデバッグ用）
// 1 行 1 JSON。dir は in（デバイス・カメラ・画面から）・out（Unity へ）・note（接続・切断など）。
// 項目は名前の順に並ぶ（Rust 版と同じ形）。

package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"io"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

type EventLog struct {
	path string
	file *os.File
	w    *bufio.Writer
}

// CreateEventLog は dir の中に「起動した日時.jsonl」を作る
func CreateEventLog(dir string) (*EventLog, error) {
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return nil, err
	}
	path := filepath.Join(dir, time.Now().Format("20060102-150405")+".jsonl")
	file, err := os.Create(path)
	if err != nil {
		return nil, err
	}
	return &EventLog{path: path, file: file, w: bufio.NewWriter(file)}, nil
}

func (l *EventLog) Path() string { return l.path }

func (l *EventLog) Close() error {
	l.w.Flush()
	return l.file.Close()
}

// Incoming は届いた行。JSON として読めなければ文字列のまま残す
func (l *EventLog) Incoming(from, line string) error {
	msg, err := decodeValue(line)
	if err != nil {
		msg = line
	}
	return l.write(map[string]any{"t": nowMs(), "dir": "in", "from": from, "msg": msg})
}

// Outgoing は Stamp で作った、画面に送る JSON
func (l *EventLog) Outgoing(stamped []byte) error {
	msg, err := decodeValue(string(stamped))
	if err != nil {
		return err
	}
	return l.write(map[string]any{"t": nowMs(), "dir": "out", "msg": msg})
}

func (l *EventLog) Note(text string) error {
	return l.write(map[string]any{"t": nowMs(), "dir": "note", "text": text})
}

func (l *EventLog) write(v map[string]any) error {
	enc := json.NewEncoder(l.w)
	// < > & をそのまま書く（Rust 版と同じ）
	enc.SetEscapeHTML(false)
	if err := enc.Encode(v); err != nil {
		return err
	}
	// 落ちたときに直前まで残るよう、1 行ごとに書き出す
	return l.w.Flush()
}

// decodeValue は JSON を 1 つ読む。数は Rust 版と同じ形にそろえる（整数はそのまま、小数は 0.50 → 0.5、5.0 → 5.0）
func decodeValue(text string) (any, error) {
	dec := json.NewDecoder(strings.NewReader(text))
	dec.UseNumber()
	var v any
	if err := dec.Decode(&v); err != nil {
		return nil, err
	}
	if _, err := dec.Token(); err != io.EOF {
		return nil, errors.New("JSON のあとに余計なものがある")
	}
	return normalizeNumbers(v), nil
}

func normalizeNumbers(v any) any {
	switch x := v.(type) {
	case map[string]any:
		for k, e := range x {
			x[k] = normalizeNumbers(e)
		}
	case []any:
		for i, e := range x {
			x[i] = normalizeNumbers(e)
		}
	case json.Number:
		// 整数で書かれていて 64 ビットに入るものは、そのまま（大きな時刻も丸めない）
		if !strings.ContainsAny(string(x), ".eE") {
			if _, err := strconv.ParseInt(string(x), 10, 64); err == nil {
				return x
			}
			if _, err := strconv.ParseUint(string(x), 10, 64); err == nil {
				return x
			}
		}
		if f, err := x.Float64(); err == nil {
			return jFloat(f)
		}
	}
	return v
}

func nowMs() int64 { return time.Now().UnixMilli() }
