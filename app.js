// カット検出プロトタイプ
// 野菜と包丁を「見せて学習(背景差分 + MobileNet特徴 + kNN)」「色追跡」「汎用AI(coco-ssd)」のいずれかで見つけ、
// 包丁が野菜の中に振り下ろされた / 野菜が2つに分かれた ときに CUT と判定する。

const $ = (id) => document.getElementById(id);
const video = $("video");
const overlay = $("overlay");
const octx = overlay.getContext("2d");
const stage = $("stage");

// 解析用の小さいキャンバス（差分・色追跡は低解像度で十分・速い）
const WORK_W = 160;
const work = document.createElement("canvas");
const wctx = work.getContext("2d", { willReadFrequently: true });
// 学習/判定用の切り出しキャンバス
const crop = document.createElement("canvas");
crop.width = crop.height = 224;
const cctx = crop.getContext("2d", { willReadFrequently: true });

const KNN_KEY = "cutproto.knn.v1";

// 色追跡の初期色（HSV: h 0-360, s/v 0-100）
const PRESET = {
  veg: { h: 25, s: 85, v: 90 },   // にんじんのオレンジ
  knife: { h: 120, s: 65, v: 70 }, // 緑
};

const state = {
  running: false,
  // 汎用AI
  model: null, modelName: null, modelLoading: false,
  // 見せて学習
  embedder: null, embedderLoading: false,
  knn: knnClassifier.create(),
  bg: null,        // 背景（何も置かない）の RGBA
  bgVeg: null,     // 野菜を置いた状態の RGBA
  lockedVeg: null, // ②で固定した野菜の位置
  teaching: null, lastTeachT: 0,
  lastVeg: null, lastVegT: 0,
  // 色追跡（初期値: 野菜=にんじんのオレンジ, 包丁=緑。クリック登録で上書きできる）
  colors: { veg: { ...PRESET.veg }, knife: { ...PRESET.knife } }, // {h,s,v}
  picking: null, // "veg" | "knife" | null
  count: 0,
  // 振り下ろし判定
  swing: { phase: "idle", aboveAt: 0 }, // idle -> above -> (cut) -> idle
  // 分離判定
  split: { singleFrames: 0, multiFrames: 0, wasSingle: false },
  // 統計
  frames: [], // {t, veg, knife}
  lastT: performance.now(),
};

// ---------- UI バインド ----------
for (const id of ["tol", "minArea", "minScore", "depth", "windowMs", "diffThr", "fgMinArea", "learnConf", "yoloScore"]) {
  const el = $(id);
  const out = $(id + "V");
  const upd = () => (out.textContent = el.value);
  el.addEventListener("input", upd);
  upd();
}
$("startBtn").addEventListener("click", startCamera);
$("resetBtn").addEventListener("click", () => {
  state.count = 0;
  $("count").textContent = "0";
  $("log").innerHTML = "";
});
$("mirror").addEventListener("change", () => video.classList.toggle("mirror", $("mirror").checked));
$("pickVeg").addEventListener("click", () => beginPick("veg"));
$("pickKnife").addEventListener("click", () => beginPick("knife"));
$("presetVeg").addEventListener("click", () => setColor("veg", PRESET.veg, "初期値のオレンジ"));
$("presetKnife").addEventListener("click", () => setColor("knife", PRESET.knife, "初期値の緑"));
stage.addEventListener("click", onStageClick);
$("vegSrc").addEventListener("change", updateSrcUI);
$("knifeSrc").addEventListener("change", updateSrcUI);
$("aiModel").addEventListener("change", () => ensureModel());
$("yoloLoad").addEventListener("click", () => { YOLO.error = null; YOLO.session = null; ensureYolo(); });
$("bgBtn").addEventListener("click", captureBg);
$("bgVegBtn").addEventListener("click", captureBgVeg);
$("bgVegClr").addEventListener("click", () => {
  state.bgVeg = null; state.lockedVeg = null;
  $("bgVegStat").textContent = "野菜: 未固定"; $("bgVegStat").className = "muted";
  log("野菜の位置固定を解除");
});
for (const b of document.querySelectorAll("button.teach")) {
  b.addEventListener("pointerdown", (e) => { e.preventDefault(); state.teaching = b.dataset.label; b.classList.add("on"); });
}
const stopTeach = () => { state.teaching = null; document.querySelectorAll("button.teach.on").forEach((b) => b.classList.remove("on")); };
window.addEventListener("pointerup", stopTeach);
window.addEventListener("pointercancel", stopTeach);
$("photoFile").addEventListener("change", learnFromPhotos);
$("clearLearn").addEventListener("click", () => {
  state.knn.clearAllClasses();
  try { localStorage.removeItem(KNN_KEY); } catch (_) {}
  updateSampleCounts();
  log("学習データを消しました");
});
updateSrcUI();
loadKnn();
setColor("veg", state.colors.veg);
setColor("knife", state.colors.knife);

// ---------- 手順ウィザード ----------
const TEACH_TARGET = 20; // バーが満タンになるサンプル数
function setStep(n) {
  for (const li of document.querySelectorAll("#steps li")) {
    const s = +li.dataset.step;
    li.classList.toggle("active", s === n);
    li.classList.toggle("done", s < n);
  }
}
// 済みの手順見出しをクリックするとそこへ戻れる
document.querySelectorAll("#steps li > b").forEach((b) => {
  b.addEventListener("click", () => {
    const li = b.parentElement;
    if (li.classList.contains("done")) setStep(+li.dataset.step);
  });
});
$("toStep5").addEventListener("click", () => {
  const c = state.knn.getClassExampleCount();
  if (!c["包丁"]) log("包丁は未学習のまま進みます（動いているもの＝包丁として仮判定）");
  setStep(5);
  hint("");
});
$("restartBtn").addEventListener("click", () => {
  state.bg = state.bgVeg = state.lockedVeg = null;
  $("bgStat").textContent = "背景: 未撮影"; $("bgStat").className = "muted";
  $("bgVegStat").textContent = "野菜: 未固定"; $("bgVegStat").className = "muted";
  setStep(2);
});

function updateSrcUI() {
  const v = $("vegSrc").value, k = $("knifeSrc").value;
  const needAI = v === "ai" || k === "ai";
  const needColor = v === "color" || k === "color";
  const needLearn = v === "learn" || k === "learn";
  const needYolo = v === "yolo" || k === "yolo";
  $("aiBox").classList.toggle("hidden", !needAI);
  $("colorBox").classList.toggle("hidden", !needColor);
  $("learnBox").classList.toggle("hidden", !needLearn);
  $("yoloBox").classList.toggle("hidden", !needYolo);
  if (state.running) {
    if (needAI) ensureModel();
    if (needLearn) ensureEmbedder();
    if (needYolo) ensureYolo();
  }
}

// ---------- 自前モデル(YOLO) ----------
async function ensureYolo() {
  if (YOLO.session || YOLO.loading || YOLO.error) return; // 失敗したら「読み込む」を押すまで再試行しない
  const url = $("yoloModel").value.trim();
  $("yoloStat").textContent = "読み込み中…"; $("yoloStat").className = "muted";
  hint("自前モデルを読み込み中…");
  try {
    await YOLO.load(url);
    $("yoloStat").textContent = `読み込み済み: ${YOLO.classes.join(", ")}`; $("yoloStat").className = "ok";
    hint("");
    log(`自前モデル読み込み完了 (${YOLO.classes.length} クラス)`);
  } catch (e) {
    $("yoloStat").textContent = "失敗: " + e.message; $("yoloStat").className = "muted";
    hint("自前モデルの読み込みに失敗: " + e.message);
    log("自前モデル読み込み失敗: " + e.message);
  }
}

function hint(msg) {
  $("hint").textContent = msg;
}

function log(msg) {
  const li = document.createElement("li");
  li.textContent = `${new Date().toLocaleTimeString()}  ${msg}`;
  $("log").prepend(li);
  while ($("log").children.length > 30) $("log").lastChild.remove();
}

// ---------- カメラ ----------
async function listCameras() {
  const devs = await navigator.mediaDevices.enumerateDevices();
  const sel = $("camSel");
  const cur = sel.value;
  sel.innerHTML = '<option value="">（既定のカメラ）</option>';
  devs
    .filter((d) => d.kind === "videoinput")
    .forEach((d, i) => {
      const o = document.createElement("option");
      o.value = d.deviceId;
      o.textContent = d.label || `カメラ ${i + 1}`;
      sel.appendChild(o);
    });
  sel.value = cur;
}

async function startCamera() {
  try {
    if (video.srcObject) video.srcObject.getTracks().forEach((t) => t.stop());
    const id = $("camSel").value;
    const stream = await navigator.mediaDevices.getUserMedia({
      video: id ? { deviceId: { exact: id } } : { facingMode: "environment", width: { ideal: 1280 } },
      audio: false,
    });
    video.srcObject = stream;
    await video.play();
    await listCameras();
    overlay.width = video.videoWidth;
    overlay.height = video.videoHeight;
    work.width = WORK_W;
    work.height = Math.round((WORK_W * video.videoHeight) / video.videoWidth);
    state.bg = state.bgVeg = state.lockedVeg = null; // 解像度が変わるので撮り直し
    hint("");
    log(`カメラ開始 ${video.videoWidth}x${video.videoHeight}`);
    if (!state.running) {
      state.running = true;
      loop();
    }
    updateSrcUI();
    $("bgStat").textContent = "背景: 未撮影"; $("bgStat").className = "muted";
    $("bgVegStat").textContent = "野菜: 未固定"; $("bgVegStat").className = "muted";
    setStep(2);
  } catch (e) {
    hint("カメラを開けませんでした: " + e.message);
  }
}

// ---------- 汎用AI モデル ----------
async function ensureModel() {
  const name = $("aiModel").value;
  if (state.modelName === name || state.modelLoading) return;
  state.modelLoading = true;
  hint("AIモデルを読み込み中…");
  try {
    state.model = await cocoSsd.load({ base: name });
    state.modelName = name;
    hint("");
    log(`AIモデル読み込み完了 (${name})`);
  } catch (e) {
    hint("モデル読み込み失敗: " + e.message);
  } finally {
    state.modelLoading = false;
  }
}

// ---------- 見せて学習：特徴抽出器 ----------
async function ensureEmbedder() {
  if (state.embedder || state.embedderLoading) return;
  state.embedderLoading = true;
  hint("特徴抽出モデル(MobileNet)を読み込み中…");
  try {
    state.embedder = await mobilenet.load({ version: 1, alpha: 0.5 });
    hint("");
    log("MobileNet 読み込み完了");
  } catch (e) {
    hint("MobileNet 読み込み失敗: " + e.message);
  } finally {
    state.embedderLoading = false;
  }
}

function captureWork() {
  return new Uint8ClampedArray(wctx.getImageData(0, 0, work.width, work.height).data);
}

function captureBg() {
  if (!state.running) return hint("先にカメラを開始してください");
  wctx.drawImage(video, 0, 0, work.width, work.height);
  state.bg = captureWork();
  $("bgStat").textContent = "背景: 撮影済み"; $("bgStat").className = "ok";
  log("背景を撮影");
  hint("背景OK。次は野菜を置いて撮影してください");
  setStep(3);
}

function captureBgVeg() {
  if (!state.bg) return hint("先に①背景を撮影してください");
  wctx.drawImage(video, 0, 0, work.width, work.height);
  state.bgVeg = captureWork();
  const m = diffMask(state.bgVeg, state.bg, +$("diffThr").value);
  const blobs = maskBlobs(m, +$("fgMinArea").value);
  if (!blobs.length) {
    state.bgVeg = null;
    return hint("野菜が見つかりません。差分しきい値を下げるか、最小サイズを小さくしてください");
  }
  state.lockedVeg = blobs[0];
  $("bgVegStat").textContent = "野菜: 固定中"; $("bgVegStat").className = "ok";
  log(`野菜の位置を固定 (${Math.round(blobs[0].w * 100)}%×${Math.round(blobs[0].h * 100)}%)`);
  hint("野菜の位置を覚えました（緑の枠）。次は包丁を覚えさせます");
  setStep(4);
}

// 映像の指定範囲（正規化座標）を 224x224 に切り出す
function cropTo224(b) {
  const W = video.videoWidth, H = video.videoHeight;
  const pad = 0.15;
  let x = (b.x - b.w * pad) * W, y = (b.y - b.h * pad) * H, w = b.w * (1 + 2 * pad) * W, h = b.h * (1 + 2 * pad) * H;
  // 正方形にして枠内に収める
  const s = Math.max(w, h);
  x -= (s - w) / 2; y -= (s - h) / 2;
  x = Math.max(0, Math.min(W - s, x)); y = Math.max(0, Math.min(H - s, y));
  const ss = Math.min(s, W, H);
  cctx.drawImage(video, x, y, ss, ss, 0, 0, 224, 224);
  return crop;
}

function embed(canvas) {
  return state.embedder.infer(canvas, true);
}

async function classify(canvas) {
  if (!state.embedder || state.knn.getNumClasses() === 0) return null;
  const t = embed(canvas);
  try {
    const r = await state.knn.predictClass(t, 5);
    return { label: r.label, conf: r.confidences[r.label] };
  } finally { t.dispose(); }
}

function addSample(canvas, label) {
  const t = embed(canvas);
  state.knn.addExample(t, label);
  t.dispose();
  updateSampleCounts();
  saveKnnLater();
}

function updateSampleCounts() {
  const c = state.knn.getClassExampleCount();
  const keys = Object.keys(c);
  $("sampleCounts").textContent = keys.length ? "学習データ: " + keys.map((k) => `${k} ${c[k]}`).join(" / ") : "学習データ: なし";
  $("barKnife").style.width = Math.min(100, (100 * (c["包丁"] || 0)) / TEACH_TARGET) + "%";
  $("barOther").style.width = Math.min(100, (100 * (c["その他"] || 0)) / TEACH_TARGET) + "%";
}

async function learnFromPhotos(ev) {
  const files = [...ev.target.files];
  ev.target.value = "";
  if (!files.length) return;
  await ensureEmbedder();
  if (!state.embedder) return;
  const label = $("photoLabel").value;
  let n = 0;
  for (const f of files) {
    try {
      const bmp = await createImageBitmap(f);
      const s = Math.min(bmp.width, bmp.height);
      cctx.drawImage(bmp, (bmp.width - s) / 2, (bmp.height - s) / 2, s, s, 0, 0, 224, 224);
      addSample(crop, label);
      n++;
    } catch (e) { log(`写真の読み込み失敗: ${f.name}`); }
  }
  log(`写真 ${n} 枚を「${label}」として学習`);
}

let saveTimer = null;
function saveKnnLater() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => {
    try {
      const ds = state.knn.getClassifierDataset();
      const out = {};
      for (const k in ds) out[k] = { shape: ds[k].shape, data: Array.from(ds[k].dataSync(), (v) => +v.toFixed(4)) };
      localStorage.setItem(KNN_KEY, JSON.stringify(out));
    } catch (e) { log("学習データの保存に失敗: " + e.message); }
  }, 500);
}

function loadKnn() {
  try {
    const raw = localStorage.getItem(KNN_KEY);
    if (!raw) return;
    const obj = JSON.parse(raw), ds = {};
    for (const k in obj) ds[k] = tf.tensor(obj[k].data, obj[k].shape);
    state.knn.setClassifierDataset(ds);
    log("前回の学習データを読み込み");
  } catch (e) { log("学習データの読み込み失敗: " + e.message); }
  updateSampleCounts();
}

// ---------- 色登録 ----------
function beginPick(which) {
  if (!state.running) return hint("先にカメラを開始してください");
  state.picking = which;
  stage.classList.add("picking");
  hint(`映像内の「${which === "veg" ? "野菜" : "包丁"}」をクリックしてください`);
}

function onStageClick(ev) {
  if (!state.picking) return;
  const r = stage.getBoundingClientRect();
  let nx = (ev.clientX - r.left) / r.width;
  const ny = (ev.clientY - r.top) / r.height;
  if ($("mirror").checked) nx = 1 - nx;
  // 解析キャンバス上の 5x5 の中央値を取る
  const cx = Math.round(nx * work.width), cy = Math.round(ny * work.height);
  const d = wctx.getImageData(0, 0, work.width, work.height).data;
  const hs = [], ss = [], vs = [];
  for (let y = cy - 2; y <= cy + 2; y++)
    for (let x = cx - 2; x <= cx + 2; x++) {
      if (x < 0 || y < 0 || x >= work.width || y >= work.height) continue;
      const i = (y * work.width + x) * 4;
      const { h, s, v } = rgb2hsv(d[i], d[i + 1], d[i + 2]);
      hs.push(h); ss.push(s); vs.push(v);
    }
  const med = (a) => a.sort((p, q) => p - q)[a.length >> 1];
  setColor(state.picking, { h: med(hs), s: med(ss), v: med(vs) }, "クリック");
  state.picking = null;
  stage.classList.remove("picking");
  hint("");
}

function setColor(which, col, how) {
  state.colors[which] = col;
  // スウォッチ表示用に HSV → HSL 変換
  const l = col.v * (1 - col.s / 200);
  const sl = l === 0 || l === 100 ? 0 : ((col.v - l) / Math.min(l, 100 - l)) * 100;
  $(which === "veg" ? "swVeg" : "swKnife").style.background = `hsl(${col.h} ${sl}% ${l}%)`;
  if (how) log(`${which === "veg" ? "野菜" : "包丁"}の色を登録 (${how}) H${col.h.toFixed(0)} S${col.s.toFixed(0)} V${col.v.toFixed(0)}`);
}

function rgb2hsv(r, g, b) {
  r /= 255; g /= 255; b /= 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
  let h = 0;
  if (d > 0) {
    if (max === r) h = ((g - b) / d) % 6;
    else if (max === g) h = (b - r) / d + 2;
    else h = (r - g) / d + 4;
    h *= 60; if (h < 0) h += 360;
  }
  return { h, s: max ? (d / max) * 100 : 0, v: max * 100 };
}

// ---------- マスク生成 ----------
// 背景との差分（RGB いずれかの差が thr を超えたら前景）
function diffMask(cur, ref, thr) {
  const n = work.width * work.height;
  const m = new Uint8Array(n);
  for (let i = 0, p = 0; i < n; i++, p += 4) {
    const d = Math.max(Math.abs(cur[p] - ref[p]), Math.abs(cur[p + 1] - ref[p + 1]), Math.abs(cur[p + 2] - ref[p + 2]));
    if (d > thr) m[i] = 1;
  }
  return m;
}

// 登録色との HSV 距離
function colorMask(data, target, tol) {
  const n = work.width * work.height;
  const hueTol = tol, satTol = tol * 2.5, valTol = tol * 2.5;
  const lowSat = target.s < 20; // 白・黒・銀など彩度が低い色は色相を使わない
  const m = new Uint8Array(n);
  for (let i = 0, p = 0; i < n; i++, p += 4) {
    const { h, s, v } = rgb2hsv(data[p], data[p + 1], data[p + 2]);
    let ok;
    if (lowSat) ok = s < 20 + satTol / 2 && Math.abs(v - target.v) < valTol;
    else {
      let dh = Math.abs(h - target.h); if (dh > 180) dh = 360 - dh;
      ok = dh < hueTol && Math.abs(s - target.s) < satTol && Math.abs(v - target.v) < valTol && s > 15;
    }
    if (ok) m[i] = 1;
  }
  return m;
}

// マスク → 連結成分（4近傍）→ 正規化バウンディングボックス（面積降順）
function maskBlobs(m, minArea) {
  const W = work.width, H = work.height;
  const seen = new Uint8Array(W * H);
  const stack = new Int32Array(W * H);
  const blobs = [];
  for (let start = 0; start < W * H; start++) {
    if (!m[start] || seen[start]) continue;
    let sp = 0, n = 0, x0 = W, y0 = H, x1 = 0, y1 = 0;
    stack[sp++] = start; seen[start] = 1;
    while (sp) {
      const i = stack[--sp]; n++;
      const x = i % W, y = (i / W) | 0;
      if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
      if (x > 0 && m[i - 1] && !seen[i - 1]) { seen[i - 1] = 1; stack[sp++] = i - 1; }
      if (x < W - 1 && m[i + 1] && !seen[i + 1]) { seen[i + 1] = 1; stack[sp++] = i + 1; }
      if (y > 0 && m[i - W] && !seen[i - W]) { seen[i - W] = 1; stack[sp++] = i - W; }
      if (y < H - 1 && m[i + W] && !seen[i + W]) { seen[i + W] = 1; stack[sp++] = i + W; }
    }
    if (n >= minArea) blobs.push({ x: x0 / W, y: y0 / H, w: (x1 - x0 + 1) / W, h: (y1 - y0 + 1) / H, area: n });
  }
  blobs.sort((a, b) => b.area - a.area);
  return blobs;
}

function mergeMask(dst, src, val) {
  for (let i = 0; i < src.length; i++) if (src[i]) dst[i] = val;
}

// ---------- メインループ ----------
async function loop() {
  if (!state.running) return;
  const now = performance.now();
  const dt = now - state.lastT; state.lastT = now;

  wctx.drawImage(video, 0, 0, work.width, work.height);
  const srcV = $("vegSrc").value, srcK = $("knifeSrc").value;
  const useColor = srcV === "color" || srcK === "color";
  const useAI = srcV === "ai" || srcK === "ai";
  const useLearn = srcV === "learn" || srcK === "learn";
  const useYolo = srcV === "yolo" || srcK === "yolo";

  let vegBoxes = [], knifeBoxes = [], candidates = [];
  let mask = null; // 1=野菜色 2=包丁色 3=前景
  let data = null;

  if (useColor || useLearn) data = wctx.getImageData(0, 0, work.width, work.height).data;

  if (useColor) {
    const tol = +$("tol").value, minArea = +$("minArea").value;
    if ($("showMask").checked) mask = mask || new Uint8Array(work.width * work.height);
    if (srcV === "color" && state.colors.veg) {
      const m = colorMask(data, state.colors.veg, tol);
      if (mask) mergeMask(mask, m, 1);
      vegBoxes = maskBlobs(m, minArea);
    }
    if (srcK === "color" && state.colors.knife) {
      const m = colorMask(data, state.colors.knife, tol);
      if (mask) mergeMask(mask, m, 2);
      knifeBoxes = maskBlobs(m, minArea);
    }
  }

  if (useLearn) {
    if (!state.embedder) ensureEmbedder();
    const thr = +$("diffThr").value, minArea = +$("fgMinArea").value, conf = +$("learnConf").value;
    let fg = [];
    if (state.bg) {
      // 野菜の位置を固定している間は「野菜を置いた状態」を背景にする → 包丁が野菜に重なってもきれいに抜ける
      const ref = state.bgVeg || state.bg;
      const m = diffMask(data, ref, thr);
      if ($("showFg").checked) { mask = mask || new Uint8Array(work.width * work.height); mergeMask(mask, m, 3); }
      fg = maskBlobs(m, minArea).slice(0, 4);
    }
    const trained = state.embedder && state.knn.getNumClasses() > 0;
    if (trained) {
      for (const b of fg) {
        const r = await classify(cropTo224(b));
        if (r) { b.cls = r.label; b.score = r.conf; }
      }
    }
    candidates = fg;

    if (srcV === "learn") {
      if (state.lockedVeg) vegBoxes = [state.lockedVeg];
      else vegBoxes = fg.filter((b) => b.cls === "野菜" && b.score >= conf);
      // 野菜は動かないので、見失っても少しの間は前の位置を使う
      if (vegBoxes.length) { state.lastVeg = vegBoxes[0]; state.lastVegT = now; }
      else if (state.lastVeg && now - state.lastVegT < 1500) vegBoxes = [state.lastVeg];
    }
    if (srcK === "learn") {
      if (trained) knifeBoxes = fg.filter((b) => b.cls === "包丁" && b.score >= conf);
      else if (state.lockedVeg) knifeBoxes = fg.slice(0, 1); // 未学習でも位置固定なら「動いているもの＝包丁」とみなす
    }

    // 押している間、対象を切り出して覚える
    if (state.teaching && state.embedder && now - state.lastTeachT > 120) {
      state.lastTeachT = now;
      let src = null;
      if (state.bg) {
        // 学習は常に「何も置いていない背景」との差分から切り出す（野菜固定中でも野菜を覚えられるように）
        const m = state.bgVeg ? diffMask(data, state.bg, thr) : null;
        src = (m ? maskBlobs(m, minArea) : fg)[0] || null;
      }
      addSample(cropTo224(src || { x: 0, y: 0, w: 1, h: 1 }), state.teaching);
      if (!src) hint("背景を撮ってから学習すると対象だけを切り出して覚えられます（今は画面全体を学習中）");
    } else if (!state.bg && !state.teaching && useLearn) {
      hint("手順2「背景を撮影」を押してください（机の上に何も置かない状態で）");
    }
  }

  if (useAI) {
    if (!state.model) ensureModel();
    else {
      const minScore = +$("minScore").value;
      const vegCls = $("vegClasses").value.split(",").map((s) => s.trim()).filter(Boolean);
      const knifeCls = $("knifeClasses").value.split(",").map((s) => s.trim()).filter(Boolean);
      const preds = await state.model.detect(video, 10, minScore);
      const W = video.videoWidth, H = video.videoHeight;
      const all = preds.map((p) => ({ x: p.bbox[0] / W, y: p.bbox[1] / H, w: p.bbox[2] / W, h: p.bbox[3] / H, cls: p.class, score: p.score }));
      candidates = candidates.concat(all);
      if (srcV === "ai") vegBoxes = all.filter((b) => vegCls.includes(b.cls));
      if (srcK === "ai") knifeBoxes = all.filter((b) => knifeCls.includes(b.cls));
    }
  }

  if (useYolo) {
    if (!YOLO.session) ensureYolo();
    else {
      const split = (id) => $(id).value.split(",").map((s) => s.trim()).filter(Boolean);
      const knifeCls = split("yoloKnife"), ignore = split("yoloIgnore");
      const all = await YOLO.detect(video, +$("yoloScore").value);
      candidates = candidates.concat(all);
      if (srcV === "yolo") vegBoxes = all.filter((b) => !knifeCls.includes(b.cls) && !ignore.includes(b.cls));
      if (srcK === "yolo") knifeBoxes = all.filter((b) => knifeCls.includes(b.cls));
    }
  }

  const veg = vegBoxes[0] || null, knife = knifeBoxes[0] || null;
  const rel = judgeSwing(veg, knife, now);
  judgeSplit(vegBoxes);

  draw(vegBoxes, knifeBoxes, candidates, mask, veg, knife);
  updateStats(now, dt, !!veg, !!knife, rel);

  if (useAI || useLearn || useYolo) requestAnimationFrame(loop);
  else setTimeout(loop, 33);
}

// 振り下ろし判定：包丁の下端が野菜の上（rel<0）→ 制限時間内に rel>=depth まで入る
function judgeSwing(veg, knife, now) {
  const s = state.swing;
  const win = +$("windowMs").value;
  if (!$("useSwing").checked || !veg) { s.phase = "idle"; return null; }
  if (!knife) {
    // 一瞬見失っただけなら「上にいた」を覚えておく
    if (s.phase === "above" && now - s.aboveAt > win) s.phase = "idle";
    return null;
  }
  const kb = knife.y + knife.h; // 包丁の下端
  const rel = (kb - veg.y) / veg.h; // 0 = 野菜の上端, 1 = 下端
  const overlapX = Math.min(veg.x + veg.w, knife.x + knife.w) - Math.max(veg.x, knife.x) > 0;
  const depth = +$("depth").value;

  if (rel < 0) {
    if (s.phase !== "above") { s.phase = "above"; s.aboveAt = now; }
    else s.aboveAt = Math.max(s.aboveAt, now - win); // 上にいる間は常に「直近」扱い
  } else if (s.phase === "above" && overlapX && rel >= depth && now - s.aboveAt <= win) {
    s.phase = "cut";
    fireCut(`${veg.cls ? veg.cls + "を" : ""}振り下ろし (深さ ${rel.toFixed(2)}, ${Math.round(now - s.aboveAt)}ms)`);
  } else if (s.phase === "above" && now - s.aboveAt > win) {
    s.phase = "idle";
  }
  return rel;
}

// 分離判定：野菜の塊が「1つで安定」→「2つ以上で安定」に変わる
function judgeSplit(vegBoxes) {
  const sp = state.split;
  if (!$("useSplit").checked || $("vegSrc").value !== "color") { sp.wasSingle = false; sp.singleFrames = sp.multiFrames = 0; return; }
  const n = vegBoxes.length;
  if (n === 1) { sp.singleFrames++; sp.multiFrames = 0; if (sp.singleFrames >= 8) sp.wasSingle = true; }
  else if (n >= 2) {
    sp.multiFrames++; sp.singleFrames = 0;
    // 2番目の塊が小さすぎる（ノイズ）なら無視
    const second = vegBoxes[1].area, first = vegBoxes[0].area;
    if (sp.wasSingle && sp.multiFrames >= 5 && second > first * 0.15) {
      sp.wasSingle = false;
      fireCut(`分離 (${n}個に分かれた)`);
    }
  } else { sp.singleFrames = 0; sp.multiFrames = 0; }
}

function fireCut(reason) {
  state.count++;
  $("count").textContent = state.count;
  log(`CUT! ${reason}`);
  const f = $("flash");
  f.classList.add("show");
  setTimeout(() => f.classList.remove("show"), 120);
  try {
    const ac = fireCut.ac || (fireCut.ac = new (window.AudioContext || window.webkitAudioContext)());
    const o = ac.createOscillator(), g = ac.createGain();
    o.frequency.value = 880; o.connect(g); g.connect(ac.destination);
    g.gain.setValueAtTime(0.2, ac.currentTime);
    g.gain.exponentialRampToValueAtTime(0.001, ac.currentTime + 0.15);
    o.start(); o.stop(ac.currentTime + 0.15);
  } catch (_) {}
}

// ---------- 描画 ----------
function draw(vegBoxes, knifeBoxes, candidates, mask, veg, knife) {
  const W = overlay.width, H = overlay.height;
  const mirror = $("mirror").checked;
  octx.setTransform(1, 0, 0, 1, 0, 0);
  octx.clearRect(0, 0, W, H);
  if (mirror) octx.setTransform(-1, 0, 0, 1, W, 0);

  if (mask) {
    const img = octx.createImageData(work.width, work.height);
    for (let i = 0; i < mask.length; i++) {
      if (!mask[i]) continue;
      const p = i * 4;
      if (mask[i] === 1) { img.data[p] = 60; img.data[p + 1] = 220; img.data[p + 2] = 90; }
      else if (mask[i] === 2) { img.data[p] = 255; img.data[p + 1] = 106; img.data[p + 2] = 60; }
      else { img.data[p] = 90; img.data[p + 1] = 150; img.data[p + 2] = 255; }
      img.data[p + 3] = 110;
    }
    const tmp = draw.tmp || (draw.tmp = document.createElement("canvas"));
    tmp.width = work.width; tmp.height = work.height;
    tmp.getContext("2d").putImageData(img, 0, 0);
    octx.imageSmoothingEnabled = false;
    octx.drawImage(tmp, 0, 0, W, H);
  }

  const box = (b, color, label, lw) => {
    octx.strokeStyle = color; octx.lineWidth = lw;
    octx.strokeRect(b.x * W, b.y * H, b.w * W, b.h * H);
    if (label) {
      octx.font = "bold 16px system-ui";
      const tw = octx.measureText(label).width + 8;
      octx.fillStyle = color;
      octx.fillRect(b.x * W, b.y * H - 20, tw, 20);
      octx.fillStyle = "#000";
      // ミラー時も文字が読めるように一旦戻す
      if (mirror) {
        octx.save(); octx.setTransform(1, 0, 0, 1, 0, 0);
        octx.fillText(label, W - (b.x * W + tw) + 4, b.y * H - 5);
        octx.restore();
      } else octx.fillText(label, b.x * W + 4, b.y * H - 5);
    }
  };

  if ($("showAll").checked)
    for (const b of candidates) box(b, "rgba(255,255,255,.55)", b.cls ? `${b.cls} ${(b.score * 100) | 0}%` : "?", 1);
  vegBoxes.forEach((b, i) => box(b, "#3cdc5a", i === 0 ? "野菜" : "", i === 0 ? 3 : 2));
  knifeBoxes.forEach((b, i) => box(b, "#ff6a3c", i === 0 ? "包丁" : "", i === 0 ? 3 : 2));

  // 深さのしきい値ライン
  if (veg && knife && $("useSwing").checked) {
    const y = (veg.y + veg.h * +$("depth").value) * H;
    octx.strokeStyle = "#ffd23c"; octx.setLineDash([6, 6]); octx.lineWidth = 2;
    octx.beginPath(); octx.moveTo(veg.x * W, y); octx.lineTo((veg.x + veg.w) * W, y); octx.stroke();
    octx.setLineDash([]);
    const kb = (knife.y + knife.h) * H;
    octx.fillStyle = "#ff6a3c";
    octx.beginPath(); octx.arc((knife.x + knife.w / 2) * W, kb, 6, 0, Math.PI * 2); octx.fill();
  }
}

// ---------- 統計 ----------
function updateStats(now, dt, hasVeg, hasKnife, rel) {
  state.frames.push({ t: now, veg: hasVeg, knife: hasKnife });
  while (state.frames.length > 60) state.frames.shift();
  const n = state.frames.length;
  const pct = (k) => Math.round((100 * state.frames.filter((f) => f[k]).length) / n) + "%";
  $("stFps").textContent = (1000 / dt).toFixed(1);
  $("stVeg").textContent = pct("veg");
  $("stKnife").textContent = pct("knife");
  $("stRel").textContent = rel == null ? "-" : rel.toFixed(2);
  const ph = state.swing.phase;
  $("stState").textContent = ph === "above" ? "包丁が野菜の上（待機中）" : ph === "cut" ? "切った → 包丁を上げると再判定" : "待機";
}

navigator.mediaDevices?.enumerateDevices && listCameras().catch(() => {});
