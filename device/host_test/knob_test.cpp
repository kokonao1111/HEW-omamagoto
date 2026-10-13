// つまみ判定（src/knob.cpp）を Mac 上で合成データで確かめる
//   画面を正面に向けて deg 回したときの加速度 = 画面の中で重力の向きが回る
#include <math.h>
#include <stdio.h>

#include "knob.h"

static int failed = 0;

static void feed(KnobDetector& k, float deg, int n = 100, float noise = 0) {
  float r = deg * (float)M_PI / 180;
  for (int i = 0; i < n; i++) {
    float e = noise * sinf(i * 1.7f);
    k.update(sinf(r) + e, cosf(r) - e, 0.02f);
  }
}

static void expect(const char* name, bool ok) {
  if (!ok) failed++;
  printf("%s  %s\n", ok ? "OK  " : "FAIL", name);
}

int main() {
  // 貼り方がずれていて「切」が 200 度の位置でも、長押しで合わせた位置から角度が出るか
  {
    KnobDetector k;
    feed(k, 200);
    k.setZero();
    struct { float turn; int level; } steps[] = {
        {0, 0}, {20, 0}, {35, 1}, {60, 1}, {80, 2}, {110, 2}, {125, 3}, {170, 3},
        {115, 3},  // 戻すときは 8 度余計に戻すまで下げない
        {70, 2}, {25, 1}, {0, 0},
        {-40, 1}, {-130, 3},  // 逆回しも同じに扱う
    };
    bool ok = true;
    for (auto& s : steps) {
      feed(k, 200 + s.turn);
      if (k.level() != s.level) {
        ok = false;
        printf("      %+.0f 度で段階 %d（期待 %d）\n", s.turn, k.level(), s.level);
      }
    }
    expect("回した角度から切・弱・中・強が決まる（戻すときは 8 度の余裕）", ok);
  }
  // 境目（30 度）付近で揺らしても段階がばたつかないか
  {
    KnobDetector k;
    feed(k, 0);
    k.setZero();
    feed(k, 31);
    int flips = 0, last = k.level();
    for (int i = 0; i < 50; i++) {
      feed(k, 31 + ((i % 2) ? -4.0f : 4.0f), 20, 0.02f);
      if (k.level() != last) {
        flips++;
        last = k.level();
      }
    }
    expect("境目 31±4 度で 50 回揺らしても段階が変わらない", flips == 0);
  }
  // 平置き（画面が上向き）では角度を出さない
  {
    KnobDetector k;
    for (int i = 0; i < 100; i++) k.update(0.05f, 0.05f, 1.0f);
    expect("平置きでは角度を出さない", !k.valid());
  }
  // 起動直後に違う段階を出さない（2026-09-30 の実機で起動直後に一瞬 HIGH が出た不具合）
  {
    KnobDetector k;
    int changes = 0;
    for (int i = 0; i < 200; i++) changes += k.update(0.0f, 1.0f, 0.02f);
    expect("起動直後に段階が変わらない", changes == 0 && k.level() == 0);
  }
  return failed ? 1 : 0;
}
