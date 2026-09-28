#include "shake.h"

#include <math.h>

static float clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

bool ShakeDetector::update(float ax, float ay, float az, uint32_t now_ms, Shake& out) {
  float mag = sqrtf(ax * ax + ay * ay + az * az);

  // 振っている間も重力の向きが分かるように強めにローパスする（100Hz で約 0.2 秒）
  const float a = 0.05f;
  gx_ += (ax - gx_) * a;
  gy_ += (ay - gy_) * a;
  gz_ += (az - gz_) * a;
  float gmag = sqrtf(gx_ * gx_ + gy_ * gy_ + gz_ * gz_);
  if (gmag > 0.1f) {
    // 加速度計は重力と逆（上向き）を指すので、注ぎ口が下を向くほど内積が負になる
    float dot = (gx_ * sx_ + gy_ * sy_ + gz_ * sz_) / gmag;
    tilt_ = asinf(-dot) * 180.0f / (float)M_PI;
  }

  float spout = ax * sx_ + ay * sy_ + az * sz_;
  if (armed_) {
    if (mag >= cfg_.peak_g) {
      armed_ = false;
      peak_ = mag;
      peak_spout_ = spout;
    }
    return false;
  }

  if (mag > peak_) {
    peak_ = mag;
    peak_spout_ = spout;
  }
  if (mag > cfg_.release_g) return false;

  // ピークが終わった
  armed_ = true;
  if (tilt_ < cfg_.tilt_min_deg) return false;
  if (peak_spout_ >= 0) return false;
  if (counted_once_ && now_ms - last_shake_ms_ < cfg_.min_interval_ms) return false;

  counted_once_ = true;
  last_shake_ms_ = now_ms;
  out = {clamp01((peak_ - cfg_.peak_g) / (cfg_.full_g - cfg_.peak_g)), tilt_};
  return true;
}
