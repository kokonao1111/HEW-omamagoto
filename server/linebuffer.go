// BLE の通知を改行までつなげて 1 行にする（要件定義書 6.6）
// デバイスは JSON 1 行を改行付きで送り、1 回の通知に入らなければ分けて送る。

package main

import (
	"bytes"
	"strings"
)

// maxLine は改行が来ないまま溜まり続けたら捨てる長さ（壊れたデータでメモリを使い続けないため）
const maxLine = 4096

type LineBuffer struct {
	buf []byte
}

// Push は届いた通知を足し、改行で終わった行を返す（改行は含まない）
func (b *LineBuffer) Push(data []byte) []string {
	b.buf = append(b.buf, data...)
	var lines []string
	for {
		i := bytes.IndexByte(b.buf, '\n')
		if i < 0 {
			break
		}
		// UTF-8 として壊れた所は「�」にする（Rust 版の from_utf8_lossy と同じ）
		lines = append(lines, strings.ToValidUTF8(string(b.buf[:i]), "�"))
		b.buf = b.buf[i+1:]
	}
	if len(b.buf) > maxLine {
		b.buf = nil
	}
	return lines
}
