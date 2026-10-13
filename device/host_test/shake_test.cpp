// 振り判定（src/shake.cpp）を Mac 上で合成データで確かめる
//   1振り = 振り出し(+) と止め(-) の 2 つのピークを持つ波形。止め（注ぎ口と逆向き）だけが数えられる
#include <math.h>
#include <stdio.h>

#include "shake.h"

static int failed = 0;

static int run(float tilt_deg, int shakes, float stroke_ms, float gap_ms, float amp_g) {
  ShakeDetector d;
  Shake out;
  int count = 0;
  float r = tilt_deg * (float)M_PI / 180, gy = -sinf(r), gz = cosf(r);
  uint32_t t = 0;
  for (; t < 1000; t += 10) count += d.update(0, gy, gz, t, out);
  for (int k = 0; k < shakes; k++) {
    for (float p = 0; p < stroke_ms; p += 10, t += 10)
      count += d.update(0, gy + amp_g * sinf(2 * (float)M_PI * p / stroke_ms), gz, t, out);
    for (float p = 0; p < gap_ms; p += 10, t += 10) count += d.update(0, gy, gz, t, out);
  }
  for (int i = 0; i < 200; i++, t += 10) count += d.update(0, gy, gz, t, out);
  return count;
}

static void expect(const char* name, int got, int want) {
  bool ok = got == want;
  if (!ok) failed++;
  printf("%s  %-40s %d 回（期待 %d）\n", ok ? "OK  " : "FAIL", name, got, want);
}

int main() {
  expect("1 回振る（70 度）", run(70, 1, 200, 0, 3.0f), 1);
  expect("ゆっくり 10 回（1 秒に 1 回）", run(70, 10, 200, 800, 3.0f), 10);
  expect("速く 10 回（1 秒に 4 回）", run(70, 10, 200, 50, 3.0f), 10);
  expect("強く 10 回（7g、60 度）", run(60, 10, 200, 300, 7.0f), 10);
  expect("立てたまま 10 回（-90 度）", run(-90, 10, 200, 300, 3.0f), 0);
  expect("水平で 10 回（0 度）", run(0, 10, 200, 300, 3.0f), 0);
  return failed ? 1 : 0;
}
