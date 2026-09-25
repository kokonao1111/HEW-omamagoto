"""ゲームロジック（カメラ側で判断する部分）
- 位置: 毎フレーム、まな板座標 (0〜1) に変換して送る。包丁は刃先と柄尻の 2 点
- 切断: 手が一定時間映っていないときにまな板ゾーンのピース数を確定し、前回より増えていたら cut
- 投入: まな板ゾーンのピースが減り、フライパンゾーンで増えたら add
判定は直近 N フレームの多数決で行い、1 フレームの誤検出に反応しない。"""
import math
from collections import Counter, deque

# モデルのクラス名 → JSON で送る item 名
ITEM_IDS = {
    "野菜": "carrot", "にんじん": "carrot", "きゅうり": "cucumber", "たまねぎ": "onion",
    "包丁": "knife", "フライパン": "pan", "ピース": "piece",
}
KNIFE = {"包丁", "knife"}
HAND = {"手", "hand", "その他", "other"}  # YOLO にこのクラスがあれば手検出として使う
TOOLS = KNIFE | HAND | {"フライパン", "pan"}


def item_id(cls):
    return ITEM_IDS.get(cls, cls)


class GameLogic:
    def __init__(self, hand_clear_sec=0.5, vote_frames=5):
        self.hand_clear_sec = hand_clear_sec
        self.vote = vote_frames
        self.history = deque(maxlen=vote_frames)  # 各フレームの {(zone, item): count}
        self.confirmed = {}  # 手がどいたときに確定した {(zone, item): count}
        self.hand_absent_since = None
        self.hand_present = None  # 前回送った状態
        self.visible = {}  # item -> 前回見えていたか

    def update(self, dets, hands, calib, now):
        """1 フレーム分を処理して送信イベントのリストを返す"""
        events = []

        # ---- 手の有無（MediaPipe の手 + YOLO の「手」クラス）----
        hands = list(hands) + [(d.center, (d.x1, d.y1, d.x2, d.y2)) for d in dets if d.cls in HAND]
        present = len(hands) > 0
        if present:
            self.hand_absent_since = None
        elif self.hand_absent_since is None:
            self.hand_absent_since = now
        if present != self.hand_present:
            self.hand_present = present
            events.append({"event": "hand", "present": present})

        # ---- 位置イベント ----
        seen = set()
        knife = max((d for d in dets if d.cls in KNIFE), key=lambda d: d.conf, default=None)
        if knife is not None:
            tip, grip = self._knife_ends(knife, hands)
            seen.add("knife")
            events.append({"event": "position", "item": "knife",
                           "tip": [round(v, 3) for v in calib.to_board(tip)],
                           "grip": [round(v, 3) for v in calib.to_board(grip)], "visible": True})
        best = {}
        for d in dets:
            if d.cls in KNIFE:
                continue
            key = item_id(d.cls)
            if key not in best or d.conf > best[key].conf:
                best[key] = d
        for key, d in best.items():
            u, v = calib.to_board(d.center)
            seen.add(key)
            events.append({"event": "position", "item": key, "x": round(u, 3), "y": round(v, 3),
                           "rot": self._rot(d), "visible": True})
        for key, was in list(self.visible.items()):
            if was and key not in seen:
                events.append({"event": "position", "item": key, "visible": False})
        self.visible = {k: True for k in seen}

        # ---- ゾーンごとのピース数（今フレーム）----
        counts = Counter()
        for d in dets:
            if d.cls in TOOLS:
                continue
            zone = calib.zone_of(d.center)
            if zone:
                counts[(zone, item_id(d.cls))] += 1
        self.history.append(counts)

        # ---- 手がどいて一定時間たったら確定して比較 ----
        if not present and self.hand_absent_since is not None and now - self.hand_absent_since >= self.hand_clear_sec and len(self.history) >= self.vote:
            voted = self._majority()
            for (zone, item), n in voted.items():
                prev = self.confirmed.get((zone, item), 0)
                # 0→1 は「置いた」なので切断ではない。1→2 のように増えたときだけ切断
                if zone == "board" and prev >= 1 and n > prev:
                    events.append({"event": "cut", "item": item, "count": n})
                if zone in ("pan", "plate") and n > prev:
                    events.append({"event": "add", "item": item, "zone": zone, "count": n - prev})
            # 消えたものは 0 に
            for key in list(self.confirmed):
                if key not in voted:
                    voted[key] = 0
            self.confirmed = voted

        return events

    # ---------- 補助 ----------
    def _majority(self):
        keys = set(k for c in self.history for k in c)
        out = {}
        for k in keys:
            vals = [c.get(k, 0) for c in self.history]
            out[k] = Counter(vals).most_common(1)[0][0]
        return {k: v for k, v in out.items() if v > 0}

    @staticmethod
    def _rot(d):
        w, h = d.size
        return 0 if w >= h else 90  # 枠の縦横比だけの粗い向き（横長=0°、縦長=90°）

    @staticmethod
    def _knife_ends(d, hands):
        """包丁の枠の長辺の両端を求め、手に近い方を柄尻、遠い方を刃先とする"""
        cx, cy = d.center
        w, h = d.size
        if w >= h:
            a, b = (d.x1, cy), (d.x2, cy)
        else:
            a, b = (cx, d.y1), (cx, d.y2)
        if hands:
            hx, hy = hands[0][0]
            da = math.hypot(a[0] - hx, a[1] - hy)
            db = math.hypot(b[0] - hx, b[1] - hy)
            grip, tip = (a, b) if da < db else (b, a)
        else:
            grip, tip = (a, b) if a[1] < b[1] else (b, a)  # 手が無ければ下側を刃先とみなす
        return tip, grip
