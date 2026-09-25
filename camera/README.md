# カメラ側プログラム（Python）

Web カメラの映像から野菜・包丁・手を認識し、まな板座標に変換して「位置」「切った」「入れた」を JSON で送る。
要件定義書の 5.6〜5.8 と 6.3 に対応する。

## 動かす

```sh
.venv/bin/python camera/main.py                     # カメラ 0、サーバー無し（イベントはコンソールに出る）
.venv/bin/python camera/main.py --camera 1          # 別のカメラ
.venv/bin/python camera/main.py --server ws://omamagoto.local:9000   # サーバーに送る
.venv/bin/python camera/main.py --source test.mp4   # 録画した動画で試す（毎回同じ条件でテストできる）
```

初回は `c` キーでキャリブレーションする（まな板 → フライパン → 皿 の順に 4 隅をクリック。フライパン・皿は `s` で飛ばせる）。
設定は `camera/config/calib_cam<N>.json` に保存され、次回からそのまま使う。

| キー | 動き |
| --- | --- |
| c | キャリブレーション |
| b | まな板の範囲だけ切り出して推論（野菜が大きく映るので精度が上がる） |
| q | 終了 |

## ファイル

| ファイル | 役割 |
| --- | --- |
| `main.py` | カメラ読み込み、ループ、デバッグ表示 |
| `detect.py` | YOLO（`models/best.onnx`）と手検出（MediaPipe HandLandmarker） |
| `calibration.py` | 4 隅クリック → 射影変換 → まな板座標 (0〜1)、ゾーン判定 |
| `logic.py` | 位置イベント、ピース数の多数決、切断・投入の判定 |
| `sender.py` | WebSocket 送信（自動再接続）。URL 無しならコンソール出力 |

## 判定のルール（logic.py）

- 位置は毎フレーム。包丁は刃先と柄尻の 2 点（手に近い端を柄尻とする）。見失ったら `visible: false` を 1 回送る
- ピース数は直近 5 フレームの多数決
- 手が 0.5 秒映っていないときに確定。まな板のピース数が 1→2 のように増えたら `cut`。0→1 は「置いた」なので何も出ない
- フライパン・皿ゾーンでピース数が増えたら `add`

## 送る JSON（要件定義書 6.3）

```json
{ "device": "camera", "ts": 1760000000123, "seq": 1, "event": "position", "item": "knife", "tip": [0.31, 0.12], "grip": [0.55, 0.05], "visible": true }
{ "device": "camera", "ts": 1760000000123, "seq": 2, "event": "position", "item": "carrot", "x": 0.42, "y": 0.50, "rot": 0, "visible": true }
{ "device": "camera", "ts": 1760000002000, "seq": 3, "event": "cut", "item": "carrot", "count": 2 }
{ "device": "camera", "ts": 1760000003000, "seq": 4, "event": "add", "item": "carrot", "zone": "pan", "count": 1 }
{ "device": "camera", "ts": 1760000004000, "seq": 5, "event": "hand", "present": false }
```

## まだ無いもの

- 3 台のカメラの統合（今は 1 台）
- 振り下ろしの即時演出イベント（`swing`）
- 「ピース」クラスの学習（今は切った半分も「野菜」として数えている）
