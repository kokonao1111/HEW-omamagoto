// 実機で記録した生データ（シリアルに r を送って出した raw 行）を、振り判定とつまみ判定にもう一度通す
//   しきい値を変えたときに、同じ動きで何回数えるかを比べるのに使う
//   使い方: ./replay 記録.txt
#include <stdio.h>

#include "knob.h"
#include "shake.h"

int main(int argc, char** argv) {
  if (argc < 2) {
    fprintf(stderr, "使い方: %s 記録.txt\n", argv[0]);
    return 2;
  }
  FILE* f = fopen(argv[1], "r");
  if (!f) {
    perror(argv[1]);
    return 2;
  }
  ShakeDetector shake;
  KnobDetector knob;
  Shake s;
  int shakes = 0, knob_changes = 0;
  char line[256];
  while (fgets(line, sizeof line, f)) {
    unsigned long t;
    float x, y, z, tilt;
    if (sscanf(line, "raw,%lu,%f,%f,%f,%f", &t, &x, &y, &z, &tilt) != 5) continue;
    if (shake.update(x, y, z, t, s)) {
      shakes++;
      printf("shake %lu intensity=%.2f tilt=%.0f\n", t, s.intensity, s.tilt);
    }
    if (knob.update(x, y, z)) {
      knob_changes++;
      printf("knob  %lu level=%d angle=%.0f\n", t, knob.level(), knob.angle());
    }
  }
  fclose(f);
  fprintf(stderr, "振り %d 回、つまみの段階の変化 %d 回\n", shakes, knob_changes);
  return 0;
}
