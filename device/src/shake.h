// 振りの検知（要件定義書 5.2）
// 注ぎ口が水平より 45 度以上下を向いているときに、加速度のピークが来たら「1振り」としてすぐ数える。
// 持ち上げただけの揺れは、注ぎ口が上を向いているので数えない。
// 実測では 1 振りで強いピークが 1 つ、注ぎ口と逆向き（注ぎ口方向の成分がマイナス）に出る。
// 逆向きのピークは数えないので、振り出しと止めを 2 回と数えることはない。
#pragma once
#include <stdint.h>

struct ShakeConfig {
  float tilt_min_deg = 45.0f;     // これより注ぎ口が上を向いていたら数えない
  float peak_g = 1.8f;            // 加速度の大きさがこれを超えたらピーク
  float release_g = 1.3f;         // ピークの後、これを下回ったら次のピークを受け付ける
  float full_g = 8.0f;            // intensity = 1 になる加速度（センサーの上限は ±8g）
  uint32_t min_interval_ms = 120; // 数えてからこの時間は次を数えない（速く振っても 1 振りは 200ms 以上ある）
};

struct Shake {
  float intensity; // 0〜1
  float tilt;      // 度（水平が 0、注ぎ口が真下で 90）
};

class ShakeDetector {
 public:
  explicit ShakeDetector(const ShakeConfig& cfg = ShakeConfig()) : cfg_(cfg) {}

  // 加速度（g、デバイス座標）を 1 サンプル入れる。振りが確定したら out に書いて true を返す
  bool update(float ax, float ay, float az, uint32_t now_ms, Shake& out);

  float tilt() const { return tilt_; }

  // 注ぎ口の向き（デバイス座標の単位ベクトル）。容器への取り付け方に合わせて変える
  void setSpoutAxis(float x, float y, float z) { sx_ = x; sy_ = y; sz_ = z; }

 private:
  ShakeConfig cfg_;
  float sx_ = 0, sy_ = 1, sz_ = 0;  // 既定は +Y（画面の上方向）が注ぎ口
  float gx_ = 0, gy_ = 0, gz_ = 1;  // ローパスした重力方向
  float tilt_ = -90;
  bool armed_ = true;               // 次のピークを受け付けるか
  float peak_ = 0;                  // 今のピークの最大値
  float peak_spout_ = 0;            // 最大のときの注ぎ口方向の成分
  uint32_t last_shake_ms_ = 0;
  bool counted_once_ = false;       // 起動直後に min_interval で弾かないため
};
