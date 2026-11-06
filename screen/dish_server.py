#!/usr/bin/env python3
"""料理の写真を作る中継サーバー。

画面（screen/index.html）が、皿を斜め横から撮った画像と、料理の状態を書いた指示を送ってくる。
それを画像生成 AI に渡し、本物らしい料理の写真にして返す。既定は OpenAI（GPT の画像モデル）、
--provider gemini で Google の Gemini に切り替えられる。
API キーはこのサーバーだけが持つ（ページに置くと誰でも読めてしまうため）。

  # 本番（OpenAI の API キーが必要）
  OPENAI_API_KEY=sk-... .venv/bin/python screen/dish_server.py

  # Gemini を使うとき
  GEMINI_API_KEY=... .venv/bin/python screen/dish_server.py --provider gemini

  # API を使わずに流れだけ試す（送られた皿の画像を 3 秒後にそのまま返す）
  .venv/bin/python screen/dish_server.py --mock

  # 1 枚だけ作って、速さと仕上がりを確かめる（画面なしで。--image に皿の画像を渡す）
  OPENAI_API_KEY=sk-... .venv/bin/python screen/dish_server.py --test --image 皿.jpg

環境変数
  OPENAI_API_KEY        OpenAI の API キー
  OPENAI_IMAGE_MODEL    使うモデル（既定 gpt-image-2。使えないときは gpt-image-1）
  OPENAI_IMAGE_QUALITY  low / medium / high（既定 medium。low は速くて安い、high は遅くて高い）
  GEMINI_API_KEY        Google AI Studio の API キー（--provider gemini のとき）
  GEMINI_IMAGE_MODEL    使うモデル（既定 gemini-2.5-flash-image）
  DISH_MAX              1 日に作る枚数の上限（既定 400）。超えたらエラーを返し、画面は用意した写真を使う

作った写真は screen/generated/ に保存する（展示のあとで見返す用。git には入れない）。
"""
import argparse
import base64
import datetime
import json
import os
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE / "generated"
TYPES = {".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".jpg": "image/jpeg",
         ".png": "image/png", ".md": "text/plain; charset=utf-8", ".hdr": "application/octet-stream",
         ".gltf": "model/gltf+json", ".bin": "application/octet-stream"}
OPENAI_MODEL = os.environ.get("OPENAI_IMAGE_MODEL", "gpt-image-2")
OPENAI_QUALITY = os.environ.get("OPENAI_IMAGE_QUALITY", "medium")
OPENAI_API = "https://api.openai.com/v1/images/edits"
GEMINI_MODEL = os.environ.get("GEMINI_IMAGE_MODEL", "gemini-2.5-flash-image")
GEMINI_API = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
MAX_PER_DAY = int(os.environ.get("DISH_MAX", "400"))

lock = threading.Lock()
dropped = set()          # モデルに断られた項目（次からは最初から外す）
count = {"day": "", "n": 0}


def split_data_url(url):
    head, b64 = url.split(",", 1)
    mime = head.split(":", 1)[1].split(";", 1)[0] if ":" in head else "image/jpeg"
    return mime, b64


def http_error_text(e):
    try:
        return e.read().decode("utf-8", "replace")[:400]
    except Exception:
        return ""


# ---------------- OpenAI（GPT の画像モデル） ----------------
def explain(code, msg):
    """よくあるエラーの意味（日本語）"""
    m = msg.lower()
    if code == 401:
        return "API キーが正しくない。sk-proj- で始まるキーを丸ごと入れる"
    if "verif" in m:
        return "組織の本人確認が必要。platform.openai.com の 設定 → 組織 → 一般 から確認する（終わってから 15 分ほどで使える）"
    if code == 429 and ("quota" in m or "billing" in m or "credit" in m):
        return "お金が入っていない。設定 → 支払い（Billing）でクレジットを入れる"
    if code == 429:
        return "短い時間に頼みすぎ。少し待ってやり直す"
    if code == 403:
        return "このキーに画像を作る権限がない。キーの権限を「全て」にするか、画像（Images）を「書き込み」にする"
    if code == 404 or "model" in m:
        return "モデルが見つからない。OPENAI_IMAGE_MODEL=gpt-image-1 を付けて試す"
    return "上の英文のエラーを見る"


def multipart(fields, files):
    """multipart/form-data の本体を作る。files = [(名前, ファイル名, MIME, バイト列)]"""
    b = uuid.uuid4().hex
    out = bytearray()
    for k, v in fields.items():
        out += f'--{b}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode()
    for name, fname, mime, data in files:
        out += f'--{b}\r\nContent-Disposition: form-data; name="{name}"; filename="{fname}"\r\nContent-Type: {mime}\r\n\r\n'.encode()
        out += data + b"\r\n"
    out += f"--{b}--\r\n".encode()
    return bytes(out), f"multipart/form-data; boundary={b}"


def call_openai(prompt, image_url, key):
    mime, b64 = split_data_url(image_url)
    img = base64.b64decode(b64)
    ext = "png" if "png" in mime else "jpg"
    fields = {
        "model": OPENAI_MODEL,
        "prompt": prompt,
        "size": "1536x1024",            # 横長（画面の札に出す）
        "quality": OPENAI_QUALITY,
        "output_format": "jpeg",
        "output_compression": "85",
        "input_fidelity": "high",       # 元の画像（皿の形・ピースの置き方）をなるべく残す
        "n": "1",
    }
    # モデルによって使えない項目があるので、断られたら減らしてやり直す（覚えておいて、次からは送らない）
    optional = ["input_fidelity", "output_compression", "output_format", "quality"]
    for k in list(dropped):
        fields.pop(k, None)
    while True:
        body, ctype = multipart(fields, [("image", f"plate.{ext}", mime, img)])
        req = urllib.request.Request(OPENAI_API, data=body,
                                     headers={"Content-Type": ctype, "Authorization": f"Bearer {key}"})
        try:
            with urllib.request.urlopen(req, timeout=90) as r:
                res = json.load(r)
            break
        except urllib.error.HTTPError as e:
            msg = http_error_text(e)
            drop = next((k for k in optional if k in fields and k in msg), None)
            if e.code == 400 and drop:
                print(f"  {drop} は使えないので外してやり直す（次からは送らない）", flush=True)
                fields.pop(drop)
                dropped.add(drop)
                continue
            if fields["model"] != "gpt-image-1" and e.code in (400, 403, 404) and "model" in msg.lower() and "verif" not in msg.lower():
                print(f"  {fields['model']} が使えないので gpt-image-1 でやり直す", flush=True)
                fields["model"] = "gpt-image-1"
                continue
            raise RuntimeError(f"OpenAI {e.code}: {msg}\n→ {explain(e.code, msg)}") from None
    data = (res.get("data") or [{}])[0]
    if data.get("b64_json"):
        m = "image/png" if fields.get("output_format", "png") == "png" else "image/jpeg"
        return f"data:{m};base64,{data['b64_json']}"
    if data.get("url"):
        with urllib.request.urlopen(data["url"], timeout=30) as r:
            return "data:image/png;base64," + base64.b64encode(r.read()).decode()
    raise RuntimeError("画像が返ってこなかった: " + json.dumps(res)[:300])


# ---------------- Google（Gemini の画像モデル） ----------------
def call_gemini(prompt, image_url, key):
    mime, b64 = split_data_url(image_url)
    body = {
        "contents": [{"parts": [{"text": prompt}, {"inline_data": {"mime_type": mime, "data": b64}}]}],
        "generationConfig": {"responseModalities": ["TEXT", "IMAGE"], "imageConfig": {"aspectRatio": "4:3"}},
    }

    def post(b):
        req = urllib.request.Request(GEMINI_API.format(model=GEMINI_MODEL), data=json.dumps(b).encode(),
                                     headers={"Content-Type": "application/json", "x-goog-api-key": key})
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.load(r)

    try:
        res = post(body)
    except urllib.error.HTTPError as e:
        if e.code != 400:
            raise RuntimeError(f"Gemini {e.code}: {http_error_text(e)}") from None
        body["generationConfig"].pop("imageConfig")       # 縦横比の指定に対応していないモデルのとき
        res = post(body)
    for cand in res.get("candidates", []):
        for part in cand.get("content", {}).get("parts", []):
            data = part.get("inlineData") or part.get("inline_data")
            if data and data.get("data"):
                m = data.get("mimeType") or data.get("mime_type") or "image/png"
                return f"data:{m};base64,{data['data']}"
    raise RuntimeError("画像が返ってこなかった: " + json.dumps(res)[:300])


PROVIDERS = {
    "openai": (call_openai, "OPENAI_API_KEY", lambda: f"OpenAI {OPENAI_MODEL}（{OPENAI_QUALITY}）"),
    "gemini": (call_gemini, "GEMINI_API_KEY", lambda: f"Gemini {GEMINI_MODEL}"),
}


def save(url, prompt):
    OUT.mkdir(exist_ok=True)
    mime, b64 = split_data_url(url)
    stamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    ext = "png" if "png" in mime else "jpg"
    (OUT / f"{stamp}.{ext}").write_bytes(base64.b64decode(b64))
    (OUT / f"{stamp}.txt").write_text(prompt, encoding="utf-8")
    return OUT / f"{stamp}.{ext}"


class Handler(BaseHTTPRequestHandler):
    mock = False
    provider = "openai"

    def cors(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Access-Control-Allow-Private-Network", "true")   # file:// や別の PC のページから呼ぶとき

    def reply(self, code, obj):
        data = json.dumps(obj).encode()
        self.send_response(code)
        self.cors()
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        """画面のページ（index.html）と、その写真を配る。同じサーバーから開けば、ブラウザに止められずに /dish を呼べる"""
        path = self.path.split("?", 1)[0]
        if path in ("", "/"):
            path = "/index.html"
        f = (HERE / path.lstrip("/")).resolve()
        if HERE not in f.parents or not f.is_file() or f.suffix not in TYPES or "_raw" in f.name:
            return self.reply(404, {"error": "not found"})
        data = f.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", TYPES[f.suffix])
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def do_OPTIONS(self):
        self.send_response(204)
        self.cors()
        self.end_headers()

    def do_POST(self):
        if self.path.rstrip("/") != "/dish":
            return self.reply(404, {"error": "not found"})
        try:
            req = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
            prompt, image = req["prompt"], req["image"]
        except Exception:
            return self.reply(400, {"error": "prompt と image が要る"})
        today = datetime.date.today().isoformat()
        with lock:
            if count["day"] != today:
                count.update(day=today, n=0)
            if count["n"] >= MAX_PER_DAY:
                return self.reply(429, {"error": f"今日の上限 {MAX_PER_DAY} 枚に達した"})
            count["n"] += 1
            n = count["n"]
        t0 = time.time()
        try:
            if self.mock:
                time.sleep(3)
                out = image
            else:
                fn, env, _ = PROVIDERS[self.provider]
                out = fn(prompt, image, os.environ[env])
            save(out, prompt)
        except Exception as e:
            print(f"[{n}] 失敗（{time.time() - t0:.1f} 秒）: {e}", file=sys.stderr)
            return self.reply(502, {"error": str(e)[:300]})
        print(f"[{n}] 作った（{time.time() - t0:.1f} 秒）")
        self.reply(200, {"image": out})

    def log_message(self, *args):
        pass


TEST_PROMPT = ("Turn this 3D toy render into a realistic food photograph of the same dish. Keep the same camera angle and framing, "
               "the same white round plate and the same carrot pieces with the same shapes, sizes and positions. "
               "Doneness: perfectly sauteed in butter, glossy glaze, golden caramelized edges. Salt: lightly salted. "
               "Black pepper: a few specks. Real food texture, on a light stone kitchen counter, soft natural daylight, no text.")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=9001)
    ap.add_argument("--provider", choices=list(PROVIDERS), default="openai")
    ap.add_argument("--mock", action="store_true", help="API を使わず、送られた画像をそのまま返す")
    ap.add_argument("--test", action="store_true", help="サーバーは立てず、--image の皿の画像で 1 枚だけ作る")
    ap.add_argument("--image", help="--test で使う皿の画像（jpg / png）")
    a = ap.parse_args()
    fn, env, label = PROVIDERS[a.provider]
    if not a.mock and not os.environ.get(env):
        sys.exit(f"{env} がない。試すだけなら --mock を付ける")
    if a.test:
        if not a.image:
            sys.exit("--test には --image で皿の画像を渡す")
        data = Path(a.image).read_bytes()
        mime = "image/png" if a.image.lower().endswith(".png") else "image/jpeg"
        log = Path(__file__).resolve().parent / "dish_test.log"
        key = os.environ[env]
        print(f"{label()} に送った。キーの頭 {key[:8]}…（1 分くらいかかることがある）", flush=True)
        t0 = time.time()
        try:
            out = fn(TEST_PROMPT, f"data:{mime};base64," + base64.b64encode(data).decode(), key)
            res = f"できた: {label()}: {time.time() - t0:.1f} 秒 → {save(out, TEST_PROMPT)}"
        except Exception as e:
            res = f"失敗（{time.time() - t0:.1f} 秒）: {e}"
        stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        with log.open("a", encoding="utf-8") as f:
            f.write(f"[{stamp}] キーの頭 {key[:8]}… {res}\n")
        print(res)
        return
    Handler.mock = a.mock
    Handler.provider = a.provider
    print(f"料理の写真サーバー（{'お試し' if a.mock else label()}、1 日 {MAX_PER_DAY} 枚まで）")
    print(f"  画面はここを Chrome で開く → http://localhost:{a.port}/?gen=1", flush=True)
    ThreadingHTTPServer(("0.0.0.0", a.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
