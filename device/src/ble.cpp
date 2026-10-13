#include "ble.h"

#include <NimBLEDevice.h>
#include <string.h>

namespace ble {

// 要件定義書 6.6
static const char* const SERVICE_UUID = "35f64d1a-5af0-4ec7-a321-3b2a485eae3f";
static const char* const EVENT_UUID = "2a28760f-2c0e-4e00-b5cd-4ae7faa984cb";

static char device_name[24];
static NimBLECharacteristic* event_chr = nullptr;
static volatile bool is_connected = false;
static volatile uint16_t mtu = 23;  // 接続直後は BLE の最小値。PC と決め直すと大きくなる

class ServerCallbacks : public NimBLEServerCallbacks {
  void onConnect(NimBLEServer*, NimBLEConnInfo& info) override {
    is_connected = true;
    mtu = info.getMTU();
  }
  void onDisconnect(NimBLEServer*, NimBLEConnInfo&, int) override {
    is_connected = false;
    mtu = 23;
    NimBLEDevice::startAdvertising();  // すぐに知らせ直して、サーバーの再接続を待つ
  }
  void onMTUChange(uint16_t new_mtu, NimBLEConnInfo&) override { mtu = new_mtu; }
};

static ServerCallbacks server_callbacks;

void begin(const char* device_id) {
  size_t n = strlen(device_id);
  // "AA:BB:CC:DD:EE:FF" の "EE" と "FF"
  snprintf(device_name, sizeof(device_name), "omamagoto-%.2s%.2s", device_id + n - 5, device_id + n - 2);

  NimBLEDevice::init(device_name);
  NimBLEDevice::setMTU(255);

  NimBLEServer* server = NimBLEDevice::createServer();
  server->setCallbacks(&server_callbacks);
  NimBLEService* service = server->createService(SERVICE_UUID);
  event_chr = service->createCharacteristic(EVENT_UUID, NIMBLE_PROPERTY::NOTIFY);
  service->start();

  // 128 ビットの UUID と名前は 1 つの広告（31 バイト）に入らないので、名前はスキャン応答に入れる。
  // enableScanResponse を setName より先に呼ぶと、名前がスキャン応答側に入る
  NimBLEAdvertising* adv = NimBLEDevice::getAdvertising();
  adv->enableScanResponse(true);
  adv->setName(device_name);
  adv->addServiceUUID(SERVICE_UUID);
  adv->start();
}

void send(const char* json, size_t len) {
  if (!is_connected || event_chr == nullptr) return;
  // 末尾に改行を付けて、MTU−3 バイトずつ通知する
  uint8_t buf[512];
  if (len + 1 > sizeof(buf)) return;
  memcpy(buf, json, len);
  buf[len++] = '\n';
  size_t chunk = mtu > 3 ? mtu - 3 : 20;
  for (size_t off = 0; off < len; off += chunk) {
    size_t n = len - off < chunk ? len - off : chunk;
    event_chr->notify(buf + off, n);
  }
}

bool connected() { return is_connected; }

const char* name() { return device_name; }

}  // namespace ble
