// コンロつまみ（要件定義書 5.4）
// 正面に付いたつまみに、画面を正面に向けて貼る。回すと画面の中で重力の向きが変わるので、
// その角度から「切」の位置から何度回したかを出し、切・弱・中・強の 4 段階にする。
// 左右どちらに回しても同じに扱う。
#pragma once
#include <stdint.h>

struct KnobConfig {
  float low_deg = 30.0f;    // これ以上回したら弱
  float mid_deg = 75.0f;    // 中
  float high_deg = 120.0f;  // 強
  float hysteresis_deg = 8.0f;  // 境目で段階が行ったり来たりしないよう、戻すときはこれだけ余計に戻す
};

class KnobDetector {
 public:
  explicit KnobDetector(const KnobConfig& cfg = KnobConfig()) : cfg_(cfg) {}

  // 加速度（g、デバイス座標）を 1 サンプル入れる。段階が変わったら true を返す
  bool update(float ax, float ay, float az);

  // 今の位置を「切」にする
  void setZero() { zero_deg_ = raw_deg_; }
  void setZero(float deg) { zero_deg_ = deg; }
  float zero() const { return zero_deg_; }

  float angle() const { return angle_; }  // 切の位置から何度回したか（0〜180）
  int level() const { return level_; }    // 0=切・1=弱・2=中・3=強
  bool valid() const { return valid_; }   // 画面が上か下を向いていて角度が出せないときは false

 private:
  KnobConfig cfg_;
  float gx_ = 0, gy_ = 0, gz_ = 0;   // ローパスした重力方向
  bool started_ = false;             // 最初のサンプルでローパスを初期化する（起動直後に違う段階を出さない）
  float raw_deg_ = 0;                // 画面の中での重力の向き
  float zero_deg_ = 0;
  float angle_ = 0;
  int level_ = 0;
  bool valid_ = false;
};
