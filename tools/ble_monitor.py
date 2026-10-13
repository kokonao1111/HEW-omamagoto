"""BLE のセンサーデバイスを探して全部につなぎ、届いたイベントの JSON を表示する（要件定義書 6.6）。
サーバーを作る前に、デバイス側の BLE だけを確かめるための道具。Mac と Windows で動く。

    uv run --with bleak tools/ble_monitor.py
    uv run --with bleak tools/ble_monitor.py --out 記録.jsonl --seconds 300

- 切断・再接続を時刻付きで出す（再接続にかかった時間が分かる）
- 連番 seq が飛んだら知らせる（BLE で取りこぼした件数が分かる）
- macOS では初回にターミナルへの Bluetooth の許可を求められる
"""
import argparse
import asyncio
import json
import time

from bleak import BleakClient, BleakScanner

SERVICE_UUID = "35f64d1a-5af0-4ec7-a321-3b2a485eae3f"
EVENT_UUID = "2a28760f-2c0e-4e00-b5cd-4ae7faa984cb"


def now():
    t = time.time()
    return time.strftime("%H:%M:%S", time.localtime(t)) + f".{int(t * 1000) % 1000:03d}"


class Monitor:
    def __init__(self, out):
        self.out = out
        self.tasks = {}  # address -> 接続を見張るタスク
        self.last_seq = {}  # device（MAC）-> 最後の seq
        self.received = 0
        self.missing = 0

    def on_line(self, name, line):
        try:
            ev = json.loads(line)
        except json.JSONDecodeError:
            print(f"{now()} {name} JSON ではない行: {line!r}")
            return
        self.received += 1
        device, seq = ev.get("device"), ev.get("seq")
        last = self.last_seq.get(device)
        # デバイスを再起動すると seq は 1 から始まり直す
        if last is not None and seq is not None and seq > last + 1:
            self.missing += seq - last - 1
            print(f"{now()} {name} seq が飛んだ：{last} → {seq}（{seq - last - 1} 件取りこぼし）")
        self.last_seq[device] = seq
        print(f"{now()} {name} {line}")
        if self.out:
            self.out.write(json.dumps({"recv": time.time(), "name": name, **ev}, ensure_ascii=False) + "\n")
            self.out.flush()

    async def watch(self, device, name):
        buf = bytearray()
        gone = asyncio.Event()

        def on_data(_, data):
            buf.extend(data)
            while b"\n" in buf:
                line, _, rest = buf.partition(b"\n")
                buf[:] = rest
                self.on_line(name, line.decode(errors="replace"))

        try:
            async with BleakClient(device, disconnected_callback=lambda _: gone.set()) as client:
                print(f"{now()} {name} 接続")
                await client.start_notify(EVENT_UUID, on_data)
                await gone.wait()
        except Exception as e:  # noqa: BLE001
            print(f"{now()} {name} 接続できない：{e}")
            return
        print(f"{now()} {name} 切断")

    async def run(self, seconds):
        end = time.time() + seconds if seconds else None
        while end is None or time.time() < end:
            found = await BleakScanner.discover(timeout=2.0, service_uuids=[SERVICE_UUID], return_adv=True)
            for address, (device, adv) in found.items():
                task = self.tasks.get(address)
                if task is None or task.done():
                    name = adv.local_name or device.name or address
                    self.tasks[address] = asyncio.create_task(self.watch(device, name))
        for task in self.tasks.values():
            task.cancel()
        print(f"受信 {self.received} 件、取りこぼし {self.missing} 件")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", help="受信したイベントを JSONL で保存する")
    ap.add_argument("--seconds", type=float, default=0, help="この秒数で終わる（0 なら Ctrl+C まで）")
    args = ap.parse_args()
    out = open(args.out, "a", encoding="utf-8") if args.out else None
    try:
        asyncio.run(Monitor(out).run(args.seconds))
    except KeyboardInterrupt:
        pass
    finally:
        if out:
            out.close()


if __name__ == "__main__":
    main()
