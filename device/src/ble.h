// BLE でイベントの JSON を送る（要件定義書 5.1、6.6）
// デバイスは周辺機器になり、展示用 PC（サーバー）が接続しに来る。
// JSON 1 行を改行付きで通知し、1 回の通知に入らなければ分けて送る。サーバーは改行までつなげる。
#pragma once
#include <stddef.h>
#include <stdint.h>

namespace ble {

// device_id は "AA:BB:CC:DD:EE:FF"。名前は "omamagoto-" + 下 4 桁（例：omamagoto-EEFF）
void begin(const char* device_id);

// JSON 1 行（改行なし）を送る。つながっていなければ捨てる
void send(const char* json, size_t len);

bool connected();
const char* name();

}  // namespace ble
