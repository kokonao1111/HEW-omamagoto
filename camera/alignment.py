"""カメラのずれを合わせる部品（door_watch.py・veg_watch.py で使う）。

覚えた映像と今の映像の、目印になる点（台の木目・角など）を見比べて、カメラがどれだけ動いたか（回転・移動・大きさ）を計算する。
アームでカメラを付け外ししても、だいたい同じ所を写していれば、覚えた位置（蝶番・コンロの 4 隅など）を自動でずらして使える。
台の上の物（電子レンジ・コンロ）は、台に作り付け（台の上で動かない）とする。
"""
import math

import cv2
import numpy as np


class CameraAlignment:
    """覚えた映像 → 今の映像 のずれ（3x3 の変換）を計算する"""

    MIN_MATCHES = 20  # これだけの点が合えば、ずれを信じる（木の台は模様が少なく、30 点くらいのこともある。おかしな答えは looks_reasonable で捨てる）

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
