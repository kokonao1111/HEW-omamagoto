"""サーバーへの送信。WebSocket で JSON を送り、切れたら自動で再接続する。
URL を指定しなければコンソールに出すだけ（サーバー無しで開発できる）。"""
import json
import queue
import threading
import time


class Sender:
    def __init__(self, url=None, device="camera"):
        self.url = url
        self.device = device
        self.seq = 0
        self.q = queue.Queue(maxsize=1000)
        self.connected = False
        if url:
            threading.Thread(target=self._loop, daemon=True).start()

    def send(self, event: dict):
        self.seq += 1
        msg = {"device": self.device, "ts": int(time.time() * 1000), "seq": self.seq, **event}
        if not self.url:
            if event.get("event") != "position":  # 位置は多いので画面にだけ出す
                print(json.dumps(msg, ensure_ascii=False))
            return
        try:
            self.q.put_nowait(json.dumps(msg, ensure_ascii=False))
        except queue.Full:
            pass

    def _loop(self):
        from websockets.sync.client import connect

        while True:
            try:
                with connect(self.url, open_timeout=3) as ws:
                    self.connected = True
                    print("サーバーに接続:", self.url)
                    while True:
                        ws.send(self.q.get())
            except Exception as e:  # noqa: BLE001
                if self.connected:
                    print("サーバーとの接続が切れました:", e)
                self.connected = False
                time.sleep(3)
