// 自前モデル(YOLO, ONNX形式) をブラウザで動かす。
// train/train.py が models/best.onnx と models/model.json ({imgsz, classes}) を出力する。
const YOLO = {
  session: null,
  classes: [],
  size: 640,
  loading: false,
  error: null,
  inputName: null,
  outputName: null,
  canvas: document.createElement("canvas"),

  async load(modelUrl) {
    if (this.loading) return;
    this.loading = true;
    this.error = null;
    try {
      ort.env.wasm.wasmPaths = "https://cdn.jsdelivr.net/npm/onnxruntime-web@1.20.1/dist/";
      const metaUrl = modelUrl.replace(/[^/]*$/, "model.json");
      const res = await fetch(metaUrl, { cache: "no-store" });
      if (!res.ok) throw new Error(`${metaUrl} が見つかりません（train/train.py を実行しましたか？）`);
      const meta = await res.json();
      this.classes = meta.classes;
      this.size = meta.imgsz || 640;
      const head = await fetch(modelUrl, { method: "HEAD", cache: "no-store" });
      if (!head.ok) throw new Error(`${modelUrl} が見つかりません（train/train.py を実行しましたか？）`);
      try {
        this.session = await ort.InferenceSession.create(modelUrl, { executionProviders: ["webgpu", "wasm"] });
      } catch (_) {
        this.session = await ort.InferenceSession.create(modelUrl, { executionProviders: ["wasm"] });
      }
      this.inputName = this.session.inputNames[0];
      this.outputName = this.session.outputNames[0];
    } catch (e) {
      this.error = e;
      this.session = null;
      throw e;
    } finally {
      this.loading = false;
    }
  },

  // 映像 1 フレームを検出して正規化座標の枠 [{x,y,w,h,cls,score}] を返す
  async detect(video, minScore = 0.4, iouThr = 0.45) {
    const S = this.size, W = video.videoWidth, H = video.videoHeight;
    // レターボックス（縦横比を保って S×S に収める）
    const r = Math.min(S / W, S / H);
    const nw = Math.round(W * r), nh = Math.round(H * r);
    const dx = (S - nw) / 2, dy = (S - nh) / 2;
    const c = this.canvas;
    if (c.width !== S) c.width = c.height = S;
    const ctx = c.getContext("2d", { willReadFrequently: true });
    ctx.fillStyle = "#727272";
    ctx.fillRect(0, 0, S, S);
    ctx.drawImage(video, dx, dy, nw, nh);
    const d = ctx.getImageData(0, 0, S, S).data;
    const n = S * S;
    const input = new Float32Array(3 * n);
    for (let i = 0, p = 0; i < n; i++, p += 4) {
      input[i] = d[p] / 255;
      input[n + i] = d[p + 1] / 255;
      input[2 * n + i] = d[p + 2] / 255;
    }
    const out = await this.session.run({ [this.inputName]: new ort.Tensor("float32", input, [1, 3, S, S]) });
    const t = out[this.outputName];
    const data = t.data;
    // ultralytics の出力は [1, 4+nc, N]。古い形式 [1, N, 4+nc] にも一応対応
    const transposed = t.dims[1] > t.dims[2];
    const N = transposed ? t.dims[1] : t.dims[2];
    const rows = transposed ? t.dims[2] : t.dims[1];
    const nc = rows - 4;
    const at = (row, i) => (transposed ? data[i * rows + row] : data[row * N + i]);

    const boxes = [];
    for (let i = 0; i < N; i++) {
      let best = 0, bi = -1;
      for (let k = 0; k < nc; k++) {
        const s = at(4 + k, i);
        if (s > best) { best = s; bi = k; }
      }
      if (best < minScore) continue;
      const cx = at(0, i), cy = at(1, i), w = at(2, i), h = at(3, i);
      const x = (cx - w / 2 - dx) / r / W, y = (cy - h / 2 - dy) / r / H;
      const bw = w / r / W, bh = h / r / H;
      const x0 = Math.max(0, x), y0 = Math.max(0, y);
      boxes.push({
        x: x0, y: y0,
        w: Math.min(1, x + bw) - x0, h: Math.min(1, y + bh) - y0,
        cls: this.classes[bi] ?? String(bi), score: best,
      });
    }
    return nms(boxes, iouThr);
  },
};

// クラスごとの Non-Maximum Suppression
function nms(boxes, thr) {
  boxes.sort((a, b) => b.score - a.score);
  const keep = [];
  for (const b of boxes) {
    if (keep.some((k) => k.cls === b.cls && iou(k, b) > thr)) continue;
    keep.push(b);
  }
  return keep;
}

function iou(a, b) {
  const ix = Math.max(0, Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x));
  const iy = Math.max(0, Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y));
  const inter = ix * iy;
  return inter / (a.w * a.h + b.w * b.h - inter + 1e-9);
}
