"""センサーデバイス（M5StickS3）のシリアル出力をファイルに記録する。
起動を待ってから生データモード（r）に切り替え、イベントの JSON と raw 行を両方残す。
記録したファイルは device/host_test/replay で判定にもう一度通せる。

    uv run --with pyserial tools/device_record.py 記録.txt --seconds 240
    uv run --with pyserial tools/device_record.py 記録.txt --no-raw      # イベントだけ
"""
import argparse
import time

import serial
from serial.tools import list_ports

ESPRESSIF_VID = 0x303A


def open_port():
    # 挿し直すとポート名が変わる（usbmodem2101 → usbmodem101 など）ので、毎回 Espressif の USB を探す
    while True:
        for p in list_ports.comports():
            if p.vid != ESPRESSIF_VID:
                continue
            try:
                return serial.Serial(p.device, 115200, timeout=0.2)
            except serial.SerialException:
                pass
        time.sleep(0.5)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--seconds", type=float, default=240, help="raw 行が来はじめてから記録する秒数")
    ap.add_argument("--no-raw", action="store_true", help="生データモードにしない")
    args = ap.parse_args()

    port = open_port()
    print("ポート:", port.port)
    raw_on = args.no_raw
    last_r = 0.0
    start = time.time() if args.no_raw else None
    with open(args.out, "w") as f:
        while start is None or time.time() - start < args.seconds:
            try:
                line = port.readline()
            except serial.SerialException:
                port.close()
                time.sleep(1)
                port = open_port()
                raw_on = args.no_raw
                continue
            text = line.decode(errors="replace").strip()
            if text.startswith("raw,") and start is None:
                raw_on = True
                start = time.time()
                print("記録開始")
            # 起動前に送った r は届かないので、raw 行が来るまで 1 秒ごとに送る
            if not raw_on and time.time() - last_r > 1.0:
                port.write(b"r")
                last_r = time.time()
            if text:
                f.write(text + "\n")
                if not text.startswith("raw,"):
                    print(text)
    print("保存:", args.out)


if __name__ == "__main__":
    main()
