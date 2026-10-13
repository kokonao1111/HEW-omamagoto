#include "knob.h"

#include <math.h>

bool KnobDetector::update(float ax, float ay, float az) {
  // 回している途中の揺れを抑える（200Hz で約 0.1 秒）
  const float a = 0.05f;
  if (!started_) {
    gx_ = ax;
    gy_ = ay;
    gz_ = az;
    started_ = true;
  }
  gx_ += (ax - gx_) * a;
  gy_ += (ay - gy_) * a;
  gz_ += (az - gz_) * a;

  // 画面が上か下を向いていると、画面の中の重力が小さくなり角度が決まらない
  valid_ = sqrtf(gx_ * gx_ + gy_ * gy_) >= 0.5f;
  if (!valid_) return false;

  raw_deg_ = atan2f(gx_, gy_) * 180.0f / (float)M_PI;
  float d = raw_deg_ - zero_deg_;
  while (d > 180) d -= 360;
  while (d < -180) d += 360;
  angle_ = fabsf(d);

  const float th[] = {cfg_.low_deg, cfg_.mid_deg, cfg_.high_deg};
  int next = level_;
  while (next < 3 && angle_ >= th[next]) next++;
  while (next > 0 && angle_ < th[next - 1] - cfg_.hysteresis_deg) next--;
  if (next == level_) return false;
  level_ = next;
  return true;
}
