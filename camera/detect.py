"""物体検出（YOLO）と手検出（MediaPipe）のラッパー"""
from dataclasses import dataclass

import cv2
import numpy as np


@dataclass
class Det:
    cls: str
    conf: float
    x1: float
    y1: float
    x2: float
    y2: float

    @property
    def center(self):
        return ((self.x1 + self.x2) / 2, (self.y1 + self.y2) / 2)

    @property
    def size(self):
        return (self.x2 - self.x1, self.y2 - self.y1)


class Detector:
    """models/best.onnx または best.pt を読み、フレームから検出リストを返す"""

    def __init__(self, model_path, conf=0.4, imgsz=640):
        from ultralytics import YOLO

        self.model = YOLO(str(model_path), task="detect")
        self.conf = conf
        self.imgsz = imgsz
        self.names = self.model.names

    def __call__(self, frame, crop=None):
        """crop=(x1,y1,x2,y2) を渡すとその範囲だけを推論する（まな板を大きく見せて精度を上げる）"""
        ox, oy = 0, 0
        img = frame
        if crop is not None:
            x1, y1, x2, y2 = [int(v) for v in crop]
            img = frame[y1:y2, x1:x2]
            ox, oy = x1, y1
        res = self.model.predict(img, conf=self.conf, imgsz=self.imgsz, verbose=False)[0]
        out = []
        for b in res.boxes:
            x1, y1, x2, y2 = b.xyxy[0].tolist()
            out.append(Det(self.names[int(b.cls[0])], float(b.conf[0]), x1 + ox, y1 + oy, x2 + ox, y2 + oy))
        return out


HAND_MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"


class HandDetector:
    """MediaPipe HandLandmarker（Tasks API）。モデルが無ければダウンロードする。使えない環境では常に「手なし」を返す"""

    def __init__(self, model_path=None, max_hands=2):
        from pathlib import Path

        self.hands = None
        self.legacy = None
        self.mp = None
        self.t0 = None
        path = Path(model_path) if model_path else Path(__file__).resolve().parent.parent / "models" / "hand_landmarker.task"
        try:
            import mediapipe as mp

            if hasattr(mp, "solutions"):
                # mediapipe 0.10 系: 従来 API（CPU で動き、macOS でも安定）
                self.legacy = mp.solutions.hands.Hands(max_num_hands=max_hands, min_detection_confidence=0.5, min_tracking_confidence=0.5)
                return
            from mediapipe.tasks.python import BaseOptions
            from mediapipe.tasks.python import vision

            if not path.exists():
                import urllib.request

                print("手検出モデルをダウンロード中…", path.name)
                path.parent.mkdir(parents=True, exist_ok=True)
                urllib.request.urlretrieve(HAND_MODEL_URL, path)
            opts = vision.HandLandmarkerOptions(
                # macOS では GPU(Metal) 委譲がクラッシュするので CPU で動かす（十分速い）
                base_options=BaseOptions(model_asset_path=str(path), delegate=BaseOptions.Delegate.CPU),
                running_mode=vision.RunningMode.VIDEO,
                num_hands=max_hands,
                min_hand_detection_confidence=0.5,
                min_tracking_confidence=0.5,
            )
            self.hands = vision.HandLandmarker.create_from_options(opts)
            self.mp = mp
        except Exception as e:  # noqa: BLE001
            print("手検出を無効化:", e)

    def __call__(self, frame):
        """検出した手ごとに (中心(x,y), 枠(x1,y1,x2,y2)) を返す"""
        h, w = frame.shape[:2]
        if self.legacy is not None:
            res = self.legacy.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
            out = []
            for lm in res.multi_hand_landmarks or []:
                xs = [p.x * w for p in lm.landmark]
                ys = [p.y * h for p in lm.landmark]
                out.append(((float(np.mean(xs)), float(np.mean(ys))), (min(xs), min(ys), max(xs), max(ys))))
            return out
        if self.hands is None:
            return []
        import time

        if self.t0 is None:
            self.t0 = time.time()
        img = self.mp.Image(image_format=self.mp.ImageFormat.SRGB, data=cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        res = self.hands.detect_for_video(img, int((time.time() - self.t0) * 1000))
        out = []
        for lm in res.hand_landmarks or []:
            xs = [p.x * w for p in lm]
            ys = [p.y * h for p in lm]
            out.append(((float(np.mean(xs)), float(np.mean(ys))), (min(xs), min(ys), max(xs), max(ys))))
        return out
