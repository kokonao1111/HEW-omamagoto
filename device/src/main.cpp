// センサーデバイス共通ファームウェア（要件定義書 5.1〜5.2、6.2）
// 今はサーバーが無いので、イベントは USB シリアルに 1 行 1 JSON で出す。
//   pio device monitor で見られる。Wi-Fi・WebSocket での送信は次の段階で足す。
//
// 画面：MAC アドレス（サーバーの設定で役割を決めるときに使う）、バッテリー、傾き、振った回数
// ボタン A：振った回数を 0 に戻す
// シリアルに r を送る：センサーの生データ（raw,ts,ax,ay,az,tilt）の出力を切り替える。しきい値の調整用
#include <ArduinoJson.h>
#include <M5Unified.h>
#include <esp_mac.h>

#include "shake.h"

static constexpr uint32_t BATTERY_INTERVAL_MS = 30000;
static constexpr uint32_t SCREEN_INTERVAL_MS = 100;

static char device_id[18];  // "AA:BB:CC:DD:EE:FF"
static uint32_t seq = 0;
static ShakeDetector shake;
static uint32_t shake_count = 0;
static float last_intensity = 0;
static bool raw_mode = false;

static void send(JsonDocument& doc) {
  doc["device"] = device_id;
  doc["ts"] = millis();
  doc["seq"] = ++seq;
  serializeJson(doc, Serial);
  Serial.println();
}

static void sendBattery() {
  JsonDocument doc;
  doc["event"] = "battery";
  doc["percent"] = M5.Power.getBatteryLevel();
  send(doc);
}

static void drawScreen() {
  auto& d = M5.Display;
  d.startWrite();
  d.setCursor(0, 0);
  d.setTextColor(TFT_WHITE, TFT_BLACK);
  d.setTextSize(1);
  d.printf("%s\n", device_id);
  d.printf("bat %3d%%  %s\n\n", M5.Power.getBatteryLevel(), M5.Power.isCharging() ? "chg" : "   ");

  d.setTextSize(2);
  float tilt = shake.tilt();
  d.setTextColor(tilt >= 45 ? TFT_GREEN : TFT_DARKGREY, TFT_BLACK);
  d.printf("tilt %4d\n", (int)lroundf(tilt));
  d.setTextColor(TFT_WHITE, TFT_BLACK);
  d.printf("\n");
  d.setTextSize(4);
  d.printf("%4lu\n", (unsigned long)shake_count);
  d.setTextSize(1);
  d.printf("last %.2f\n\n", last_intensity);
  d.setTextColor(TFT_YELLOW, TFT_BLACK);
  d.printf("USB serial (no Wi-Fi)\n");
  d.endWrite();
}

void setup() {
  auto cfg = M5.config();
  M5.begin(cfg);
  Serial.begin(115200);

  uint8_t mac[6];
  esp_read_mac(mac, ESP_MAC_WIFI_STA);
  snprintf(device_id, sizeof(device_id), "%02X:%02X:%02X:%02X:%02X:%02X",
           mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);

  M5.Display.setRotation(0);  // 縦向き。USB が下
  M5.Display.fillScreen(TFT_BLACK);

  JsonDocument doc;
  doc["event"] = "hello";
  doc["board"] = M5.getBoard();
  doc["imu"] = M5.Imu.isEnabled();
  send(doc);
  sendBattery();
}

void loop() {
  M5.update();
  uint32_t now = millis();

  if (M5.BtnA.wasPressed()) {
    shake_count = 0;
    last_intensity = 0;
  }

  while (Serial.available()) {
    if (Serial.read() == 'r') raw_mode = !raw_mode;
  }

  if (M5.Imu.update()) {
    auto data = M5.Imu.getImuData();
    auto& a = data.accel;
    Shake found;
    bool hit = shake.update(a.x, a.y, a.z, now, found);
    if (raw_mode) {
      Serial.printf("raw,%lu,%.3f,%.3f,%.3f,%.1f\n", (unsigned long)now, a.x, a.y, a.z, shake.tilt());
    }
    if (hit) {
      shake_count++;
      last_intensity = found.intensity;
      JsonDocument doc;
      doc["event"] = "shake";
      doc["intensity"] = roundf(found.intensity * 100) / 100;
      doc["tilt"] = (int)lroundf(found.tilt);
      send(doc);
    }
  }

  static uint32_t last_battery = 0;
  if (now - last_battery >= BATTERY_INTERVAL_MS) {
    last_battery = now;
    sendBattery();
  }

  static uint32_t last_screen = 0;
  if (now - last_screen >= SCREEN_INTERVAL_MS) {
    last_screen = now;
    drawScreen();
  }
}
