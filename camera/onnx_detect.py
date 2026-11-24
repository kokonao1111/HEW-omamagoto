"""学習モデル（YOLOv8 を ONNX に書き出したもの。models/*.onnx）を、onnxruntime で直接動かす。

camera/detect.py（ultralytics を使う）と同じ形の結果（Det）を返す。ちがいは、GPU を使えること：
  Windows で onnxruntime-directml を入れてあれば、DirectML で GPU を使う（CUDA を入れなくても、NVIDIA・AMD・Intel の GPU で動く）。
  なければ CPU で動く。
"""
import ast

import cv2
import numpy as np
import onnxruntime as ort

from detect import Det

IOU_THRESHOLD = 0.5  # 同じ物に重なった枠を 1 つにまとめるときの重なり具合


class OnnxDetector:
    """models/*.onnx を読み、フレームから見つけた物のリスト（Det）を返す"""

    def __init__(self, model_path, conf=0.4, use_gpu=True):
        providers = ["CPUExecutionProvider"]
        if use_gpu and "DmlExecutionProvider" in ort.get_available_providers():
            providers.insert(0, "DmlExecutionProvider")
        self.session = ort.InferenceSession(str(model_path), providers=providers)
        self.device = "GPU (DirectML)" if self.session.get_providers()[0] == "DmlExecutionProvider" else "CPU"
        self.input_name = self.session.get_inputs()[0].name
        self.size = self.session.get_inputs()[0].shape[2]  # 正方形（640 など）
        metadata = self.session.get_modelmeta().custom_metadata_map
        self.names = ast.literal_eval(metadata["names"]) if "names" in metadata else {}
        self.conf = conf

    def __call__(self, frame):
        image, scale, pad_x, pad_y = self._letterbox(frame)
        output = self.session.run(None, {self.input_name: image})[0][0]  # (4 + クラスの数, 候補の数)
        boxes, scores, classes = self._decode(output)
        if len(boxes) == 0:
            return []
        # 枠を、元の映像の画素に戻す
        boxes[:, [0, 2]] = (boxes[:, [0, 2]] - pad_x) / scale
        boxes[:, [1, 3]] = (boxes[:, [1, 3]] - pad_y) / scale
        keep = cv2.dnn.NMSBoxes(
            [[float(x1), float(y1), float(x2 - x1), float(y2 - y1)] for x1, y1, x2, y2 in boxes],
            scores.tolist(), self.conf, IOU_THRESHOLD)
        return [Det(self.names.get(int(classes[i]), str(int(classes[i]))), float(scores[i]), *map(float, boxes[i]))
                for i in np.array(keep).flatten()]

    def _letterbox(self, frame):
        """縦横の比を変えずに、正方形（size × size）の中に縮めて入れる（すき間は灰色）"""
        height, width = frame.shape[:2]
        scale = self.size / max(height, width)
        new_w, new_h = int(round(width * scale)), int(round(height * scale))
        pad_x, pad_y = (self.size - new_w) // 2, (self.size - new_h) // 2
        canvas = np.full((self.size, self.size, 3), 114, np.uint8)
        canvas[pad_y:pad_y + new_h, pad_x:pad_x + new_w] = cv2.resize(frame, (new_w, new_h), interpolation=cv2.INTER_LINEAR)
        image = cv2.cvtColor(canvas, cv2.COLOR_BGR2RGB).transpose(2, 0, 1)[None].astype(np.float32) / 255.0
        return image, scale, pad_x, pad_y

    def _decode(self, output):
        """モデルの出力（中心 x・中心 y・幅・高さ・クラスごとの点数）から、点数の高い候補だけを取り出す"""
        class_scores = output[4:]
        classes = class_scores.argmax(axis=0)
        scores = class_scores.max(axis=0)
        chosen = scores >= self.conf
        cx, cy, w, h = output[:4, chosen]
        boxes = np.stack([cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2], axis=1)
        return boxes, scores[chosen], classes[chosen]
