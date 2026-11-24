#!/usr/bin/env python3
"""おもちゃの電子レンジの扉が「どれだけ開いているか（角度）」をカメラで見て、Unity の電子レンジの扉も同じだけ開く（試し用）。

しくみ（カメラは電子レンジを上から見る）:
  扉を閉めたときの映像を 1 枚覚えておき、今の映像との「ちがい」を見る。
  扉が開くと、扉のある所が閉めたときとちがって見える（台の色が何色でも使える。キッチン台も扉も木でもよい）。
  蝶番から角度を変えながら帯を引き、ちがいがいちばん多く乗る向きを「今の扉の向き」とする。
  蝶番と閉めた扉の先は、映像をクリックして決める（h キー）。いっぱいに開けた向きは、開けて o キーで自動で決まる。
  閉めたときを 0、開けたときを 1 として、その割合を本物の角度（--open-degrees、ふつう 90 度）に直して Unity に送る:
    {"event": "door", "open": true, "angle": 45.0}

  にんじん（学習モデルの「野菜」）も見て、扉が開いている間に扉の入り口で見えなくなり、
  そのまま扉が閉まったら「電子レンジに入れた」とする（今は画面とコンソールに出すだけ。Unity にはまだ送らない）。

  本番では、サーバー（server/）が画面にイベントを配る。この道具は試し用に、
  サーバーの代わりに ws://localhost:9000/screen を開いて、Unity に直接送る。
  なので、Rust のサーバーを動かしているときは使えない（同じ 9000 番を使うため）。

使い方（Windows）:
  .venv\\Scripts\\python camera\\door_watch.py --camera 2
  そのあと Unity でキッチンのシーンを再生すると、自動でつながる。

キー（カメラの窓で押す）:
  c : 扉を閉めて、手を外に出してから押す（「閉めた」の映像を覚える）
  h : 2 点をクリックする（蝶番 → 閉めた扉の先）
  o : 扉をいっぱいに開けて、手を外に出してから押す（開けた扉の向きを自動で決める）
  t : 判定とは関係なく、開け閉めを Unity に送る（つながりの確認用）
  q : 終わる
カメラが少し動いても（アームで付け外ししても）、1 秒ごとに映像の目印（木目・角など）を見比べて、自動で合わせ直す。
大きく向きを変えて、目印が合わなくなったときだけ、c と h をやり直す。
"""
import argparse
import asyncio
import json
import math
import platform
import sys
import threading
import time
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from util import draw_lines  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
CONFIG_DIR = ROOT / "camera" / "config"
SCALE = 0.5  # 計算は半分の大きさの画像でする（速くするため）
MAX_FRACTION = 1.2  # 「いっぱいに開けた」として決めた所の 120% まで見る（もっと大きく開ける人もいるため）
DIFF_THRESHOLD = 28  # 閉めたときと、これだけ明るさがちがえば「変わった」とする（0〜255）
MIN_TURN = 8  # これより小さい角度は探さない（もともと扉があった所と区別できないため）。見つからなければ閉まっている
FOUND_SCORE = 0.35  # 帯の上の、これだけの割合が「変わった」なら、そこに扉がある
LEFT_SCORE = 0.3  # 閉めた扉の所が、これだけ変わっていたら「扉が閉めた所からどいた」
BAND_OFFSETS = (-4, 0, 4)  # 線を 1 本でなく、横に少しずらした 3 本の帯で見る（少しずれていても拾えるように）
MISS_DIR = ROOT / "camera" / "captures" / "door_miss"  # 扉が見つからなかったときの映像（原因を調べる用）
MAX_MISS_IMAGES = 10
CLICK_NAMES = ["蝶番（扉の回る軸）", "閉めた扉の先"]
FOLLOW_SECONDS = 1.0  # これだけおきに、カメラのずれを計算し直す


# ======================== 閉めたときとのちがい ========================
def prepare(frame):
    """見比べやすい形にする：半分の大きさ・ぼかし・全体の明るさをそろえる（カメラが明るさを変えても同じに見えるように）"""
    small = cv2.resize(frame, None, fx=SCALE, fy=SCALE, interpolation=cv2.INTER_AREA)
    blurred = cv2.GaussianBlur(small, (5, 5), 0).astype(np.int16)
    return blurred - blurred.reshape(-1, 3).mean(axis=0).astype(np.int16)


def changed_mask(frame, closed):
    """閉めたときから変わった所を True にした画像（半分の大きさ）"""
    difference = np.abs(prepare(frame) - closed).max(axis=2)
    mask = (difference > DIFF_THRESHOLD).astype(np.uint8)
    return cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8)) > 0  # 小さなごみを消す


def rotate(direction, degrees):
    """向きを、画像の上で degrees 度回す（時計回りが +）"""
    angle = math.radians(degrees)
    cos, sin = math.cos(angle), math.sin(angle)
    return np.array([direction[0] * cos - direction[1] * sin, direction[0] * sin + direction[1] * cos])


def signed_angle(from_dir, to_dir):
    """from_dir から to_dir まで、何度回ったか（画像の上で。時計回りが +）"""
    return math.degrees(math.atan2(from_dir[0] * to_dir[1] - from_dir[1] * to_dir[0], from_dir @ to_dir))


# ======================== カメラのずれを合わせる ========================
class CameraAlignment:
    """覚えた映像（閉めたとき）と今の映像の、目印になる点（台の木目・角など）を見比べて、カメラのずれを計算する。
    アームでカメラを付け外ししても、だいたい同じ所を写していれば、蝶番の位置などを自動でずらして使える。
    電子レンジは台に作り付け（台の上で動かない）とする。電子レンジを動かしたときは、c・h・o をやり直す"""

    MIN_MATCHES = 40  # これだけの点が合えば、ずれを信じる（扉が閉まっていれば、ふつう 100 点以上合う）

    def __init__(self):
        self.orb = cv2.ORB_create(nfeatures=3000)
        self.matcher = cv2.BFMatcher(cv2.NORM_HAMMING)
        self.reference = None  # (点, 特徴)
        self.matches = 0  # 最後に合った点の数（画面に出す）

    def set_reference(self, frame):
        self.reference = self.orb.detectAndCompute(cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY), None)

    def find(self, frame):
        """覚えた映像 → 今の映像 への変換（3x3）を返す。見つからなければ None"""
        if self.reference is None or self.reference[1] is None:
            return None
        keypoints, descriptors = self.orb.detectAndCompute(cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY), None)
        if descriptors is None:
            return None
        pairs = self.matcher.knnMatch(self.reference[1], descriptors, k=2)
        good = [p[0] for p in pairs if len(p) == 2 and p[0].distance < 0.75 * p[1].distance]  # はっきり似ているものだけ
        if len(good) < self.MIN_MATCHES:
            self.matches = len(good)
            return None
        source = np.float32([self.reference[0][m.queryIdx].pt for m in good])
        target = np.float32([keypoints[m.trainIdx].pt for m in good])
        # ずれは「回転・移動・大きさ」だけとして計算する（ゆがみまで入れると、点が少ないときに答えが不安定になる）。
        # 扉や手など動いた所の点は外れ値として捨てる（RANSAC）
        similarity, inliers = cv2.estimateAffinePartial2D(source, target, method=cv2.RANSAC, ransacReprojThreshold=4.0)
        self.matches = int(inliers.sum()) if inliers is not None else 0
        if similarity is None or self.matches < self.MIN_MATCHES:
            return None
        transform = np.vstack([similarity, [0, 0, 1]])
        return transform if looks_reasonable(transform) else None


def looks_reasonable(transform):
    """ずれの計算がおかしくないか（大きさが 0.8〜1.25 倍、ゆがみが小さい）。まちがった合わせ方を捨てるため"""
    scale = math.sqrt(abs(np.linalg.det(transform[:2, :2])))
    skew = abs(transform[2, 0]) + abs(transform[2, 1])
    return 0.8 < scale < 1.25 and skew < 0.001


def transform_points(points, transform):
    """点の並びを、3x3 の変換で動かす"""
    moved = cv2.perspectiveTransform(np.float32(points).reshape(-1, 1, 2), transform).reshape(-1, 2)
    return [[float(x), float(y)] for x, y in moved]


# ======================== 扉の角度を測る ========================
class DoorAngle:
    """閉めたときの映像と 3 点（蝶番・閉めた扉の先・開けた扉の先）から、1 枚ごとに扉の角度を測る。

    覚えるもの（camera/config/ に保存）：閉めたときの映像（元の大きさ）と、その映像の上での 3 点。
    カメラが動いたら、CameraAlignment でずれを計算して、3 点と閉めたときの映像を今の映像に合わせてずらす。
    """

    def __init__(self, camera_number, open_degrees):
        self.path = CONFIG_DIR / f"door_cam{camera_number}.json"
        self.image_path = CONFIG_DIR / f"door_cam{camera_number}_closed.png"
        self.open_degrees = open_degrees  # いっぱいに開けたときの、本物の角度
        self.alignment = CameraAlignment()
        self.reference_frame = None  # 閉めたときの映像（覚えたときのカメラの向き）
        self.reference_points = None  # 覚えた映像の上での [蝶番, 閉めた扉の先, 開けた扉の先]
        self.transform = np.eye(3)  # 覚えた映像 → 今の映像 のずれ
        self.aligned_closed = None  # 閉めたときの映像を、今のカメラの向きにずらしたもの
        self.aligned_valid = None  # ずらした映像が届いている所（はみ出した所は比べない）
        self.fraction = None  # 今どれだけ開いているか（0 = 閉めた、1 = いっぱいに開けた）。わからなければ None
        self.score = 0.0
        self.aligned_ok = False
        self.load()

    # ---- 形（今のカメラの向きでの 3 点から決まるもの） ----
    @property
    def ready(self):
        return self.has_open and self.reference_frame is not None and self.aligned_closed is not None

    @property
    def has_open(self):
        """開けた扉の向きが決まっているか（o を押したか）"""
        return self.reference_points is not None and abs(self.full_turn) > 5

    @property
    def points(self):
        """今のカメラの向きでの 3 点（元の大きさの画像の画素）"""
        if self.reference_points is None:
            return None
        return transform_points(self.reference_points, self.transform)

    @property
    def hinge(self):
        return np.array(self.points[0]) * SCALE

    @property
    def closed_dir(self):
        offset = np.array(self.points[1]) * SCALE - self.hinge
        return offset / np.linalg.norm(offset)

    @property
    def full_turn(self):
        """いっぱいに開けるまでに、画像の上で何度回るか（向きつき）"""
        offset = np.array(self.points[2]) * SCALE - self.hinge
        return signed_angle(self.closed_dir, offset / np.linalg.norm(offset))

    @property
    def length(self):
        """扉の長さ（半分の大きさの画像の画素）"""
        return float(np.linalg.norm(np.array(self.points[1]) * SCALE - self.hinge))

    @property
    def degrees(self):
        """今の扉の角度（本物の角度に直したもの）"""
        return None if self.fraction is None else self.fraction * self.open_degrees

    # ---- カメラのずれに合わせる ----
    def follow_camera(self, frame):
        """今の映像と覚えた映像を見比べて、ずれを計算し直す。合わなければ前のずれのまま。
        扉が開いていると、扉がふさいだ所の目印が合わず、ずれの計算が少し狂うので、閉まっているとき（またはまだわからないとき）だけ計算する"""
        if self.reference_frame is None:
            return
        if self.fraction is not None and self.fraction > 0:
            return
        transform = self.alignment.find(frame)
        self.aligned_ok = transform is not None
        if transform is not None:
            self.transform = transform
            self._warp_reference(frame.shape)

    def _warp_reference(self, shape):
        height, width = shape[:2]
        warped = cv2.warpPerspective(self.reference_frame, self.transform, (width, height))
        valid = cv2.warpPerspective(np.full(self.reference_frame.shape[:2], 255, np.uint8), self.transform, (width, height))
        valid = cv2.erode(valid, np.ones((9, 9), np.uint8))  # ふちは少し内側まで比べない
        self.aligned_closed = warped
        self.aligned_valid = cv2.resize(valid, None, fx=SCALE, fy=SCALE, interpolation=cv2.INTER_NEAREST) > 0

    def _refresh_reference(self):
        """覚えた映像が変わったら、目印を取り直す"""
        if self.reference_frame is not None:
            self.alignment.set_reference(self.reference_frame)

    # ---- 覚える ----
    def remember_closed(self, frame):
        """閉めたときの映像を覚え直す。3 点はもう決めてあれば、カメラのずれの分だけずらして引きつぐ"""
        if self.reference_points is not None:
            transform = self.alignment.find(frame)  # 扉は閉まっているので、開き具合に関係なくずれを計算する
            if transform is not None:
                self.transform = transform
            self.reference_points = self.points  # 今のカメラの向きでの 3 点を、新しい基準にする
        self.reference_frame = frame.copy()
        self.transform = np.eye(3)
        self._refresh_reference()
        self._warp_reference(frame.shape)
        cv2.imwrite(str(self.image_path), frame)
        self.save()

    def set_points(self, hinge, closed_tip):
        """今の映像の上でクリックした 2 点（蝶番・閉めた扉の先）を覚える。開けた扉の先は、まだ仮（o で決める）"""
        points = [hinge, closed_tip, closed_tip]
        self.reference_points = transform_points(points, np.linalg.inv(self.transform))
        self._refresh_reference()
        self.save()

    def remember_open(self, frame):
        """扉をいっぱいに開けた映像から、開けた扉の向きを決める（蝶番から、どちら回りにも探す）"""
        if self.reference_frame is None or self.reference_points is None:
            return False
        mask = self.changed(frame)
        turns = np.concatenate([np.arange(-150, -MIN_TURN + 1, 1.0), np.arange(MIN_TURN, 151, 1.0)])
        turn, score = self.door_direction(mask, turns)
        if turn is None or score < FOUND_SCORE:
            print("開けた扉が見つかりません。扉をいっぱいに開けて、手を外に出してから o をやり直してください")
            return False
        hinge = np.array(self.points[0])
        tip = hinge + rotate(self.closed_dir, turn) * self.length / SCALE
        points = [self.points[0], self.points[1], tip.tolist()]
        self.reference_points = transform_points(points, np.linalg.inv(self.transform))
        self._refresh_reference()
        print(f"蝶番 {np.round(hinge)}、いっぱいに開けると画像の上で {self.full_turn:.0f} 度、扉の長さ {self.length / SCALE:.0f} 画素")
        self.save()
        return True

    # ---- 測る ----
    def measure(self, frame):
        """今の扉の開き具合（fraction）を測る。わかったら True を返す"""
        if not self.ready:
            return False
        # ずらした「閉めた」がはみ出した所は、今の映像で埋める（そこは「変わっていない」ことにする）
        closed = np.where(self.aligned_valid[..., None], prepare(self.aligned_closed), prepare(frame))
        mask = self.changed(frame)
        left = self.changed_along(mask, 0)  # 閉めた扉の所が変わったか
        sign = 1 if self.full_turn >= 0 else -1
        turns = np.arange(MIN_TURN, abs(self.full_turn) * MAX_FRACTION + 1, 1.0) * sign
        best_turn, best_score = self.door_direction(mask, turns)
        self.score = best_score

        if best_turn is not None and best_score >= FOUND_SCORE and left >= LEFT_SCORE:
            self.fraction = min(MAX_FRACTION, abs(best_turn) / abs(self.full_turn))
            return True
        if left < LEFT_SCORE and best_score < FOUND_SCORE:
            self.fraction = 0.0  # 閉めた所に扉があって、ほかの所も変わっていない → 閉まっている
            return True
        return False  # はっきりしない（手でかくれている など）。前の値のままにする

    def changed(self, frame):
        """閉めたときから変わった所。ずらした「閉めた」がはみ出した所は、今の映像で埋める（変わっていないことにする）"""
        closed = np.where(self.aligned_valid[..., None], prepare(self.aligned_closed), prepare(frame))
        return changed_mask(frame, closed)

    def door_direction(self, mask, turns):
        """角度の候補 turns（度）の中で、扉のある向きを探す。(向き, 点数) を返す。
        斜めから見ると扉は太い帯に見え、いくつもの向きが同じくらい高い点数になるので、
        いちばん高い点数のまわりで、高い点数が続いている範囲の「真ん中」を扉の向きにする"""
        scores = np.array([self.changed_along(mask, turn) for turn in turns])
        if len(scores) == 0 or scores.max() <= 0:
            return None, 0.0
        best = int(scores.argmax())
        high = scores >= scores[best] * 0.85
        start = best
        while start > 0 and high[start - 1]:
            start -= 1
        end = best
        while end < len(scores) - 1 and high[end + 1]:
            end += 1
        return float((turns[start] + turns[end]) / 2), float(scores[best])

    def changed_along(self, mask, turn_degrees):
        """閉めた向きから turn_degrees 回した向きに、蝶番から帯を引き、その上の「変わった」の割合を返す"""
        height, width = mask.shape
        direction = rotate(self.closed_dir, turn_degrees)
        side = np.array([-direction[1], direction[0]])  # 帯の横の向き
        distances = np.linspace(0.25, 0.9, 25) * self.length  # 蝶番の近くは、ほかの物とまざりやすいので使わない
        hits = np.zeros(len(distances), bool)
        seen = np.zeros(len(distances), bool)
        for offset in BAND_OFFSETS:
            points = (self.hinge + side * offset + np.outer(distances, direction)).astype(int)
            inside = (points[:, 0] >= 0) & (points[:, 0] < width) & (points[:, 1] >= 0) & (points[:, 1] < height)
            # 閉めたときの映像が届いていない所（カメラがずれて、はみ出した所）は、数えない
            inside[inside] = self.aligned_valid[points[inside, 1], points[inside, 0]]
            hit = np.zeros(len(distances), bool)
            hit[inside] = mask[points[inside, 1], points[inside, 0]]
            hits |= hit  # 3 本のどれかが変わっていれば、その距離は変わったとみなす
            seen |= inside
        if seen.sum() < 5:
            return 0.0  # ほとんど映像の外
        return float(hits[seen].mean())

    def in_door_area(self, point):
        """点（元の大きさの画像の画素）が、扉の入り口のあたり（扉が回る扇の形の中）にあるか"""
        if self.points is None:
            return False
        offset = np.array(point) * SCALE - self.hinge
        distance = np.linalg.norm(offset)
        if distance < 1 or distance > self.length * 1.15:
            return False
        turn = signed_angle(self.closed_dir, offset / distance)
        low, high = sorted((-10.0 * np.sign(self.full_turn), self.full_turn * MAX_FRACTION))
        return low <= turn <= high

    def draw(self, view):
        """蝶番と、今の扉の向きを映像に重ねる"""
        if self.points is None:
            return
        hinge = tuple(int(v) for v in self.points[0])
        cv2.circle(view, hinge, 10, (0, 0, 255), 3)
        for tip in self.points[1:]:
            cv2.line(view, hinge, tuple(int(v) for v in tip), (160, 160, 160), 1)  # 閉めた・開けたの向き（細い灰色）
        if self.fraction is not None:
            direction = rotate(self.closed_dir, self.fraction * self.full_turn)
            tip = self.hinge / SCALE + direction * self.length / SCALE
            cv2.line(view, hinge, (int(tip[0]), int(tip[1])), (60, 200, 255), 4)

    # ---- 保存・読み込み ----
    def save(self):
        CONFIG_DIR.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps({"points": self.reference_points}, indent=2), encoding="utf-8")

    def load(self):
        if self.image_path.exists():
            self.reference_frame = cv2.imread(str(self.image_path))
            self._warp_reference(self.reference_frame.shape)
        if self.path.exists():
            self.reference_points = json.loads(self.path.read_text(encoding="utf-8")).get("points")
        self._refresh_reference()


# ======================== にんじんを電子レンジに入れたか ========================
class MicrowaveLoading:
    """にんじんが電子レンジに入ったか・出てきたかを決める。

    入れた：扉が開いている間に、にんじんが扉の入り口のあたりで見えなくなり、そのまま扉が閉まった
    出した：中に入っているときに、にんじんがまた見えた
    """

    HIDDEN_SECONDS = 0.4  # これだけ見えなかったら「見えなくなった」とする（1 枚だけ見落としても気にしない）
    OPEN_DEGREES = 20  # これより開いていたら「開いている」
    CLOSED_DEGREES = 8  # これより閉まっていたら「閉まった」

    def __init__(self):
        self.inside = False  # にんじんが電子レンジの中にあるか
        self.maybe_in = False  # 扉の入り口で見えなくなった（扉が閉まったら「入れた」になる）
        self.last_seen = 0.0
        self.last_center = None
        self.message = ""
        self.message_time = 0.0

    def update(self, carrot, door, now):
        """1 枚ごとに呼ぶ。carrot は見つけたにんじん（なければ None）。起きたこと（"入れた" / "出した" / None）を返す"""
        degrees = door.degrees or 0.0
        if carrot is not None:
            self.last_seen = now
            self.last_center = carrot.center
            self.maybe_in = False
            if self.inside:
                self.inside = False
                return self._say("出した")
            return None

        hidden = now - self.last_seen > self.HIDDEN_SECONDS
        if hidden and not self.inside and degrees > self.OPEN_DEGREES and self.last_center and door.in_door_area(self.last_center):
            self.maybe_in = True
        if self.maybe_in and degrees < self.CLOSED_DEGREES:
            self.maybe_in = False
            self.inside = True
            return self._say("入れた")
        return None

    def _say(self, what):
        self.message, self.message_time = what, time.time()
        print("にんじんを電子レンジに", what)
        return what

    def status(self):
        if time.time() - self.message_time < 2:
            return f"★ にんじんを電子レンジに{self.message}！"
        if self.inside:
            return "にんじん：電子レンジの中"
        if self.maybe_in:
            return "にんじん：入れたかも（扉を閉めたら「入れた」）"
        return "にんじん：外"


def find_carrot(detector, frame, min_conf=0.4):
    """学習モデルで「野菜」を探し、いちばん確かなものを返す（なければ None）"""
    if detector is None:
        return None
    vegetables = [d for d in detector(frame) if d.cls in ("野菜", "carrot") and d.conf >= min_conf]
    return max(vegetables, key=lambda d: d.conf, default=None)


# ======================== Unity へ送る（サーバーの代わり） ========================
class ScreenServer:
    """ws://<この PC>:<port>/screen を開いて、つないできた画面（Unity）にイベントを配る。
    別のスレッドで動く。send_door() はどのスレッドから呼んでもよい"""

    def __init__(self, port):
        self.port = port
        self.screens = set()
        self.seq = 0
        self.door_angle = 0.0
        self.loop = asyncio.new_event_loop()
        threading.Thread(target=self._run, daemon=True).start()

    def _run(self):
        asyncio.set_event_loop(self.loop)
        self.loop.run_until_complete(self._serve())

    async def _serve(self):
        from websockets.asyncio.server import serve

        async with serve(self._handle, "0.0.0.0", self.port):
            print(f"Unity を待っています: ws://localhost:{self.port}/screen")
            await asyncio.Future()  # ずっと動かす

    async def _handle(self, connection):
        self.screens.add(connection)
        print("画面がつながりました", connection.remote_address)
        await connection.send(self._message(self._door_body(self.door_angle)))  # 今の状態を伝える
        try:
            async for text in connection:
                await self._on_message(text)
        finally:
            self.screens.discard(connection)
            print("画面が切れました")

    async def _on_message(self, text):
        """画面から届いたもの。reset を頼まれたら、本物のサーバーと同じく全部の画面に reset を配る"""
        try:
            message = json.loads(text)
        except json.JSONDecodeError:
            return
        if message.get("event") == "reset":
            self.door_angle = 0.0
            await self._broadcast({"event": "reset"})

    @staticmethod
    def _door_body(degrees):
        degrees = float(degrees)  # numpy の数のままだと JSON にできない
        return {"event": "door", "open": degrees > 5, "angle": round(degrees, 1)}

    def _message(self, body):
        self.seq += 1
        return json.dumps({"device": "door-test", "ts": int(time.time() * 1000), "seq": self.seq, **body})

    async def _broadcast(self, body):
        text = self._message(body)
        for screen in list(self.screens):
            try:
                await screen.send(text)
            except Exception:  # noqa: BLE001  切れた画面は、次の受け取りで消える
                pass

    def send_door(self, degrees):
        self.door_angle = degrees
        asyncio.run_coroutine_threadsafe(self._broadcast(self._door_body(degrees)), self.loop)


# ======================== 画面 ========================
def open_camera(index, width, height):
    backend = cv2.CAP_DSHOW if platform.system() == "Windows" else cv2.CAP_ANY
    cap = cv2.VideoCapture(index, backend)
    # 大きさを先に決めてから MJPG にする（順番が逆だと Windows では効かず、1 秒に 5 枚しか来ない）
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
    cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
    if not cap.isOpened():
        raise SystemExit(f"カメラ {index} を開けません")
    return cap


def draw(frame, door, server, carrot, loading, clicks, clicking):
    """カメラの映像に、蝶番・扉の向き・今の角度・にんじんを重ねる"""
    view = frame.copy()
    door.draw(view)
    if carrot is not None:
        cv2.rectangle(view, (int(carrot.x1), int(carrot.y1)), (int(carrot.x2), int(carrot.y2)), (0, 140, 255), 3)
    for point in clicks:
        cv2.circle(view, tuple(point), 8, (255, 0, 255), -1)
    angle = "わからない" if door.degrees is None else f"{door.degrees:.0f} 度"
    follow = f"カメラのずれ：合わせた（目印 {door.alignment.matches} 点）" if door.aligned_ok else \
        f"カメラのずれ：合わせられない（目印 {door.alignment.matches} 点。前のずれのまま）"
    lines = [
        f"扉の角度: {angle}（見つかり具合 {door.score:.0%}）",
        loading.status(),
        follow,
        f"Unity: {len(server.screens)} 台つながっている",
        next_step(door, clicks, clicking),
        "c: 閉めたを覚える  h: 蝶番と扉の先をクリック  o: 開けたを覚える  t: 送る確認  q: 終わる",
    ]
    return draw_lines(view, lines, size=22)


def next_step(door, clicks, clicking):
    """次にやることを 1 行で出す"""
    if clicking:
        return f"→ クリック：{CLICK_NAMES[len(clicks)]}"
    if door.reference_frame is None:
        return "→ 扉を閉めて、手を外に出して c"
    if door.reference_points is None:
        return "→ h を押して、蝶番 → 閉めた扉の先 をクリック"
    if not door.has_open:
        return "→ 扉をいっぱいに開けて、手を外に出して o"
    return "準備できた：扉を開け閉めしてみて（赤い丸 = 蝶番、黄色の線 = 扉）"


class MissRecorder:
    """扉がはっきりしなかったときの映像を、数枚だけ保存する（あとで原因を調べるため）"""

    def __init__(self):
        self.saved = 0
        self.last_time = 0.0

    def record(self, frame, door):
        now = time.time()
        if self.saved >= MAX_MISS_IMAGES or now - self.last_time < 0.5:
            return
        MISS_DIR.mkdir(parents=True, exist_ok=True)
        view = frame.copy()
        door.draw(view)
        name = f"{time.strftime('%H%M%S')}_{self.saved}_score{door.score:.2f}.jpg"
        cv2.imwrite(str(MISS_DIR / name), view)
        self.saved += 1
        self.last_time = now


class SendLimiter:
    """角度が少し変わっただけで何度も送らないようにする（1.5 度以上変わったら送る。1 秒に 20 回まで）"""

    def __init__(self):
        self.last_degrees = None
        self.last_time = 0.0

    def should_send(self, degrees):
        now = time.time()
        changed = self.last_degrees is None or abs(degrees - self.last_degrees) >= 1.5
        if changed and now - self.last_time >= 0.05:
            self.last_degrees, self.last_time = degrees, now
            return True
        return False


# ======================== 本体 ========================
def main():
    ap = argparse.ArgumentParser(description="電子レンジの扉の角度をカメラで見て、Unity に送る（試し用）")
    ap.add_argument("--camera", type=int, default=2)
    ap.add_argument("--port", type=int, default=9000)
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    ap.add_argument("--open-degrees", type=float, default=90, help="いっぱいに開けたときの、本物の扉の角度（度）")
    ap.add_argument("--model", default=str(ROOT / "models" / "best.onnx"), help="にんじん（野菜）を見つける学習モデル")
    ap.add_argument("--no-carrot", action="store_true", help="にんじんを見ない（扉だけ）")
    args = ap.parse_args()

    cap = open_camera(args.camera, args.width, args.height)
    door = DoorAngle(args.camera, args.open_degrees)
    server = ScreenServer(args.port)
    limiter = SendLimiter()
    misses = MissRecorder()
    loading = MicrowaveLoading()
    detector = None
    if not args.no_carrot:
        from detect import Detector

        detector = Detector(args.model, conf=0.4)

    window = f"door camera {args.camera}"
    cv2.namedWindow(window)
    clicks = []  # h を押したあとにクリックした点
    clicking = {"on": False}

    def on_mouse(event, x, y, flags, _):
        if event == cv2.EVENT_LBUTTONDOWN and clicking["on"]:
            clicks.append([x, y])
            if len(clicks) == len(CLICK_NAMES):
                door.set_points(clicks[0], clicks[1])
                clicks.clear()
                clicking["on"] = False

    cv2.setMouseCallback(window, on_mouse)
    smoothed = None
    last_follow = 0.0

    while True:
        ok, frame = cap.read()
        if not ok:
            continue
        if time.time() - last_follow > FOLLOW_SECONDS:
            door.follow_camera(frame)  # カメラが動いていたら、蝶番などをずらして合わせる
            last_follow = time.time()
        if door.ready and not door.measure(frame):
            misses.record(frame, door)
        if door.degrees is not None:
            smoothed = door.degrees if smoothed is None else 0.3 * smoothed + 0.7 * door.degrees  # 少しだけなめらかにする
            if limiter.should_send(smoothed):
                server.send_door(smoothed)
        carrot = find_carrot(detector, frame)
        loading.update(carrot, door, time.time())
        cv2.imshow(window, draw(frame, door, server, carrot, loading, clicks, clicking["on"]))

        key = cv2.waitKey(1) & 0xFF
        if key in (ord("q"), 27):
            break
        if key == ord("c"):
            door.remember_closed(frame)
        if key == ord("h"):
            clicks.clear()
            clicking["on"] = True
        if key == ord("o"):
            door.remember_open(frame)
        if key == ord("t"):
            server.send_door(0.0 if server.door_angle > 5 else args.open_degrees)

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
