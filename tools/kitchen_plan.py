#!/usr/bin/env python3
"""おままごとキッチン（机の上に置く卓上型）の設計図を作る。

出力:
  docs/img/kitchen_plan.png       全体図（平面・正面・側面・背面）＋カメラから見える範囲
  docs/img/kitchen_parts.png      部材ごとの寸法と穴の位置
  docs/img/kitchen_cutlayout.png  板取り図（910×1820 の合板のどこから何を切るか。自動で並べる）
  docs/kitchen_3d.html            組み立ての 3D（ブラウザで開く。tools/kitchen_3d_template.html に数字を入れて作る）
  docs/キッチン設計書.md の自動の部分（主な寸法・部材表・カメラの見え方）

数字は下の P を書き換えて再実行するだけで、図と表がすべて描き直される。単位はすべて mm。

  .venv/bin/python tools/kitchen_plan.py

座標: x = 本体の左端から右へ / y = 本体の手前の縁から奥へ / z = キッチンの天板の上面から上へ

モニターは背板の裏から窓にはめる。背板は窓のまわりの 4 枚（下・上・左・右）を四角につなぎ、
窓の左右に立てる「縦の受け」と左右の袖板で 4 枚をつなぐ。
土台は底板⑪の上に前板・側板・中仕切り・背板 下を立てた箱。中（底板の上）に基板と配線を入れる。
"""
import json
import math
import os
import re
import textwrap
from dataclasses import dataclass, field
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib import font_manager
from matplotlib.patches import Circle, Rectangle

ROOT = Path(__file__).resolve().parent.parent
IMG = ROOT / "docs" / "img"
DOC = ROOT / "docs" / "キッチン設計書.md"

T = 15               # 合板の厚さ（シナ合板 15mm）
KERF = 4             # のこ刃の幅（切りしろ）
SHEET = (1820, 910)  # 合板 1 枚の大きさ（サブロク）
PLY_KG_M3 = 550      # シナ合板の比重（重さの目安用）
MIN_CUT_W = 50       # これより細い板は店で切ってもらえないことが多い

# ============================== パラメータ（mm） ==============================
P = {
    # 置く机（会場の机。実物を測って書き換える）
    "desk_w": 1200, "desk_d": 600, "desk_h": 700, "desk_t": 25,
    # 本体
    "W": 1050,          # 幅
    "D": 550,           # 奥行き（背板まで。モニターは背板の後ろに出る）
    "base_h": 150,      # 土台の高さ（机の上面 → 天板の上面）。底板・天板の間につまみ・配線・基板が入る
    "back_h": 620,      # 背板の高さ（天板の上面 → 背板の上端）
    "wing_d": 150,      # 袖板の奥行き
    "part_x": 580,      # 中仕切りの位置（本体の左端 → 中仕切りの左の面）
    # モニター 27 インチ（縁の細いモデルの目安。買ったら実測して書き換える）
    "mon_w": 613, "mon_h": 364,                  # 本体の幅・高さ
    "mon_t": 45,                                 # 本体のいちばん厚い所
    "view_w": 598, "view_h": 337,                # 表示部分の幅・高さ
    "bezel_bottom": 20,                          # 本体の下端 → 表示部分の下端（下の縁の太さ）
    "view_z": 170,      # 天板の上面 → 画面（表示部分）の下端
    "win_margin": 2,    # 窓を表示部分より各辺これだけ大きくする
    "gap": 3,           # モニターと受けの板のすき間
    "fin_below": 80,    # 縦の受けを窓の下の縁からどれだけ下まで延ばすか（背板の継ぎ目をまたぐ）
    "fin_above": 60,    # 縦の受けを窓の上の縁からどれだけ上まで延ばすか
    "bar_w": 60,        # 押さえ桟の幅
    # まな板（おもちゃ。天板に置く）
    "board_x": 90, "board_y": 90, "board_w": 300, "board_d": 220, "board_t": 20,
    # 皿を置く所（料理を出す）
    "plate_x": 410, "plate_y": 100, "plate_w": 160, "plate_d": 160,
    # コンロ（作り付け。天板の上に合板を 1 枚重ねる。2 口）
    "stove_x": 600, "stove_y": 60, "stove_w": 400, "stove_d": 300,
    "burner_x": (100, 300),   # コンロ板の左端 → 各バーナーの中心
    "burner_r": 60,
    "pan_on": 1,              # フライパンをのせるバーナー（0 = 左, 1 = 右）
    "pan_r": 90,              # フライパン（おもちゃ）の半径
    "pan_z": 25,              # コンロ板の上面 → フライパンの底
    # 電子レンジ（おもちゃを置く）
    "mw_x": 30, "mw_y": 320, "mw_w": 280, "mw_d": 200, "mw_h": 230,
    # 調味料の置き場
    "spice_x": 400, "spice_y": 330, "spice_w": 180, "spice_d": 110, "spice_h": 130,
    # カメラ
    "fov_center": 78,   # 中央カメラ（Anker C200）の画角
    # 中央カメラ: カメラに付いているクリップで背板①の上の縁に挟む
    "cam_front": 25,    # レンズが背板の前の面からどれだけ前に出るか
    "cam_up": 35,       # レンズが背板の上端からどれだけ上に来るか
    # 左右のカメラ: スマホアームスタンドの先。本体の外側に side_cam_out だけ出す
    "side_cam_y": 240, "side_cam_z": 600, "side_cam_out": 60, "fov_side": 58,
    # アーム台⑫: 袖板の外側、天板の高さに横向きに付ける。アームのクランプでこの台の後ろの縁を挟む
    "mount_w": 90, "mount_d": 150,
    # 来場者（大人）の目の高さ（床から）。目の高さ ≒ 身長 − 110
    "eye_low": 1440,    # 身長 155cm
    "eye_high": 1690,   # 身長 180cm
    "eye_y": -150,      # 目の位置（本体の手前の縁より手前が負）
}
# =============================================================================

WOOD, MINT, EDGE, DIM, BLUE = "#d9b98a", "#a8c8b8", "#333", "#c0392b", "#3b82f6"
DESK, LEG, FRAME, PLY = "#eeeeee", "#cccccc", "#b08a5a", "#e6cfa5"
MON = "#1a1a1a"
VIS_COLORS = {0: "#e74c3c", 1: "#f39c12", 2: "#27ae60"}  # 見えるカメラの台数ごとの色


# ============================== 位置の計算 ==============================
def board_center():
    return np.array([P["board_x"] + P["board_w"] / 2, P["board_y"] + P["board_d"] / 2, P["board_t"]], float)


def pan_center():
    return np.array([P["stove_x"] + P["burner_x"][P["pan_on"]], P["stove_y"] + P["stove_d"] / 2, T + P["pan_z"]], float)


def counter_h():
    """床からキッチンの天板の上面まで"""
    return P["desk_h"] + P["base_h"]


def layout():
    """P から決まる位置をまとめて計算する"""
    W, D, m, g = P["W"], P["D"], P["win_margin"], P["gap"]
    # wall_h = 前板・側板・中仕切りの高さ（底板の上面 → 天板の下面）
    L = {"back_front": D - T, "wall_h": P["base_h"] - 2 * T, "inner_d": D - 2 * T}
    # 基板（ESP32 開発ボード）を置く所の目安（左端から・手前から・幅・奥行き）。土台の右の部屋の底板の上。
    # 左のつまみの真後ろに置くと、左のつまみの線がまっすぐ届く
    L["esp"] = (P["stove_x"] + P["burner_x"][0] - 10, 380, 55, 28)
    # 上の枠（背板＋袖板）と下の台のつなぎ目。木口に金具を打たず、角材の溝に差し込んでパチン錠で留める
    #   溝: 天板の上の背板のすぐ前に角材（袖板の間いっぱい）、土台の後ろ板⑬の裏に天板より guide_h 高く出る角材を 3 つ
    L["guide"] = (15, 20)                                          # 角材の 厚さ・高さ（天板から出る分）
    L["back_blocks"] = [(80, 230), (450, 600), (W - 230, W - 80)]   # 後ろの角材（左右の範囲）
    L["latch_x"] = (340, W - 330)                                  # パチン錠（背板の裏 ↔ 後ろ板⑬の裏。どちらも同じ面）
    L["seam_x"] = P["part_x"] + T                                  # 底板を 2 枚に分ける所（中仕切りの右の面）
    # 窓（画面の枠）。電子レンジの右から右の袖板までの間の真ん中に置く
    L["win_w"] = P["view_w"] + 2 * m
    L["win_h"] = P["view_h"] + 2 * m
    lo, hi = P["mw_x"] + P["mw_w"] + 20, W - T - 20
    L["win_x"] = round((lo + hi - L["win_w"]) / 2)
    L["win_z0"] = P["view_z"] - m
    L["win_z1"] = L["win_z0"] + L["win_h"]
    L["view_x"] = L["win_x"] + m
    # モニター本体（背板の裏。表示部分が窓の真ん中に来る）
    L["mon_x"] = L["view_x"] - (P["mon_w"] - P["view_w"]) / 2
    L["mon_z"] = P["view_z"] - P["bezel_bottom"]
    # 裏の受け。押さえ桟は縦の受けの間の後ろの端にはさむ（モニターの背面との間は gap）
    L["fin_d"] = P["mon_t"] + g + T                     # 縦の受け・下の受け木が背板から後ろに出る長さ
    L["fin_xl"] = L["mon_x"] - g - T
    L["fin_xr"] = L["mon_x"] + P["mon_w"] + g
    L["fin_z0"] = min(L["win_z0"] - P["fin_below"], L["mon_z"] - T)
    L["fin_z1"] = min(L["win_z1"] + P["fin_above"], P["back_h"])
    L["bar_z"] = L["mon_z"] + P["mon_h"] / 2 - P["bar_w"] / 2
    L["bar_len"] = P["mon_w"] + 2 * g
    L["depth_total"] = D + L["fin_d"]
    # 中央カメラ: まな板とフライパンの真ん中の上。レンズを背板より前に出す
    bc, pc = board_center(), pan_center()
    L["cam_x"] = round((bc[0] + pc[0]) / 2)
    L["cam_y"] = L["back_front"] - P["cam_front"]
    L["cam_z"] = P["back_h"] + P["cam_up"]
    return L


CAM_BODY = {"中央": (70, 44, 40), "左": (73, 32, 28), "右": (73, 32, 28)}   # カメラ本体の 幅・高さ・奥行き（目安）
# 左右のカメラ Brio 100。仕様は 幅 72.9・高さ 32・奥行き 66.7（奥行きはクリップ込み）。本体だけの奥行きは約 30
# 左右のカメラは、アームの先のホルダー（6〜10cm）で Brio の左右の端（幅 73）をじかに挟む
# Brio 100 はカプセル形。レンズは正面の端寄り（カメラから見て右）にあり、ケーブルは背中から出る。
# モニターに掛ける足は本体の後ろ下の付け根から下へ伸び、先の板が後ろへ開く（背中にぴったり畳むことはできない）
BRIO_LENS_OFF = 18     # レンズ → 本体の中心（カメラから見て左へ）
BRIO_FOOT = (46, 62, 6, 45)   # 足の先の板の 幅・長さ・厚さ（目安）と、真下から後ろへ開く角度
# スマホアームスタンド（クランプ式・2 段アーム。Amazon B09WV17QFJ）。商品写真の寸法（アーム 約 36cm × 2、ホルダー 6〜10cm）から
ARM = {
    "L1": 360, "L2": 360,      # 下の段・上の段（軸から軸）
    "bar": 8, "gap": 24,       # アームの角棒の太さ、2 本の棒の間隔（中心どうし）
    "clamp_up": 12,            # クランプの上面がアーム台の上面より上に出る分
    "post": 30, "pivot": 15,   # 根元の軸の長さ、軸の上 → 下の段の回転軸
    "rod": 35, "head": 30, "stud": 18,   # 先の軸・自由雲台・ねじ
    "rod_down": 30,            # 先の軸がアームの延びる向きから下がる角度（商品写真から）
    "neck_max": 90,            # 自由雲台の首が曲がる限度（切り欠きに倒したとき）
    "holder_t": 12, "holder_h": 30, "jaw": 8,   # ホルダーの背板の厚さ・高さ、爪の厚さ
}


def side_rig(cam, sgn):
    """左右のカメラをアームで付けたときの各部の位置（天板の上面から）。sgn = -1 左 / +1 右。
    カメラ → ホルダー（カメラの左右の端を挟む）→ 自由雲台 → アームの先 → 2 段アーム → 根元（アーム台⑫のクランプ）の順に決める"""
    a, r, u = cam["basis"]                       # カメラの前・右・上
    pos = cam["pos"]
    w, h, d = CAM_BODY["左"]
    body = pos - a * d / 2 - r * BRIO_LENS_OFF   # Brio 本体の中心（pos はレンズ。レンズは右寄り）
    hold = body - a * (d / 2 + ARM["holder_t"] / 2)   # ホルダーの背板（本体の背中に当たる。足は下に垂らす）
    ball = hold - a * (ARM["holder_t"] / 2 + ARM["stud"])
    px = -P["mount_w"] / 2 if sgn < 0 else P["W"] + P["mount_w"] / 2
    py = P["D"] + 15                                            # クランプの本体（アーム台の後ろの縁の外）
    B = np.array([px, py, ARM["clamp_up"] + ARM["post"] + ARM["pivot"]])   # アーム台の上面 = 天板の上面
    zup = np.array([0.0, 0.0, 1.0])
    # アームの先の軸は、アームの面の中で前へ（rod_down だけ下向き）に出る。横には出せない。
    # 軸の向きはアームの面で決まり、アームの面は先の位置で決まるので、くり返して合わせる
    hdir = np.array([ball[0] - B[0], ball[1] - B[1], 0.0])
    hdir /= np.linalg.norm(hdir)
    rd = math.radians(ARM["rod_down"])
    for _ in range(30):
        rod_dir = hdir * math.cos(rd) - zup * math.sin(rd)
        head_end = ball - rod_dir * ARM["head"]
        tip = head_end - rod_dir * ARM["rod"]                   # アームの先の回転軸
        nh = np.array([tip[0] - B[0], tip[1] - B[1], 0.0])
        nh /= np.linalg.norm(nh)
        if np.linalg.norm(nh - hdir) < 1e-6:
            break
        hdir = nh
    neck = math.degrees(math.acos(np.clip(rod_dir @ a, -1, 1)))  # 雲台の首の曲がり（軸の向き → カメラの向き）
    dv = tip - B
    dist = math.hypot(dv @ hdir, dv[2])
    L1, L2 = ARM["L1"], ARM["L2"]
    if dist > L1 + L2 - 5:
        raise SystemExit(f"アームが届かない（根元から先まで {dist:.0f}、アームは {L1 + L2}）")
    th1 = math.atan2(dv[2], dv @ hdir) + math.acos((L1 ** 2 + dist ** 2 - L2 ** 2) / (2 * L1 * dist))
    E = B + L1 * (math.cos(th1) * hdir + math.sin(th1) * zup)
    s2 = (tip - E) / np.linalg.norm(tip - E)
    th2 = math.atan2(s2[2], s2 @ hdir)
    return {"a": a, "r": r, "u": u, "rod_dir": rod_dir, "neck": neck, "body": body, "hold": hold,
            "ball": ball, "head_end": head_end, "tip": tip, "B": B, "E": E, "hdir": hdir, "px": px, "py": py,
            "th1": math.degrees(th1), "th2": math.degrees(th2)}


def cam_mount(lens, aim, body, ball_r=11, stud=8):
    """カメラを雲台（またはアームの先）に留める位置。lens・aim は設計図の座標。
    カメラは下を向くので、ねじ穴のある底面は後ろ（奥）を向く。雲台の玉はその後ろに来る"""
    w, h, d = body
    z = np.asarray(aim, float) - np.asarray(lens, float)
    z /= np.linalg.norm(z)
    x = np.cross([0.0, 0.0, 1.0], z)
    x /= np.linalg.norm(x)
    y = np.cross(z, x)                                   # カメラの「上」
    socket = np.asarray(lens, float) - z * d / 2 - y * h / 2   # 底面の真ん中（1/4 インチのねじ穴）
    ball = socket - y * (stud + ball_r)
    return {"socket": socket, "ball": ball, "ball_r": ball_r}


def center_cam_clip(L):
    """中央カメラ（背板の上の縁にクリップで挟む）。底のねじ穴（クリップの軸）の位置と、下に向ける角度"""
    lens = np.array([L["cam_x"], L["cam_y"], L["cam_z"]], float)
    aim = (board_center() + pan_center()) / 2
    m = cam_mount(lens, aim, CAM_BODY["中央"])
    d = aim - lens
    m["tilt"] = math.degrees(math.atan2(-d[2], math.hypot(d[0], d[1])))   # 水平から下へ何度
    return m


def hidden_top(L, eye):
    """背板の厚みのせいで、画面の上の端が何 mm 隠れるか（目の高さ eye の人から見て）"""
    C = counter_h()
    ez, ey = eye, P["eye_y"]
    edge_z, edge_y = C + L["win_z1"], L["back_front"]
    if ez <= edge_z:
        return 0.0
    slope = (ez - edge_z) / (edge_y - ey)
    z_hit = edge_z - slope * T
    return max(0.0, C + P["view_z"] + P["view_h"] - z_hit)


# ============================== 見え方の計算 ==============================
def occluders(L):
    """カメラの視線をさえぎる物 (x0, y0, z0, x1, y1, z1)。モニターは背板の裏なので入らない"""
    W, D, bh = P["W"], P["D"], P["back_h"]
    wy0 = D - T - P["wing_d"]
    return {
        "背板": (0, D - T, -P["base_h"], W, D, bh),
        "左の袖板": (0, wy0, 0, T, D - T, bh),
        "右の袖板": (W - T, wy0, 0, W, D - T, bh),
        "電子レンジ": (P["mw_x"], P["mw_y"], 0, P["mw_x"] + P["mw_w"], P["mw_y"] + P["mw_d"], P["mw_h"]),
        "調味料": (P["spice_x"], P["spice_y"], 0, P["spice_x"] + P["spice_w"], P["spice_y"] + P["spice_d"], P["spice_h"]),
        "コンロ板": (P["stove_x"], P["stove_y"], 0, P["stove_x"] + P["stove_w"], P["stove_y"] + P["stove_d"], T),
    }


def segment_hits_box(p0, p1, box):
    """線分 p0→p1 が箱を通り抜けるか（端をかすめるだけ・端点で触れるだけは通らない扱い）"""
    d = p1 - p0
    tmin, tmax = 0.0, 1.0
    for i in range(3):
        lo, hi = box[i], box[i + 3]
        if abs(d[i]) < 1e-9:
            if p0[i] <= lo or p0[i] >= hi:
                return False
            continue
        t1, t2 = sorted(((lo - p0[i]) / d[i], (hi - p0[i]) / d[i]))
        tmin, tmax = max(tmin, t1), min(tmax, t2)
        if tmin >= tmax:
            return False
    return tmax - tmin > 1e-3 and tmin < 1 - 1e-3


def cameras(L):
    aims = {"board": board_center(), "pan": pan_center(), "both": (board_center() + pan_center()) / 2}
    out_ = P["side_cam_out"]
    specs = [("中央", L["cam_x"], L["cam_y"], L["cam_z"], P["fov_center"], "both"),
             ("左", -out_, P["side_cam_y"], P["side_cam_z"], P["fov_side"], "board"),
             ("右", P["W"] + out_, P["side_cam_y"], P["side_cam_z"], P["fov_side"], "pan")]
    out = []
    for name, x, y, z, fov, aim in specs:
        pos = np.array([x, y, z], float)
        a = aims[aim] - pos
        a /= np.linalg.norm(a)
        r = np.cross(a, [0.0, 0.0, 1.0])
        r = r / np.linalg.norm(r) if np.linalg.norm(r) > 1e-9 else np.array([1.0, 0, 0])
        out.append({"name": name, "pos": pos, "aim": aims[aim], "fov": fov, "basis": (a, r, np.cross(r, a))})
    return out


def in_fov(cam, p):
    """16:9 のカメラの画角（対角）に p が入っているか。画面の横は水平に置く前提"""
    a, r, u = cam["basis"]
    d = p - cam["pos"]
    depth = d @ a
    if depth <= 0:
        return False
    k = math.tan(math.radians(cam["fov"]) / 2) / math.hypot(16, 9)
    return abs(d @ r) / depth <= k * 16 and abs(d @ u) / depth <= k * 9


def zone_points():
    """見えるかを調べる点（まな板の上面とフライパンの底）"""
    bx, by, bw, bd = P["board_x"], P["board_y"], P["board_w"], P["board_d"]
    board = [np.array([x, y, P["board_t"]]) for y in np.linspace(by + bd * 0.04, by + bd * 0.96, 9)
             for x in np.linspace(bx + bw * 0.04, bx + bw * 0.96, 12)]
    c, r = pan_center(), P["pan_r"] * 0.95
    pan = [np.array([x, y, c[2]]) for y in np.linspace(c[1] - r, c[1] + r, 10)
           for x in np.linspace(c[0] - r, c[0] + r, 10) if math.hypot(x - c[0], y - c[1]) <= r]
    return {"まな板": board, "フライパン": pan}


def analyze(L):
    """ゾーンごとに、各点が何台のカメラから見えるかと、カメラごとの見える割合"""
    cams, boxes = cameras(L), occluders(L)
    result = {}
    for zone, pts in zone_points().items():
        counts, per_cam = [], {c["name"]: 0 for c in cams}
        for p in pts:
            n = 0
            for c in cams:
                if in_fov(c, p) and not any(segment_hits_box(c["pos"], p, b) for b in boxes.values()):
                    per_cam[c["name"]] += 1
                    n += 1
            counts.append(n)
        result[zone] = {"pts": pts, "counts": counts, "per_cam": per_cam}
    return cams, result


# ============================== 部材 ==============================
@dataclass
class Hole:
    x: float
    y: float
    d: float
    note: str
    kind: str = "hole"   # hole（貫通）/ cb（裏から座ぐり）/ opt（あとで開けてもよい）/ mark（穴ではなく目印の円）
    cb: float = 0        # 座ぐりの直径


@dataclass
class Part:
    no: str
    name: str
    qty: int
    a: float             # 長さ（図の横）
    b: float             # 幅（図の縦）
    t: float
    mat: str
    where: str           # どこに付くか
    view: str            # 図がどちら向きか
    holes: list = field(default_factory=list)
    marks: list = field(default_factory=list)   # (x, y, w, h, 名前) 他の部材が付く所・物を置く所
    color: str = PLY
    cutouts: list = field(default_factory=list)  # (x, y, w, h, 名前) くり抜く所
    src: str = ""                                # ほかの板のくり抜きから取るとき、その説明（板取りに入れない）


PLYWOOD = "シナ合板 15mm"


def parts(L):
    W, D, bh, base, g = P["W"], P["D"], P["back_h"], P["base_h"], P["gap"]
    wall_h, inner_d, px = L["wall_h"], L["inner_d"], P["part_x"]
    wx, ww, wh, wz0, wz1 = L["win_x"], L["win_w"], L["win_h"], L["win_z0"], L["win_z1"]
    right_w = W - wx - ww
    wing_y0 = D - T - P["wing_d"]
    fxl, fxr, fz0, fz1 = L["fin_xl"], L["fin_xr"], L["fin_z0"], L["fin_z1"]
    ledge_x, ledge_z = L["mon_x"] - g, L["mon_z"] - T

    # ---- 背板（1 枚板。窓をくり抜く）。縦の受け⑦・袖板⑥とつないで「上の枠」にする ----
    back1 = Part("①", "背板", 1, W, bh, T, PLYWOOD,
                 "上の枠。窓をくり抜いた 1 枚板。天板③の後ろの溝に差し込む。上の縁に中央カメラのクリップを挟む",
                 "前から見た図（下 = 天板の上面）")
    back1.cutouts = [(wx, wz0, ww, wh, "窓（くり抜く）")]
    back1.marks = [
        (0, 0, T, bh, ""), (W - T, 0, T, bh, ""),
        (fxl, fz0, T, fz1 - fz0, "⑦（裏）"), (fxr, fz0, T, fz1 - fz0, "⑦（裏）"),
        (ledge_x, ledge_z, P["mon_w"] + 2 * g, T, "⑧（裏）"),
        (L["cam_x"] - 30, bh - 10, 60, 10, "カメラ"),
    ] + [(xl - 16, 0, 32, 45, "錠（裏）") for xl in L["latch_x"]]

    # ---- 土台・天板・袖板 ----
    front = Part("②", "前板", 1, W, wall_h, T, PLYWOOD, "土台のいちばん前。底板⑪の上、天板の下。つまみが付く", "前から見た図")
    front.marks = [(px, 0, T, wall_h, "")]
    front.holes = [Hole(P["stove_x"] + bx, wall_h / 2, 7.5, f"つまみ{'左右'[i]}", kind="cb", cb=25)
                   for i, bx in enumerate(P["burner_x"])]

    top = Part("③", "天板", 1, W, D, T, PLYWOOD, "土台の上。奥の端まであり、後ろの端の溝に上の枠（背板と袖板）を差し込む",
               "上から見た図（下 = 手前）")
    top.marks = [
        (P["stove_x"], P["stove_y"], P["stove_w"], P["stove_d"], "⑨コンロ板"),
        (P["board_x"], P["board_y"], P["board_w"], P["board_d"], "まな板"),
        (P["plate_x"], P["plate_y"], P["plate_w"], P["plate_d"], "皿"),
        (P["mw_x"], P["mw_y"], P["mw_w"], P["mw_d"], "レンジ"),
        (P["spice_x"], P["spice_y"], P["spice_w"], P["spice_d"], "調味料"),
        (0, wing_y0, T, P["wing_d"], ""), (W - T, wing_y0, T, P["wing_d"], ""), (0, D - T, W, T, "①"),
    ]
    top.marks.append((T, D - T - L["guide"][0], W - 2 * T, L["guide"][0], "溝の角材"))
    top.holes = [Hole(P["mw_x"] + P["mw_w"] - 40, P["mw_y"] + 40, 15, "レンジの配線", kind="opt")]

    side = Part("④", "側板", 2, inner_d, wall_h, T, PLYWOOD, "土台の左右の端。底板⑪の上で、前板と後ろ板⑬の間にはさむ",
                "横から見た図（左 = 手前）")
    part = Part("⑤", "中仕切り", 1, inner_d, wall_h, T, PLYWOOD, f"土台の中。左端から {px}。底板⑪の上に立て、天板がたわまないように支える",
                "横から見た図（左 = 手前）")
    part.holes = [Hole(inner_d - 60, wall_h / 2, 20, "レンジの配線", kind="opt")]

    wing = Part("⑥", "袖板", 2, P["wing_d"], bh, T, PLYWOOD,
                "上の枠。背板の左右の端に付け、天板の上の左右の端に立つ。外側に持ち運び用の取っ手を付ける",
                "横から見た図（左 = 手前）")
    wing.marks = [(P["wing_d"] / 2 - 40, 340, 80, 30, "取っ手（外側）")]

    # ---- モニターの受け（背板の裏）----
    fin = Part("⑦", "縦の受け", 2, fz1 - fz0, L["fin_d"], T, PLYWOOD,
               "背板の裏、モニターの左右に立てる。L 金具 3 個で背板の下・左か右・上に留め、背板の継ぎ目をつなぐ",
               "寝かせた図（左 = 下の端、下 = 背板側）")
    fin.marks = [(ledge_z - fz0, 0, T, L["fin_d"], "⑧"),
                 (wz0 - fz0, 0, 2, L["fin_d"], ""), (wz1 - fz0, 0, 2, L["fin_d"], ""),
                 (L["bar_z"] - fz0, L["fin_d"] - T, P["bar_w"], T, "⑩")]
    fin.marks.append((L["bar_z"] - fz0 + P["bar_w"] / 2 - 15, L["fin_d"] - 3, 30, 3, "回転留め（後ろの木口）"))
    ledge = Part("⑧", "下の受け木", 1, P["mon_w"] + 2 * g, L["fin_d"], T, PLYWOOD,
                 "モニターをのせる板。左右の縦の受けの間にはさみ、外側からビスで留める", "上から見た図（下 = 背板側）")

    stove = Part("⑨", "コンロ板", 1, P["stove_w"], P["stove_d"], T, PLYWOOD,
                 "天板の上。天板をのせる前に、天板の裏からビスで留めておく。背板①の窓のくり抜きから取る", "上から見た図（下 = 手前）",
                 src="① の窓")
    stove.holes = [Hole(bx, P["stove_d"] / 2, 2 * P["burner_r"], f"バーナー{'左右'[i]}", kind="mark")
                   for i, bx in enumerate(P["burner_x"])]

    bar = Part("⑩", "押さえ桟", 1, L["bar_len"], P["bar_w"], T, PLYWOOD,
               "モニターの裏。縦の受けの間の後ろの端にはさみ、縦の受けの後ろの木口に付けた回転留めで押さえる（しまうときは回して外す）。"
               "モニターとの間に薄いスポンジを貼る", "後ろから見た図")

    # ---- 底板（いちばん下。土台の箱の底）。合板 1 枚に背板と並ばないので、中仕切りの所で 2 枚に分ける ----
    esx, esy, esw, esd = L["esp"]
    sx_ = L["seam_x"]
    bot_l = Part("⑪a", "底板 左", 1, sx_, D, T, PLYWOOD,
                 "いちばん下の左。前板・側板・中仕切り・後ろ板をのせる。右の縁は中仕切りの右の面にそろえる", "上から見た図（下 = 手前）")
    bot_l.marks = [(0, 0, sx_, T, "②"), (0, D - T, sx_, T, "⑬"), (0, T, T, inner_d, ""), (px, T, T, inner_d, "⑤")]
    bot_r = Part("⑪b", "底板 右", 1, W - sx_, D, T, PLYWOOD,
                 "いちばん下の右。左の縁は、中仕切りの右の面に付けた角材で受ける。基板と配線をこの上に置く", "上から見た図（下 = 手前）")
    bot_r.marks = [(0, 0, W - sx_, T, "②"), (0, D - T, W - sx_, T, "⑬"), (W - sx_ - T, T, T, inner_d, ""),
                   (0, T, 30, inner_d, "角材"), (esx - sx_, esy, esw, esd, "基板")]

    # ---- 左右のカメラのアーム台 ----
    mw, md = P["mount_w"], P["mount_d"]
    mount = Part("⑫", "アーム台", 2, mw, md, T, PLYWOOD,
                 "側板④の外側、上の面を天板にそろえて横向きに付ける（下を L 金具 2 個で側板に留める）。上の枠とはつながない。"
                 "後ろの縁をアームのクランプで挟む",
                 "上から見た図（左の台。下 = 手前、右 = 本体側）", src="① の窓")
    mount.marks = [(mw / 2 - 20, md - 60, 40, 60, "クランプ")]

    # ---- 土台の後ろ板（下の台の後ろの壁）----
    back = Part("⑬", "土台の後ろ板", 1, W, wall_h, T, PLYWOOD,
                "土台のいちばん後ろ。底板⑪の上、天板の下。裏に溝の角材とパチン錠の受けを付ける。基板の USB の穴がある", "前から見た図")
    back.marks = [(0, 0, T, wall_h, ""), (W - T, 0, T, wall_h, ""), (px, 0, T, wall_h, "⑤")] + \
        [(x0, wall_h - 40, x1 - x0, 40, "溝の角材（裏）") for x0, x1 in L["back_blocks"]] + \
        [(xl - 12, wall_h - 40, 24, 25, "錠の受け") for xl in L["latch_x"]]
    back.holes = [Hole(W - 120, wall_h / 2, 30, "基板のUSB")]

    return [back1, front, top, side, part, wing, fin, ledge, stove, bar, bot_l, bot_r, mount, back]


# ============================== 板取り（自動） ==============================
class MaxRects:
    """長方形の詰め込み（MaxRects・短い辺の余りが最小の所に置く）。部材は 90° 回してよい"""

    def __init__(self, w, h):
        self.free = [(0.0, 0.0, float(w), float(h))]

    def find(self, w, h):
        best = None
        for fx, fy, fw, fh in self.free:
            for rw, rh in ((w, h), (h, w)):
                if rw <= fw + 1e-9 and rh <= fh + 1e-9:
                    key = (min(fw - rw, fh - rh), max(fw - rw, fh - rh), fy, fx)
                    if best is None or key < best[0]:
                        best = (key, fx, fy, rw, rh)
        return best

    def place(self, x, y, w, h):
        new = []
        for fx, fy, fw, fh in self.free:
            if x >= fx + fw or x + w <= fx or y >= fy + fh or y + h <= fy:
                new.append((fx, fy, fw, fh))
                continue
            if x > fx:
                new.append((fx, fy, x - fx, fh))
            if x + w < fx + fw:
                new.append((x + w, fy, fx + fw - x - w, fh))
            if y > fy:
                new.append((fx, fy, fw, y - fy))
            if y + h < fy + fh:
                new.append((fx, y + h, fw, fy + fh - y - h))

        def inside(a, b):
            return b[0] <= a[0] and b[1] <= a[1] and a[0] + a[2] <= b[0] + b[2] and a[1] + a[3] <= b[1] + b[3]

        self.free = [a for i, a in enumerate(new)
                     if not any(j != i and inside(a, b) and (a != b or j < i) for j, b in enumerate(new))]


def sheet_layout(ps):
    """合板の部材を 910×1820 の板に自動で並べる。返り値: 板ごとに [(番号, x, y, 横, 縦), ...]"""
    k = KERF
    items = [(p.no, p.a, p.b) for p in ps if p.mat == PLYWOOD and not p.src for _ in range(p.qty)]
    items.sort(key=lambda it: (-max(it[1], it[2]), -it[1] * it[2]))
    bins, sheets = [], []
    for no, w, h in items:
        found = None
        for b, sheet in zip(bins, sheets):
            found = b.find(w + k, h + k)
            if found:
                break
        if found is None:
            b, sheet = MaxRects(SHEET[0] + k, SHEET[1] + k), []
            bins.append(b)
            sheets.append(sheet)
            found = b.find(w + k, h + k)
            if found is None:
                raise SystemExit(f"{no}（{w:.0f}×{h:.0f}）が合板 1 枚より大きい")
        _, x, y, rw, rh = found
        b.place(x, y, rw, rh)
        sheet.append((no, x, y, rw - k, rh - k))
    return sheets


def check_sheets(sheets):
    msgs = []
    sw, sh = SHEET
    for si, sheet in enumerate(sheets, 1):
        for no, x, y, w, h in sheet:
            if x < -0.5 or y < -0.5 or x + w > sw + 0.5 or y + h > sh + 0.5:
                msgs.append(f"板取り: {si}枚目の {no} が板からはみ出す")
        for i, (n1, x1, y1, w1, h1) in enumerate(sheet):
            for n2, x2, y2, w2, h2 in sheet[i + 1:]:
                if x1 < x2 + w2 and x2 < x1 + w1 and y1 < y2 + h2 and y2 < y1 + h1:
                    msgs.append(f"板取り: {si}枚目の {n1} と {n2} が重なる")
    return msgs


def net_area(p):
    """くり抜きを引いた板の面積"""
    return p.a * p.b - sum(w * h for _, _, w, h, _ in p.cutouts)


def weight_kg(ps):
    vol = sum(net_area(p) * p.t * p.qty for p in ps)  # mm³
    return vol * 1e-9 * PLY_KG_M3


# ============================== 描画の部品 ==============================
def setup_font():
    for f in ["/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc", "/System/Library/Fonts/Hiragino Sans GB.ttc",
              "C:/Windows/Fonts/meiryob.ttc", "C:/Windows/Fonts/YuGothB.ttc"]:
        if os.path.exists(f):
            font_manager.fontManager.addfont(f)
            plt.rcParams["font.family"] = font_manager.FontProperties(fname=f).get_name()
            return


def rect(ax, x, y, w, h, fc, label=None, lw=1.5, ls="-", fs=10, z=2, color="black", ec=EDGE):
    ax.add_patch(Rectangle((x, y), w, h, fc=fc, ec=ec, lw=lw, ls=ls, zorder=z))
    if label:
        ax.text(x + w / 2, y + h / 2, label, ha="center", va="center", fontsize=fs, zorder=z + 1, color=color)


def tag(ax, x, y, text, ha="left", va="bottom", fs=9, color="black"):
    ax.text(x, y, text, ha=ha, va=va, fontsize=fs, zorder=12, color=color,
            bbox=dict(boxstyle="round,pad=0.15", fc="white", ec="none", alpha=0.8))


def dim_h(ax, x1, x2, y, text, fs=9):
    ax.annotate("", xy=(x1, y), xytext=(x2, y), arrowprops=dict(arrowstyle="<->", color=DIM, lw=1))
    ax.text((x1 + x2) / 2, y, text, ha="center", va="bottom", fontsize=fs, color=DIM)


def dim_v(ax, y1, y2, x, text, rot=90, fs=9):
    ax.annotate("", xy=(x, y1), xytext=(x, y2), arrowprops=dict(arrowstyle="<->", color=DIM, lw=1))
    ax.text(x + 12, (y1 + y2) / 2, text, ha="left", va="center", fontsize=fs, color=DIM, rotation=rot)


def draw_cam(ax, x, y, label, r=22, where="above"):
    ax.add_patch(Circle((x, y), r, fc="white", ec=EDGE, lw=1.5, zorder=10))
    ax.add_patch(Circle((x, y), r * 0.4, fc="#222", zorder=11))
    off = r + 8
    tx, ty, ha, va = {"above": (x, y + off, "center", "bottom"), "below": (x, y - off, "center", "top"),
                      "left": (x - off, y, "right", "center"), "right": (x + off, y, "left", "center")}[where]
    tag(ax, tx, ty, label, ha=ha, va=va)


def desk_front(ax, x0, width):
    """机を正面/側面から見た形（天板と脚）と床"""
    dh, dt = P["desk_h"], P["desk_t"]
    ax.plot([x0 - 100, x0 + width + 100], [0, 0], color="#555", lw=2)
    rect(ax, x0, dh - dt, width, dt, DESK, z=1)
    for lx in (x0 + 20, x0 + width - 60):
        rect(ax, lx, 0, 40, dh - dt, LEG, z=1)


def draw_wall(ax, L, xmap, C):
    """背板の 4 枚（天板から上）と窓の中の画面。xmap で左右を変換（背面図は左右逆）"""
    W, bh = P["W"], P["back_h"]
    wx, ww, wz0, wz1 = L["win_x"], L["win_w"], L["win_z0"], L["win_z1"]

    def R(x, height, w, h, *args, **kw):
        x0, x1 = sorted((xmap(x), xmap(x + w)))
        rect(ax, x0, C + height, x1 - x0, h, *args, **kw)

    R(0, 0, W, bh, PLY)                       # ①背板（1 枚板。窓は呼ぶ側で描く）
    return R


def side_rigs(cams):
    return [side_rig(c, -1 if c["name"] == "左" else 1) for c in cams if c["name"] in ("左", "右")]


def draw_arm(ax, g, proj, lw=3.0):
    """左右のカメラのアーム（根元 → 関節 → 先 → カメラ）を線で描く。proj = 天板の上面からの 3D の点 → 図の点"""
    pts = [g["B"], g["E"], g["tip"], g["body"]]
    xs, ys = zip(*[proj(p) for p in pts])
    ax.plot(xs, ys, color="#2a2c30", lw=lw, solid_capstyle="round", zorder=7)
    for p in pts[:3]:
        ax.add_patch(Circle(proj(p), 9, fc="#d4d8dc", ec="#2a2c30", lw=1, zorder=8))


def mount_x0(g):
    return -P["mount_w"] if g["px"] < 0 else P["W"]


# ============================== 全体図 ==============================
def top_view(ax, L, cams, vis):
    W, D = P["W"], P["D"]
    dx = (W - P["desk_w"]) / 2
    ax.set_title("平面図（上から）  点 = そこが見えるカメラの台数（緑 2台以上 / 橙 1台 / 赤 見えない）", fontsize=12)
    rect(ax, dx, 0, P["desk_w"], P["desk_d"], DESK, ls="--", lw=1, z=1)
    ax.text(dx + 10, P["desk_d"] - 8, "机", ha="left", va="top", fontsize=9, color="#777")
    rect(ax, 0, 0, W, D, WOOD)                                                     # ③天板（奥の端まで）
    rect(ax, 0, D - T, W, T, FRAME)                                                # ①背板
    wy0 = D - T - P["wing_d"]
    rect(ax, 0, wy0, T, P["wing_d"], FRAME)                                        # ⑥袖板
    rect(ax, W - T, wy0, T, P["wing_d"], FRAME)
    # 背板の裏のモニターと受け
    rect(ax, L["mon_x"], D, P["mon_w"], P["mon_t"], MON, "モニター（背板の裏）", fs=8, color="white")
    rect(ax, L["fin_xl"], D, T, L["fin_d"], FRAME)
    rect(ax, L["fin_xr"], D, T, L["fin_d"], FRAME)
    rect(ax, L["mon_x"] - P["gap"], D + L["fin_d"] - T, L["bar_len"], T, FRAME)
    rect(ax, P["board_x"], P["board_y"], P["board_w"], P["board_d"], "#f0d9a8")
    tag(ax, P["board_x"] + 6, P["board_y"] + P["board_d"] - 6, "まな板（切る）", va="top")
    rect(ax, P["stove_x"], P["stove_y"], P["stove_w"], P["stove_d"], "#999")
    for bx in P["burner_x"]:
        ax.add_patch(Circle((P["stove_x"] + bx, P["stove_y"] + P["stove_d"] / 2), P["burner_r"], fc="#777", ec=EDGE, lw=1, zorder=3))
    pc = pan_center()
    ax.add_patch(Circle((pc[0], pc[1]), P["pan_r"], fc="#555", ec=EDGE, lw=1.5, zorder=4))
    tag(ax, P["stove_x"] + 6, P["stove_y"] + 6, "コンロ（2口）＋フライパン")
    rect(ax, P["mw_x"], P["mw_y"], P["mw_w"], P["mw_d"], MINT, "電子レンジ\n（おもちゃ）", fs=9)
    rect(ax, P["spice_x"], P["spice_y"], P["spice_w"], P["spice_d"], "#e8e8e8")
    tag(ax, P["spice_x"] + P["spice_w"] - 4, P["spice_y"] + 4, "調味料", ha="right")
    rect(ax, P["plate_x"], P["plate_y"], P["plate_w"], P["plate_d"], "#fff", "皿\n（提供）", fs=9, ls="--")
    for g in side_rigs(cams):                                                      # ⑫アーム台と左右のアーム
        rect(ax, mount_x0(g), D - P["mount_d"], P["mount_w"], P["mount_d"], PLY)
        draw_arm(ax, g, lambda p: (p[0], p[1]), lw=2.5)
    tag(ax, -P["mount_w"] - 8, D - P["mount_d"] / 2, "⑫", ha="right", va="center", fs=8)
    for z in vis.values():
        ax.scatter([p[0] for p in z["pts"]], [p[1] for p in z["pts"]],
                   c=[VIS_COLORS[min(n, 2)] for n in z["counts"]], s=9, zorder=9, linewidths=0)
    for c in cams:
        x, y = c["pos"][0], c["pos"][1]
        ax.plot([x, c["aim"][0]], [y, c["aim"][1]], ls=":", color=BLUE, lw=1.3, zorder=8)
        draw_cam(ax, x, y, f"{c['name']}カメラ", r=16, where="left" if c["name"] == "中央" else "below")
    dim_h(ax, 0, W, -60, f"本体 {W}")
    dim_h(ax, dx, dx + P["desk_w"], -130, f"机 {P['desk_w']}")
    dim_v(ax, 0, D, dx + P["desk_w"] + 30, f"本体 {D}")
    dim_v(ax, 0, L["depth_total"], dx + P["desk_w"] + 110, f"モニター込み {L['depth_total']:.0f}（机 {P['desk_d']}）")
    ax.text(W / 2, -200, "手前（プレイヤー側）", ha="center", fontsize=10, color="#555")
    ax.set_xlim(dx - 170, dx + P["desk_w"] + 230)
    ax.set_ylim(-230, max(P["desk_d"], L["depth_total"]) + 80)


def front_view(ax, L, cams):
    W, bh, dh = P["W"], P["back_h"], P["desk_h"]
    C, dw = counter_h(), P["desk_w"]
    dx = (W - dw) / 2
    ax.set_title("正面図（プレイヤーから見て）", fontsize=13)
    desk_front(ax, dx, dw)
    ax.text(dx + dw / 2, (dh - P["desk_t"]) / 2, "机（会場のもの）", ha="center", va="center", fontsize=11, color="#888")
    rect(ax, 0, dh, W, T, PLY)                                                       # ⑪底板（前の木口）
    tag(ax, -12, dh + T / 2, "⑪底板", ha="right", va="center", fs=8)
    rect(ax, 0, dh + T, W, L["wall_h"], MINT)
    ax.text(P["stove_x"] / 2, dh + T + L["wall_h"] / 2, "②前板（中につまみの部品・基板）", ha="center", va="center", fontsize=9, zorder=3)
    rect(ax, 0, C - T, W, T, "#c9a26b")
    draw_wall(ax, L, lambda x: x, C)
    rect(ax, L["win_x"], C + L["win_z0"], L["win_w"], L["win_h"], "#333")          # 窓（モニターの縁が少し見える）
    rect(ax, L["view_x"], C + P["view_z"], P["view_w"], P["view_h"], "#111",
         "モニター 27インチ\n（背板の裏から窓にはめる）\n画面の中は本物のキッチン", fs=10, color="white")
    rect(ax, 0, C, T, bh, FRAME)                                                   # ⑥袖板
    rect(ax, W - T, C, T, bh, FRAME)
    tag(ax, L["win_x"] * 0.5, C + (L["win_z0"] + L["win_z1"]) / 2, "①背板\n（1 枚板）", ha="center", va="center", fs=8, color="#7a5b2e")
    rect(ax, T, C, W - 2 * T, L["guide"][1], "#c9a26b", z=3)                       # 溝の角材（背板の前）
    rect(ax, P["mw_x"], C, P["mw_w"], P["mw_h"], MINT, "電子レンジ\n（おもちゃ）", fs=9)
    rect(ax, P["spice_x"], C, P["spice_w"], P["spice_h"], "#e8e8e8", z=2)
    rect(ax, P["board_x"], C, P["board_w"], P["board_t"], "#f0d9a8")
    rect(ax, P["stove_x"], C, P["stove_w"], T, "#888")
    pc = pan_center()
    rect(ax, pc[0] - P["pan_r"], C + pc[2], 2 * P["pan_r"], 30, "#555")
    for bx in P["burner_x"]:
        ax.add_patch(Circle((P["stove_x"] + bx, dh + T + L["wall_h"] / 2), 24, fc=WOOD, ec=EDGE, lw=1.5, zorder=5))
    rect(ax, L["cam_x"] - 26, C + bh, 52, 7, "#2a2c2e")                             # 中央カメラのクリップ
    for g in side_rigs(cams):                                                      # ⑫アーム台と左右のアーム
        rect(ax, mount_x0(g), C - T, P["mount_w"], T, PLY)
        draw_arm(ax, g, lambda p: (p[0], C + p[2]))
    tag(ax, -P["mount_w"] - 8, C - T / 2, "⑫アーム台", ha="right", va="center", fs=8)
    for c in cams:
        x = c["pos"][0]
        where = "above" if c["name"] == "中央" else ("left" if x < W / 2 else "right")
        draw_cam(ax, x, C + c["pos"][2], f"{c['name']}カメラ", where=where)
    xr = dx + dw + 40
    dim_h(ax, 0, W, -60, f"本体 {W}")
    dim_v(ax, 0, dh, xr, f"机 {dh}")
    dim_v(ax, dh, C, xr, f"土台 {P['base_h']}", rot=0)
    dim_v(ax, C, C + bh, xr, f"背板 {bh}")
    dim_v(ax, 0, C, xr + 150, f"床 → 天板 {C}")
    dim_h(ax, L["win_x"], L["win_x"] + L["win_w"], C + L["win_z1"] + 12, f"窓 {L['win_w']}×{L['win_h']}")
    ax.set_xlim(dx - 130, xr + 260)
    ax.set_ylim(-110, C + L["cam_z"] + 150)


def side_view(ax, L, cams):
    D, bh, dh, dd = P["D"], P["back_h"], P["desk_h"], P["desk_d"]
    C = counter_h()
    ax.set_title("側面図（左から）", fontsize=13)
    desk_front(ax, 0, dd)
    ax.plot([-800, 0], [0, 0], color="#555", lw=2)
    rect(ax, 0, dh, D, T, PLY)                                                      # ⑪底板
    tag(ax, -12, dh + T / 2, "⑪底板", ha="right", va="center", fs=8)
    rect(ax, 0, dh + T, D - T, L["wall_h"], MINT)                                   # ④側板
    rect(ax, D - T, dh + T, T, L["wall_h"], PLY)                                    # ⑬土台の後ろ板
    rect(ax, 0, C - T, D, T, "#c9a26b")                                             # ③天板（奥の端まで）
    rect(ax, D - T, C, T, bh, FRAME)                                                # ①背板（上の枠。天板の上にのる）
    rect(ax, D - T - L["guide"][0], C, L["guide"][0], L["guide"][1], "#c9a26b", z=4)   # 溝の角材（前）
    rect(ax, D, C - 40, L["guide"][0], 40 + L["guide"][1], "#c9a26b", z=4)         # 溝の角材（後ろ）
    rect(ax, D - T - P["wing_d"], C, P["wing_d"], bh, "none", lw=1, ls="--", z=6)   # ⑥袖板（手前にあるので点線）
    rect(ax, D, C + L["mon_z"], P["mon_t"], P["mon_h"], MON)                         # モニター（背板の裏）
    rect(ax, D, C + L["fin_z0"], L["fin_d"], L["fin_z1"] - L["fin_z0"], "none", lw=1, ls="--", z=6)  # ⑦縦の受け
    rect(ax, D, C + L["mon_z"] - T, L["fin_d"], T, FRAME)                            # ⑧下の受け木
    rect(ax, D + L["fin_d"] - T, C + L["bar_z"], T, P["bar_w"], FRAME)               # ⑩押さえ桟
    tag(ax, D + L["fin_d"] + 8, C + L["bar_z"] + P["bar_w"] / 2, "⑩押さえ桟", va="center", fs=8)
    tag(ax, D + L["fin_d"] + 8, C + L["mon_z"] - T - 20, "⑧受け木", va="top", fs=8)
    rect(ax, P["board_y"], C, P["board_d"], P["board_t"], "#f0d9a8")
    rect(ax, P["mw_y"], C, P["mw_d"], P["mw_h"], MINT, "レンジ", fs=9)
    rect(ax, D - T - 8, C + bh, T + 16, 7, "#2a2c2e")                                # 中央カメラのクリップ
    rect(ax, D, C + bh - 70, 7, 70, "#2a2c2e")
    for c in cams:
        y, z = c["pos"][1], C + c["pos"][2]
        if c["name"] == "中央":
            ay, az = c["aim"][1], C + c["aim"][2]
            ax.plot([y, ay], [z, az], ls=":", color=BLUE, lw=1.5)
            dist = np.linalg.norm(c["aim"] - c["pos"])
            ang = math.degrees(math.atan2(y - ay, z - az))
            draw_cam(ax, y, z, "中央カメラ")
            ax.text(y - 40, z + 120, f"狙う点まで {dist:.0f}\n真下から {ang:.0f}°", ha="right", va="bottom", fontsize=9, color=BLUE)
    g = side_rigs(cams)[0]                                                          # 左のアーム（右も同じ形）
    rect(ax, D - P["mount_d"], C - T, P["mount_d"], T, PLY, z=5)
    draw_arm(ax, g, lambda p: (p[1], C + p[2]))
    draw_cam(ax, P["side_cam_y"], C + P["side_cam_z"], "左・右カメラ（アーム）", where="below")
    mon_cz = C + P["view_z"] + P["view_h"] / 2
    px = P["eye_y"]
    ax.plot([px, px], [0, P["eye_high"] - 90], color="#bbb", lw=8, solid_capstyle="round")
    for eye, name in ((P["eye_low"], "身長155cm"), (P["eye_high"], "身長180cm")):
        ax.add_patch(Circle((px, eye), 65, fc="#f2f2f2", ec="#999", zorder=3))
        deg = math.degrees(math.atan2(eye - mon_cz, D - px))
        ax.plot([px + 40, D], [eye, mon_cz], ls="--", color="#999", lw=1)
        ax.text(px - 85, eye, f"{name}\n目の高さ {eye}\n画面へ 下{deg:.0f}°", ha="right", va="center", fontsize=8, color="#555")
    xr = max(dd, L["depth_total"]) + 40
    dim_h(ax, 0, D, -60, f"本体 {D}")
    dim_h(ax, 0, L["depth_total"], -130, f"モニター込み {L['depth_total']:.0f}")
    dim_v(ax, 0, dh, xr, f"机 {dh}")
    dim_v(ax, dh, C, xr, f"土台 {P['base_h']}", rot=0)
    dim_v(ax, C, C + bh, xr, f"背板 {bh}")
    ax.text(D / 2, -200, "← 手前（プレイヤー側）", ha="center", fontsize=10, color="#555")
    ax.set_xlim(-800, xr + 170)
    ax.set_ylim(-230, max(C + L["cam_z"], P["eye_high"]) + 260)


def back_view(ax, L):
    """後ろから見た図（左右が逆になる）。モニターのはめ方と受けの板"""
    W, bh, dh, dw = P["W"], P["back_h"], P["desk_h"], P["desk_w"]
    C, base = counter_h(), P["base_h"]
    dx = (W - dw) / 2
    xm = lambda x: W - x                                                             # 後ろから見ると左右が逆
    ax.set_title("背面図（後ろから見て。左右が逆）  モニターは後ろからはめる", fontsize=13)
    desk_front(ax, dx, dw)
    rect(ax, 0, dh, W, T, PLY)                                                       # ⑪底板（後ろの木口）
    rect(ax, 0, dh + T, W, L["wall_h"], PLY)                                         # ⑬土台の後ろ板
    rect(ax, 0, C - T, W, T, "#c9a26b")                                              # ③天板の後ろの木口
    for x0, x1 in L["back_blocks"]:                                                  # 溝の角材（後ろ板の裏。天板より上に出る）
        rect(ax, W - x1, C - 40, x1 - x0, 40 + L["guide"][1], "#c9a26b", z=5)
    for xl in L["latch_x"]:                                                          # パチン錠（上の枠 ↔ 下の台）
        rect(ax, W - xl - 14, C - 38, 28, 80, "#888", z=6)
    tag(ax, W - L["latch_x"][1] + 22, C + 25, "パチン錠（上の枠を外すときに起こす）", va="center", fs=8)
    for x0 in (-P["mount_w"], W):                                                    # ⑫アーム台（左右が逆）とクランプ
        rect(ax, W - x0 - P["mount_w"], C - T, P["mount_w"], T, PLY)
        rect(ax, W - x0 - P["mount_w"] / 2 - 16, C - 52, 32, 64, "#2a2c30", z=4)
    R = draw_wall(ax, L, xm, C)
    R(L["win_x"], L["win_z0"], L["win_w"], L["win_h"], "#333")                        # 窓（後ろからは穴）
    R(L["mon_x"], L["mon_z"], P["mon_w"], P["mon_h"], MON, "モニター（背面）", fs=10, color="white", z=4)
    R(L["fin_xl"], L["fin_z0"], T, L["fin_z1"] - L["fin_z0"], FRAME, z=5)            # ⑦
    R(L["fin_xr"], L["fin_z0"], T, L["fin_z1"] - L["fin_z0"], FRAME, z=5)
    R(L["mon_x"] - P["gap"], L["mon_z"] - T, P["mon_w"] + 2 * P["gap"], T, FRAME, z=5)  # ⑧
    R(L["mon_x"] - P["gap"], L["bar_z"], L["bar_len"], P["bar_w"], FRAME, "⑩押さえ桟（回転留めを回して外せる）", fs=9, z=6)
    for fx in (L["fin_xl"], L["fin_xr"]):                                            # L 金具（縦の受け 1 本に 3 個）
        for zz in ((L["fin_z0"] + L["win_z0"]) / 2, (L["win_z0"] + L["win_z1"]) / 2 - 80, (L["win_z1"] + L["fin_z1"]) / 2):
            side = -18 if xm(fx) > W / 2 else T + 3
            ax.add_patch(Rectangle((min(xm(fx), xm(fx + T)) + side, C + zz - 12), 15, 24, fc="#888", ec=EDGE, lw=1, zorder=6))
    x_hole = xm(W - 120)
    ax.add_patch(Circle((x_hole, dh + T + L["wall_h"] / 2), 15, fc="white", ec=DIM, lw=1.3, zorder=6))
    tag(ax, x_hole + 22, dh + T + L["wall_h"] / 2, "基板の USB Φ30", va="center", fs=8, color=DIM)
    tag(ax, xm(L["fin_xl"]) + 10, C + L["fin_z1"] + 10, "⑦縦の受け", fs=8)
    tag(ax, xm(L["mon_x"] + P["mon_w"] / 2), C + L["mon_z"] - T - 10, "⑧下の受け木", ha="center", va="top", fs=8)
    tag(ax, 20, C + bh + 100, "灰色の小さい四角 = L 金具（縦の受けを背板に留める）", fs=8)
    xr = dx + dw + 40
    dim_v(ax, C + L["mon_z"], C + L["mon_z"] + P["mon_h"], xr, f"モニター {P['mon_h']}")
    dim_h(ax, min(xm(L["fin_xl"]), xm(L["fin_xr"] + T)), max(xm(L["fin_xl"]), xm(L["fin_xr"] + T)),
          C + L["fin_z1"] + 45, f"縦の受けの外から外 {L['fin_xr'] + T - L['fin_xl']:.0f}")
    ax.set_xlim(dx - 130, xr + 200)
    ax.set_ylim(-110, C + bh + 150)


def draw_overview(L, cams, vis, notes):
    fig, axes = plt.subplots(2, 2, figsize=(26, 19), dpi=100)
    top_view(axes[0][0], L, cams, vis)
    front_view(axes[0][1], L, cams)
    side_view(axes[1][0], L, cams)
    back_view(axes[1][1], L)
    for ax in axes.ravel():
        ax.set_aspect("equal")
        ax.axis("off")
    fig.suptitle(f"おままごとキッチン（卓上型） 全体図    本体 W{P['W']} × D{P['D']} × H{P['base_h'] + P['back_h']}"
                 f"    机 {P['desk_h']} の上 → 天板は床から {counter_h()}    単位 mm", fontsize=16, y=0.995)
    fig.text(0.01, 0.005, "\n".join(notes), fontsize=12, va="bottom", color="#222")
    plt.tight_layout(rect=(0, 0.02 * len(notes), 1, 0.97))
    fig.savefig(IMG / "kitchen_plan.png", bbox_inches="tight", facecolor="white")
    plt.close(fig)


# ============================== 部材図 ==============================
def draw_part(ax, p):
    a, b = p.a, p.b
    s = max(a, b)
    rect(ax, 0, 0, a, b, p.color)
    th = s * 0.026                                   # 文字の高さ（図の単位）の目安
    for x, y, w, h, name in p.cutouts:               # くり抜く所（白抜き）
        ax.add_patch(Rectangle((x, y), w, h, fc="white", ec=DIM, lw=1.5, zorder=3))
        ax.text(x + w / 2, y + h / 2, f"{name}\n{w:.0f} × {h:.0f}", ha="center", va="center", fontsize=9, color=DIM, zorder=4)
    for x, y, w, h, name in p.marks:
        rect(ax, x, y, w, h, "none", lw=1, ls="--", ec="#666", z=3)
        if not name:
            continue
        kw = dict(fontsize=7, color="#555", zorder=4)
        if w >= th * (len(name) + 0.5) and h >= th * 1.5:            # 中に入る
            ax.text(x + w / 2, y + h / 2, name, ha="center", va="center", **kw)
        elif w >= h:                                                 # 細い横長 → 板の真ん中の側の上か下に書く
            up = y + h / 2 < b / 2
            ax.text(x + w / 2, y + h + th * 0.3 if up else y - th * 0.3, name, ha="center", va="bottom" if up else "top", **kw)
        else:                                                        # 細い縦長 → 板の真ん中の側の右か左に書く
            right = x + w / 2 < a / 2
            ax.text(x + w + th * 0.3 if right else x - th * 0.3, y + h / 2, name, ha="left" if right else "right", va="center", **kw)
    lines = textwrap.wrap(p.where, 40)
    for x, y, w, h, name in p.cutouts:
        lines.append(f"{name}: {w:.0f} × {h:.0f}、左下の角 = 左から {x:.0f}・下から {y:.0f}")
    for i, h in enumerate(p.holes):
        letter = "ABCDEFGH"[i]
        r = max(h.d / 2, s * 0.006)
        if h.kind == "cb":
            ax.add_patch(Circle((h.x, h.y), h.cb / 2, fc="none", ec=DIM, lw=1, ls=":", zorder=5))
        ls = "--" if h.kind in ("opt", "mark") else "-"
        fc = "none" if h.kind == "mark" else "white"
        ax.add_patch(Circle((h.x, h.y), r, fc=fc, ec=DIM, lw=1.3, ls=ls, zorder=6))
        tag(ax, h.x + max(r, h.cb / 2) + s * 0.008, h.y + max(r, h.cb / 2) * 0.6, letter, fs=9, color=DIM)
        what = {"hole": f"Φ{h.d:g} の穴", "cb": f"Φ{h.d:g} の穴（裏から Φ{h.cb:g} 深さ {T - 4} の座ぐり）",
                "opt": f"Φ{h.d:g} の穴（任意）", "mark": f"Φ{h.d:g} の円（目印）"}[h.kind]
        lines.append(f"{letter}: {h.note}  {what}  中心 = 左から {h.x:.0f}・下から {h.y:.0f}")
    dim_h(ax, 0, a, -s * 0.07, f"{a:.0f}", fs=9)
    dim_v(ax, 0, b, a + s * 0.03, f"{b:.0f}", fs=9)
    ax.set_title(f"{p.no} {p.name}  {a:.0f}×{b:.0f}×{p.t:g}  {p.qty}枚\n{p.mat}  ／ {p.view}", fontsize=10)
    ax.text(0, -s * 0.13, "\n".join(lines), ha="left", va="top", fontsize=8, color="#333", linespacing=1.6)
    ax.set_aspect("equal")
    ax.axis("off")
    ax.set_xlim(-s * 0.08, a + s * 0.42)
    ax.set_ylim(-s * (0.2 + 0.075 * len(lines)), b + s * 0.08)


def draw_base_inside(ax, L):
    """土台の中（天板を外して上から見た図）。基板と配線は底板⑪の上に置く"""
    W, D, px = P["W"], P["D"], P["part_x"]
    ax.set_title("土台の中（天板を外して上から）  底板⑪の上に つまみの部品・基板・配線", fontsize=10)
    rect(ax, 0, 0, W, D, "#f3e6cc", lw=1)                             # ⑪底板（いちばん下）
    rect(ax, 0, 0, W, T, MINT, lw=1)                                  # ②前板
    rect(ax, 0, T, T, D - 2 * T, PLY, lw=1)                            # ④側板
    rect(ax, W - T, T, T, D - 2 * T, PLY, lw=1)
    rect(ax, px, T, T, D - 2 * T, PLY, lw=1)                            # ⑤中仕切り
    rect(ax, 0, D - T, W, T, FRAME, lw=1)                               # ⑬土台の後ろ板
    ax.plot([L["seam_x"], L["seam_x"]], [0, D], color="#7a5b2e", lw=1.2, ls="--", zorder=3)   # 底板の継ぎ目
    rect(ax, L["seam_x"], T, 30, D - 2 * T, "#c9a26b", lw=1, z=3)                  # 継ぎ目を受ける角材
    ax.text(px / 2, D * 0.35, "左の部屋（空き）", ha="center", va="center", fontsize=8, color="#777")
    ax.text(px / 2, D * 0.35 - 40, f"見えている面 = 底板⑪の上面（天板の下まで高さ {L['wall_h']}）",
            ha="center", va="center", fontsize=7, color="#7a5b2e")
    esx, esy, esw, esd = L["esp"]                                        # 底板の上にスペーサーで留める
    rect(ax, esx, esy, esw, esd, "#2e86de", lw=1, z=4)
    tag(ax, esx, esy + esd + 6, "ESP32（底板に留める）", fs=7)
    for i, bx in enumerate(P["burner_x"]):
        kx = P["stove_x"] + bx
        rect(ax, kx - 8, T, 16, 16, "#555", lw=1, z=4)                   # つまみの部品（前板の裏）
        if kx <= esx + esw - 10:                                         # 基板の真ん前 → まっすぐ奥へ
            ax.plot([kx, kx], [T + 16, esy], color="#e67e22", lw=1.3, zorder=3)
        else:                                                            # 基板の手前で曲げて、基板の下の縁へ
            xin = esx + esw - 15
            ax.plot([kx, kx, xin, xin], [T + 16, esy - 20, esy - 20, esy], color="#e67e22", lw=1.3, zorder=3)
        tag(ax, kx + 12, T + 20, f"つまみ{'左右'[i]}", fs=7)
    hx, uy = W - 120, esy + esd / 2
    ax.plot([esx + esw, hx, hx], [uy, uy, D - T], color="#8e44ad", lw=1.5, zorder=3)
    tag(ax, hx - 8, D - T - 40, "USB → 背板 Φ30 → PC", ha="right", fs=7, color="#8e44ad")
    mx, my = P["mw_x"] + P["mw_w"] - 40, P["mw_y"] + 40
    xl = esx - 30
    ax.plot([mx, mx, xl, xl, esx], [my, D - T - 60, D - T - 60, uy, uy], color="#999", lw=1.2, ls="--", zorder=3)
    tag(ax, mx - 10, D - T - 70, "レンジのスイッチ線（任意）\n天板 Φ15 → 中仕切り Φ20", ha="right", va="top", fs=7, color="#666")
    ax.set_aspect("equal")
    ax.axis("off")
    ax.set_xlim(-30, W + 30)
    ax.set_ylim(-30, D + 30)


def draw_parts(ps, L):
    rows = math.ceil((len(ps) + 2) / 4)                 # 部材 + 土台の中 + 図の見方
    fig, axes = plt.subplots(rows, 4, figsize=(26, 6.5 * rows), dpi=100)
    axes = axes.ravel()
    for ax, p in zip(axes, ps):
        draw_part(ax, p)
    draw_base_inside(axes[len(ps)], L)
    for ax in axes[len(ps) + 1:]:
        ax.axis("off")
    axes[-1].text(0, 1, "図の見方\n・点線の四角 = 他の部材が付く所・物を置く所\n・赤い丸 = 穴（点線の丸は任意の穴・目印）\n"
                  "・寸法は mm。穴の位置は「左から・下から」の中心\n・ビスは板の端から 8mm 以上内側、150mm おき\n"
                  "・④⑥⑦⑫は 2 枚とも同じ形\n・背板①は 1 枚板で、窓をくり抜く\n  （くり抜いた板から⑨と⑫を取る）",
                  va="top", fontsize=11, transform=axes[-1].transAxes)
    fig.suptitle("おままごとキッチン 部材図（寸法と穴の位置）  単位 mm", fontsize=16, y=0.995)
    plt.tight_layout(rect=(0, 0, 1, 0.98))
    fig.savefig(IMG / "kitchen_parts.png", bbox_inches="tight", facecolor="white")
    plt.close(fig)


# ============================== 板取り図 ==============================
def draw_cutlayout(sheets, ps):
    pb = {p.no: p for p in ps}
    sw, sh = SHEET
    fig, axes = plt.subplots(len(sheets), 1, figsize=(18, 9 * len(sheets)), dpi=100, squeeze=False)
    for i, (ax, sheet) in enumerate(zip(axes[:, 0], sheets), 1):
        ax.add_patch(Rectangle((0, 0), sw, sh, fc="#f4f4f4", ec=EDGE, lw=2, hatch="//", zorder=1))
        for no, x, y, w, h in sheet:
            p = pb[no]
            rect(ax, x, y, w, h, PLY, z=2)
            for cx, cy, cw, ch, _ in p.cutouts:                   # くり抜き。中からほかの部材を取る
                rot_ = abs(w - p.a) > 0.5
                wx0, wy0, ww_, wh_ = (x + cy, y + cx, ch, cw) if rot_ else (x + cx, y + cy, cw, ch)
                ax.add_patch(Rectangle((wx0, wy0), ww_, wh_, fc="#f8f3e8", ec=DIM, lw=1.2, ls="--", zorder=3))
                kids = [q for q in ps if q.src == f"{p.no} の窓" for _ in range(q.qty)]
                kx = wx0 + 6
                for q in kids:
                    qa, qb = (q.a, q.b) if q.b <= wh_ - 12 else (q.b, q.a)
                    rect(ax, kx, wy0 + 6, qa, qb, PLY, z=4)
                    ax.text(kx + qa / 2, wy0 + 6 + qb / 2, f"{q.no}\n{q.a:.0f}×{q.b:.0f}", ha="center", va="center", fontsize=8, zorder=5)
                    kx += qa + KERF
                ax.text(wx0 + ww_ / 2, wy0 + wh_ - 8, "窓のくり抜き", ha="center", va="top", fontsize=8, color=DIM, zorder=5)
                continue
            fs = 11 if min(w, h) > 120 else 8
            rot = 90 if h > w * 1.8 else 0
            ty = y + h / 2 if not p.cutouts else y + h * 0.12
            ax.text(x + w / 2, ty, f"{no}{p.name}\n{p.a:.0f}×{p.b:.0f}", ha="center", va="center",
                    fontsize=fs, rotation=rot, zorder=3)
        dim_h(ax, 0, sw, -45, f"{sw}")
        dim_v(ax, 0, sh, sw + 20, f"{sh}")
        ax.set_title(f"{i}枚目  {PLYWOOD}  {sw}×{sh}（斜線 = 余り。捨てずに予備・試し打ちに使う）", fontsize=13)
        ax.set_aspect("equal")
        ax.axis("off")
        ax.set_xlim(-30, sw + 120)
        ax.set_ylim(-90, sh + 30)
    fig.suptitle(f"板取り図  合板 {len(sheets)} 枚  部材の間は のこ刃の幅 {KERF}mm をあける", fontsize=16, y=0.995)
    plt.tight_layout(rect=(0, 0, 1, 0.98))
    fig.savefig(IMG / "kitchen_cutlayout.png", bbox_inches="tight", facecolor="white")
    plt.close(fig)


# ============================== チェックと表 ==============================
def coverage_lines(cams, vis):
    lines = []
    for zone, z in vis.items():
        n = len(z["pts"])
        per = " ・ ".join(f"{name} {100 * v / n:.0f}%" for name, v in z["per_cam"].items())
        one = 100 * sum(1 for k in z["counts"] if k >= 1) / n
        two = 100 * sum(1 for k in z["counts"] if k >= 2) / n
        lines.append(f"{zone}が見える割合: {per}   ／  1台以上 {one:.0f}%  ・  2台以上 {two:.0f}%")
    return lines


def check(L, cams, vis, sheets, ps):
    C = counter_h()
    W, bh = P["W"], P["back_h"]
    msgs = []
    if W > P["desk_w"]:
        msgs.append(f"本体の幅 {W} が机の幅 {P['desk_w']} より大きい（はみ出す）")
    if P["D"] > P["desk_d"]:
        msgs.append(f"本体の奥行き {P['D']} が机の奥行き {P['desk_d']} より大きい（はみ出す）")
    elif L["depth_total"] > P["desk_d"]:
        msgs.append(f"モニターの後ろが机から {L['depth_total'] - P['desk_d']:.0f} はみ出す（重さは机の上なので倒れない。"
                    "机の後ろは、この分と PC を置く分をあける）")
    if not 800 <= C <= 950:
        msgs.append(f"天板が床から {C}：大人が立って作業するには{'低い' if C < 800 else '高い'}（目安 800〜950）")
    if L["win_x"] < P["mw_x"] + P["mw_w"]:
        msgs.append("窓（画面）が電子レンジの後ろに隠れる")
    if L["fin_xr"] + T > W - T:
        msgs.append("右の縦の受けが右の袖板にぶつかる")
    if L["win_z1"] + 40 > bh:
        msgs.append("窓の上の背板が 40 より細くなる。背板を高くするか画面を下げる")
    if L["mon_z"] - T < 0:
        msgs.append("下の受け木が天板より下に入る。画面を上げる")
    for p in ps:
        if p.mat == PLYWOOD and min(p.a, p.b) < MIN_CUT_W:
            msgs.append(f"{p.no}{p.name} は幅 {min(p.a, p.b):.0f} で細い。店によっては切ってもらえない（先に聞く）")
    if P["spice_y"] + P["spice_d"] > L["back_front"]:
        msgs.append("調味料の置き場が背板にぶつかる")
    if P["stove_x"] < P["part_x"] + T:
        msgs.append("コンロ板が中仕切りより左にはみ出す（つまみの部品が左の部屋に入る）")
    for g, name in zip(side_rigs(cams), ("左", "右")):
        if not 55 <= g["th1"] <= 100:
            msgs.append(f"{name}のアームの下の段が水平から {g['th1']:.0f}°。バネで支えにくい角度なので、アーム台の位置を見直す")
        if g["neck"] > ARM["neck_max"]:
            msgs.append(f"{name}のカメラは自由雲台の首を {g['neck']:.0f}° 倒さないと向かない（限度 {ARM['neck_max']}°）。アームの位置を見直す")
        outside = -g["E"][0] if name == "左" else g["E"][0] - W        # 関節が本体の外へ出ている長さ
        if outside < 10:
            msgs.append(f"{name}のアームの関節が本体にぶつかる")
    for zone, z in vis.items():
        hidden = sum(1 for k in z["counts"] if k == 0)
        if hidden:
            msgs.append(f"{zone}の {100 * hidden / len(z['counts']):.0f}% がどのカメラからも見えない")
    return msgs + check_sheets(sheets)


def md_dims(L, ps, warnings):
    C = counter_h()
    bc = C + P["view_z"] + P["view_h"] / 2
    knobs = "・".join(f"{P['stove_x'] + bx:.0f}" for bx in P["burner_x"])
    hid = hidden_top(L, P["eye_high"])
    rows = [
        ("本体（幅×奥行き×高さ）", f"{P['W']} × {P['D']} × {P['base_h'] + P['back_h']}（机の上面から背板の上端まで）"),
        ("モニターを含めた奥行き", f"{L['depth_total']:.0f}（モニターと受けが背板の後ろに出る）"),
        ("天板の高さ", f"机の上面から {P['base_h']}。床から {C}（机 {P['desk_h']} のとき）"),
        ("背板の上端", f"床から {C + P['back_h']}"),
        ("窓（画面の枠）", f"左端から {L['win_x']}、天板から {L['win_z0']}。大きさ {L['win_w']} × {L['win_h']}"),
        ("モニター本体（背板の裏）", f"左端から {L['mon_x']:.1f}、天板から {L['mon_z']}（本体 {P['mon_w']}×{P['mon_h']}×{P['mon_t']}）"),
        ("画面の中心", f"床から {bc:.0f}"),
        ("画面の上の端", f"背板の厚みで、身長 180cm の人からは上の約 {hid:.0f}mm が隠れる" if hid > 0.5 else "隠れない"),
        ("土台の中", f"底板⑪の上から天板の下まで 高さ {L['wall_h']}。右の部屋（基板・つまみの部品）は "
                     f"幅 {P['W'] - T - P['part_x'] - T} × 奥行き {L['inner_d']}"),
        ("つまみ", f"左端から {knobs}、机の上面から {T + L['wall_h'] / 2:g}（前板の高さの真ん中）"),
        ("中央カメラ", f"左端から {L['cam_x']}、手前から {L['cam_y']}、天板から {L['cam_z']}（背板の上の縁にクリップ。下に約 {center_cam_clip(L)['tilt']:.0f}° 向ける。画角 {P['fov_center']}°）"),
        ("左右カメラ", f"手前から {P['side_cam_y']}、天板から {P['side_cam_z']}（アーム台⑫に挟んだアームの先、画角 {P['fov_side']}°）"),
        ("重さ（目安）", f"約 {weight_kg(ps):.0f} kg（モニター・カメラ・金物を除く）"),
    ]
    out = ["| 項目 | 寸法（mm） |", "| --- | --- |"] + [f"| {a} | {b} |" for a, b in rows]
    if warnings:
        out += ["", "**注意（tools/kitchen_plan.py のチェック）**", ""] + [f"- {m}" for m in warnings]
    return "\n".join(out)


def md_cutlist(ps, sheets):
    out = ["| 番号 | 名前 | 長さ × 幅 × 厚さ | 枚数 | 付く所 |", "| --- | --- | --- | --- | --- |"]
    for p in ps:
        out.append(f"| {p.no} | {p.name} | {p.a:.0f} × {p.b:.0f} × {p.t:g} | {p.qty} | {p.where} |")
    n_ply = sum(p.qty for p in ps if p.mat == PLYWOOD and not p.src)
    kids = [p for p in ps if p.src]
    out += ["", f"- すべて {PLYWOOD}。{len(sheets)} 枚（{SHEET[1]}×{SHEET[0]}）から {n_ply} 枚を取る。"
            f"並べ方は下の板取り図。カットはおよそ {2 * n_ply} 回"]
    if kids:
        out.append("- " + "・".join(f"{p.no}{p.name}" for p in kids) + f" は、{kids[0].src}のくり抜いた板から取る（板取り図の点線の中）")
    return "\n".join(out)


def md_camera(L, cams, vis):
    out = ["| カメラ | 位置（左端から・手前から・天板から） | 画角 | 狙う所 |", "| --- | --- | --- | --- |"]
    aim_name = {"中央": "まな板とフライパンの真ん中", "左": "まな板", "右": "フライパン"}
    for c in cams:
        x, y, z = c["pos"]
        out.append(f"| {c['name']} | {x:.0f} ・ {y:.0f} ・ {z:.0f} | {c['fov']}° | {aim_name[c['name']]} |")
    out += ["", "計算の結果（手で隠れる分は含まない）:", ""] + [f"- {line}" for line in coverage_lines(cams, vis)]
    return "\n".join(out)


# ============================== 組み立ての 3D ==============================
def model3d(L, cams, ps):
    """3D の部材と工程。座標は設計図と同じ x・y で、z だけ机の上面から測る。形は shape で決まる
    （box / rod / ball / knob / burner / pan / carrot / microwave / bottle / plate / screen / cam）。
    from = 工程で入ってくる向きと距離、ex = 「分解」で動かす量（どちらも x 右・y 奥・z 上）"""
    W, D, bh, base, g = P["W"], P["D"], P["back_h"], P["base_h"], P["gap"]
    Z, wall_h, px = base, L["wall_h"], P["part_x"]
    wx, ww, wz0, wz1 = L["win_x"], L["win_w"], L["win_z0"], L["win_z1"]
    fxl, fxr, fz0, fz1, fd = L["fin_xl"], L["fin_xr"], L["fin_z0"], L["fin_z1"], L["fin_d"]
    zc = T + wall_h / 2                      # 前板の高さの真ん中（つまみの中心）
    info = {p.no: p for p in ps}
    items = []
    # 工程の番号（steps の並びと同じ）
    (S_BOTTOM, S_FRAME, S_INSIDE, S_TOP, S_UPPER, S_JOIN, S_LEDGE, S_MON, S_BAR, S_CAMC,
     S_MOUNT, S_ARM, S_SCAM, S_CABLE, S_TOYS) = range(1, 16)
    # 「分解」: 底板は机の上に残す。土台の枠は少し浮かせて外へ、天板とその上の物は上へ、背板とモニターは後ろへ
    EW, ET, EB = 40, 230, 200
    e_up = ET + 100                          # 天板の上に立つ物・置く物
    wy0 = D - T - P["wing_d"]

    def num(v):
        if isinstance(v, (list, tuple, np.ndarray)):
            return [round(float(x), 1) for x in v]
        if isinstance(v, (float, int, np.floating, np.integer)) and not isinstance(v, bool):
            return round(float(v), 1)
        return v

    def add(id_, no, name, step, kind, shape, frm, dims=None, where=None, sub=False, arrow=True, ex=None, **geo):
        p = info.get(no)
        items.append({
            "id": id_, "no": no, "name": name, "step": step, "kind": kind, "shape": shape,
            "from": num(frm), "noArrow": not arrow, "sub": sub, "ex": num(ex) if ex is not None else None,
            "dims": dims if dims is not None else (f"{p.a:.0f} × {p.b:.0f} × {p.t:g}" if p else ""),
            "where": where if where is not None else (p.where if p else ""),
            **{k: (v if k in ("ax", "pts") else num(v)) for k, v in geo.items()},
        })

    def box(id_, no, name, step, kind, b, frm, **kw):
        add(id_, no, name, step, kind, "box", frm, box=b, **kw)

    def wire(id_, name, step, kind, pts, r, where):
        """折れ線の線（配線）。1 本につき最初の 1 区間だけ部材の一覧に出す"""
        for k in range(len(pts) - 1):
            add(f"{id_}{k}", "", name, step, kind, "rod", [0, 0, 120], a=pts[k], b=pts[k + 1], r=r,
                sub=k > 0, arrow=False, where=where, ex=[0, 0, 0])

    R = 170
    # 机（工程に入らない。「机」で表示を切り替える）
    dx = (W - P["desk_w"]) / 2
    box("desk", "", "机", -1, "desk", [dx, 0, -P["desk_t"], dx + P["desk_w"], P["desk_d"], 0], [0, 0, 0], arrow=False, round=3)
    for i, lx in enumerate((dx + 30, dx + P["desk_w"] - 70)):
        for j, ly in enumerate((30, P["desk_d"] - 70)):
            box(f"leg{i}{j}", "", "机の脚", -1, "leg", [lx, ly, -P["desk_h"], lx + 40, ly + 40, -P["desk_t"]], [0, 0, 0], arrow=False)

    # 1 底板（いちばん下。上から置く。中仕切りの所で 2 枚に分かれている）
    sxm = L["seam_x"]
    box("11a", "⑪a", "底板 左", S_BOTTOM, "ply", [0, 0, 0, sxm, D, T], [0, 0, R], ex=[0, 0, 0])
    box("11b", "⑪b", "底板 右", S_BOTTOM, "ply", [sxm, 0, 0, W, D, T], [0, 0, R], ex=[0, 0, 0])

    # 3 土台の枠（底板の上に立てる。前板は塗装し、表につまみの目盛りを描く）
    ex_front = [0, -170, EW]
    box("2", "②", "前板", S_FRAME, "mint", [0, 0, T, W, T, T + wall_h], [0, -R, 0], face="knobs", ex=ex_front)
    box("4l", "④", "側板", S_FRAME, "ply", [0, T, T, T, D - T, T + wall_h], [-R, 0, 0], ex=[-170, 0, EW])
    box("4r", "④", "側板", S_FRAME, "ply", [W - T, T, T, W, D - T, T + wall_h], [R, 0, 0], ex=[170, 0, EW])
    box("5", "⑤", "中仕切り", S_FRAME, "ply", [px, T, T, px + T, D - T, T + wall_h], [0, 0, R], ex=[0, 0, EW])
    box("13", "⑬", "土台の後ろ板", S_FRAME, "ply", [0, D - T, T, W, D, T + wall_h], [0, R, 0], ex=[0, 170, EW])
    box("seamcleat", "", "底板の継ぎ目の角材", S_FRAME, "board", [sxm, T, T, sxm + 30, D - T, T + 15], [0, 0, R], sub=True, arrow=False,
        dims="15 × 30 くらい", where="中仕切り⑤の右の面に付け、底板 右⑪b の左の縁を上から押さえてビスで留める", ex=[0, 0, 0])

    # 4 つまみと基板（土台の中の配線。天板をのせると手が入らないので、ここで済ませる）
    sx, sy, sw, sd = P["stove_x"], P["stove_y"], P["stove_w"], P["stove_d"]
    kxs = [sx + bx for bx in P["burner_x"]]
    esx, esy, esw, esd = L["esp"]
    for i, kx in enumerate(kxs):
        add(f"kp{i}", "", "つまみの部品", S_INSIDE, "metal", "rod", [0, 90, 0], a=[kx, T, zc], b=[kx, T + 22, zc], r=12,
            arrow=False, dims="ネジ部分 M7〜M9", where="可変抵抗かロータリーエンコーダ。前板②の穴に裏から差し込み、表からナットで締める",
            ex=ex_front)
        add(f"kn{i}", "", "つまみ", S_INSIDE, "knob", "knob", [0, -R, 0], c=[kx, -12, zc], r=22, h=24,
            dims="直径 40〜50", where="前板②の表で、つまみの部品の軸にかぶせる", ex=[0, -290, EW])
    add("esp", "", "基板（ESP32）", S_INSIDE, "pcb", "box", [0, 0, R], box=[esx, esy, T + 5, esx + esw, esy + esd, T + 13],
        dims=f"約 {esw} × {esd}（開発ボード）", where="底板⑪の右の部屋にスペーサーで留める", ex=[0, 0, 0])
    wz = T + 3                               # 線は底板の上をはわせる
    for i, kx in enumerate(kxs):
        pts = [[kx, T + 24, zc - 6], [kx, T + 60, wz]]
        if kx <= esx + esw - 10:             # 基板の真ん前 → まっすぐ奥へ
            pts.append([kx, esy + 2, wz])
        else:                                # 基板の手前で曲げて、基板の下の縁へ
            xin = esx + esw - 15
            pts += [[kx, esy - 20, wz], [xin, esy - 20, wz], [xin, esy + 2, wz]]
        wire(f"w{i}", "配線", S_INSIDE, "wire", pts, 2.5, "つまみの部品 → 基板（ESP32）")
    hx, uy = W - 120, esy + esd / 2
    wire("usb", "USB ケーブル", S_INSIDE, "usb",
         [[esx + esw - 2, uy, T + 9], [esx + esw + 15, uy, wz], [hx, uy, wz], [hx, D - T - 40, wz], [hx, D - T, zc], [hx, D + 80, zc]],
         3, "基板 → 土台の後ろ板⑬ の Φ30 → 本体の後ろの PC（電源もここから）")

    # 5 天板とコンロ板（コンロ板は天板の裏から留めておき、いっしょにのせる）
    box("3", "③", "天板", S_TOP, "ply", [0, 0, T + wall_h, W, D, Z], [0, 0, 240], ex=[0, 0, ET])
    box("9", "⑨", "コンロ板", S_TOP, "stove", [sx, sy, Z, sx + sw, sy + sd, Z + T], [0, 0, 240], ex=[0, 0, ET + 100])
    for i, bx in enumerate(P["burner_x"]):
        add(f"bn{i}", "", "バーナーと五徳", S_TOP, "burner", "burner", [0, 0, 240], c=[sx + bx, sy + sd / 2, Z + T], r=P["burner_r"],
            sub=True, arrow=False, ex=[0, 0, ET + 130])
    # 溝の角材（上の枠の背板を差し込む溝）。前は天板の上、後ろは後ろ板⑬の裏で天板より上に出す
    gt_, gh_ = L["guide"]
    box("gfront", "", "溝の角材（前）", S_TOP, "board", [T, D - T - gt_, Z, W - T, D - T, Z + gh_], [0, 0, 120], arrow=False,
        dims=f"{gt_} × {gh_} × {W - 2 * T:.0f}", where="天板の上、背板のすぐ前。袖板の間いっぱいに付けるので、上の枠が左右にもずれない", ex=[0, 0, ET])
    for i, (x0, x1) in enumerate(L["back_blocks"]):
        box(f"gback{i}", "", "溝の角材（後ろ）", S_TOP, "board", [x0, D, Z - 40, x1, D + gt_, Z + gh_], [0, 60, 0], arrow=False,
            sub=i > 0, dims=f"{gt_} × {40 + gh_} × {x1 - x0}", where=f"後ろ板⑬の裏に付け、天板より {gh_} 上に出す。背板の下の縁を後ろから受ける",
            ex=[0, 170, EW])

    # 5 上の枠を組む（窓をくり抜いた 1 枚の背板＋縦の受け＋袖板。床で組み、組んだまま天板の溝に差し込む）
    add("1", "①", "背板", S_UPPER, "ply", "wpanel", [0, R, 0], box=[0, D - T, Z, W, D, Z + bh], hole=[wx, wz0, ww, wz1 - wz0],
        ex=[0, EB, EW + 55])
    ex_fin = [0, EB + 130, EW + 55]
    box("7l", "⑦", "縦の受け", S_UPPER, "ply", [fxl, D, Z + fz0, fxl + T, D + fd, Z + fz1], [0, R, 0], ex=ex_fin)
    box("7r", "⑦", "縦の受け", S_UPPER, "ply", [fxr, D, Z + fz0, fxr + T, D + fd, Z + fz1], [0, R, 0], ex=ex_fin)
    for i, zz in enumerate(((fz0 + wz0) / 2, (wz0 + wz1) / 2 - 80, (wz1 + fz1) / 2)):
        for side, x0 in (("l", fxl - 25), ("r", fxr + T)):
            box(f"L{side}{i}", "", "L 金具", S_UPPER, "metal", [x0, D, Z + zz - 12, x0 + 25, D + 25, Z + zz + 12], [0, R, 0],
                dims="30〜40mm", where="縦の受け⑦を背板の裏に留める", arrow=False, ex=ex_fin)
    ex_wl, ex_wr = [-170, 0, e_up], [170, 0, e_up]
    box("6l", "⑥", "袖板", S_UPPER, "ply", [0, wy0, Z, T, D - T, Z + bh], [-R, 0, 0], ex=ex_wl)
    box("6r", "⑥", "袖板", S_UPPER, "ply", [W - T, wy0, Z, W, D - T, Z + bh], [R, 0, 0], ex=ex_wr)
    hz, hy0, hy1 = Z + 355, wy0 + 40, wy0 + 110                   # 取っ手（袖板の外側）
    for i, (xo, sg, exw) in enumerate(((0.0, -1, ex_wl), (float(W), 1, ex_wr))):
        add(f"hgrip{i}", "", "取っ手", S_UPPER, "metal", "rod", [sg * 120, 0, 0], a=[xo + sg * 26, hy0, hz], b=[xo + sg * 26, hy1, hz], r=5,
            sub=i > 0, arrow=False, dims="長さ 100 くらい", where="上の枠を持ち上げる所。縦の受け⑦はつかまない", ex=exw)
        for k, yy in enumerate((hy0, hy1)):
            add(f"hpost{i}{k}", "", "取っ手", S_UPPER, "metal", "rod", [sg * 120, 0, 0], a=[xo, yy, hz], b=[xo + sg * 26, yy, hz], r=4,
                sub=True, arrow=False, ex=exw)

    # 6 上の枠を溝に差し込み、パチン錠で留める（工具なし。木口に金具を打たない）
    for i, xl in enumerate(L["latch_x"]):
        kw_l = dict(sub=i > 0, arrow=False, ex=[0, EB, EW + 55])
        box(f"latchA{i}", "", "パチン錠", S_JOIN, "metal", [xl - 14, D, Z + 30, xl + 14, D + 4, Z + 58], [0, 80, 0],
            dims="長さ 70 くらい", where="本体を背板の裏、受けを後ろ板⑬の裏に付ける。レバーを起こすと外れる", **kw_l)
        box(f"latchL{i}", "", "パチン錠", S_JOIN, "metal", [xl - 9, D + 4, Z - 34, xl + 9, D + 10, Z + 36], [0, 80, 0],
            sub=True, arrow=False, ex=[0, EB, EW + 55], where="レバー")
        box(f"latchB{i}", "", "パチン錠", S_JOIN, "metal", [xl - 12, D, Z - 40, xl + 12, D + 4, Z - 26], [0, 80, 0],
            sub=True, arrow=False, ex=[0, 170, EW], where="受け（後ろ板⑬の裏）")

    # 7 下の受け木
    mx, mz = L["mon_x"], L["mon_z"]
    box("8", "⑧", "下の受け木", S_LEDGE, "ply", [mx - g, D, Z + mz - T, mx + P["mon_w"] + g, D + fd, Z + mz], [0, R, 0],
        ex=[0, EB + 130, EW])

    # 8 モニター（後ろからはめる）
    ex_mon = [0, EB + 280, EW + 55]
    box("mon", "", "モニター 27インチ", S_MON, "monitor", [mx, D, Z + mz, mx + P["mon_w"], D + P["mon_t"], Z + mz + P["mon_h"]],
        [0, 450, 0], round=6, dims=f"{P['mon_w']} × {P['mon_h']} × {P['mon_t']}", where="背板の裏から窓にはめる。下の受け木⑧にのせる",
        ex=ex_mon)
    vx = L["view_x"]
    add("scr", "", "画面", S_MON, "screen", "screen", [0, 450, 0],
        box=[vx, D - 0.5, Z + P["view_z"], vx + P["view_w"], D, Z + P["view_z"] + P["view_h"]], sub=True, arrow=False, ex=ex_mon)

    # 9 押さえ桟
    ex_bar = [0, EB + 410, EW + 55]
    box("10", "⑩", "押さえ桟", S_BAR, "ply", [mx - g, D + fd - T, Z + L["bar_z"], mx + P["mon_w"] + g, D + fd, Z + L["bar_z"] + P["bar_w"]],
        [0, R, 0], ex=ex_bar)
    zbm = Z + L["bar_z"] + P["bar_w"] / 2
    for i, (xc, sg) in enumerate(((fxl + T / 2, 1), (fxr + T / 2, -1))):   # 縦の受けの後ろの木口の真ん中と、押さえ桟の向き
        xb = xc + sg * (T / 2 + 16)
        box(f"turn{i}", "", "回転留め", S_BAR, "metal", [min(xc, xb) - 4, D + fd, zbm - 6, max(xc, xb) + 4, D + fd + 3, zbm + 6], [0, R, 0],
            sub=i > 0, arrow=False, dims="長さ 30 くらい", where="縦の受けの後ろの木口にネジ 1 本で付ける。回して押さえ桟を押さえる", ex=ex_fin)
        add(f"turnS{i}", "", "回転留め", S_BAR, "chrome", "rod", [0, R, 0], a=[xc, D + fd + 3, zbm], b=[xc, D + fd + 5, zbm], r=2.5,
            sub=True, arrow=False, ex=ex_fin)

    # カメラが狙う点（机の上面から）
    aim = {c["name"]: [float(c["aim"][0]), float(c["aim"][1]), float(c["aim"][2] + base)] for c in cams}
    pos = {c["name"]: [float(c["pos"][0]), float(c["pos"][1]), float(c["pos"][2] + base)] for c in cams}

    # 10 中央のカメラ（カメラに付いているクリップで背板①の上の縁に挟み、下に傾ける）
    cx = L["cam_x"]
    clip = center_cam_clip(L)
    ks = clip["socket"]
    ex_camc = [0, EB, EW + 200]
    for id_, bb in (("cliptop", [cx - 26, D - T - 8, Z + bh, cx + 26, D + 8, Z + bh + 7]),
                    ("clipfront", [cx - 24, D - T - 8, Z + bh - 14, cx + 24, D - T, Z + bh]),
                    ("clipback", [cx - 22, D, Z + bh - 70, cx + 22, D + 7, Z + bh])):
        add(id_, "", "カメラのクリップ", S_CAMC, "clip", "box", [0, 0, R], box=bb, sub=True, arrow=False, ex=ex_camc)
    add("hinge", "", "カメラのクリップ", S_CAMC, "clip", "rod", [0, 0, R], a=[cx, D - T / 2, Z + bh + 7], b=[ks[0], ks[1], Z + ks[2]], r=6,
        sub=True, arrow=False, ex=ex_camc)
    add("camc", "", "中央カメラ（Anker C200）", S_CAMC, "camera", "cam", [0, 0, R], c=pos["中央"], aim=aim["中央"],
        body=list(CAM_BODY["中央"]), lens=11, tone="dark", dims=f"画角 {P['fov_center']}°",
        where=f"カメラに付いているクリップで背板①の上の縁に挟む。下に約 {clip['tilt']:.0f}° 傾けて、まな板とコンロの真ん中を向ける",
        ex=ex_camc)

    # 11〜14 左右のカメラ（アーム台⑫にクランプしたスマホアームスタンドの先。ホルダーで Brio をじかに挟む）
    mw, md = P["mount_w"], P["mount_d"]
    bar, gap = ARM["bar"], ARM["gap"]
    zb = np.array([0.0, 0.0, base])          # 天板の上面から → 机の上面から
    zu = np.array([0.0, 0.0, 1.0])
    cams_by = {c["name"]: c for c in cams}
    views = {}

    def p3(v):
        return np.asarray(v, float) + zb

    def obox(id_, no, name, step, kind, c, size, ax1, ax2, frm, **kw):
        """向きのある箱。c = 中心（天板の上面から）。size は ax1・ax2・ax1×ax2 の向きの長さ"""
        e1 = np.asarray(ax1, float)
        e1 = e1 / np.linalg.norm(e1)
        e2 = np.asarray(ax2, float)
        e2 = e2 - e1 * (e2 @ e1)
        e2 = e2 / np.linalg.norm(e2)
        e3 = np.cross(e1, e2)
        add(id_, no, name, step, kind, "obox", frm, c=p3(c), size=list(size),
            ax=[round(float(x), 4) for x in np.concatenate([e1, e2, e3])], **kw)

    def rod3(id_, name, step, kind, a, b, r, frm, **kw):
        add(id_, "", name, step, kind, "rod", frm, a=p3(a), b=p3(b), r=r, **kw)

    def tube(id_, name, step, kind, pts, r, frm, **kw):
        add(id_, "", name, step, kind, "tube", frm, pts=[[round(float(x), 1) for x in p3(p)] for p in pts], r=r, **kw)

    def spring(id_, step, p, q, rad, turns, frm, ex):
        p, q = np.asarray(p, float), np.asarray(q, float)
        ax_ = (q - p) / np.linalg.norm(q - p)
        e1 = np.cross(ax_, zu)
        e1 = e1 / np.linalg.norm(e1)
        e2 = np.cross(ax_, e1)
        n = int(turns * 12)
        pts = []
        for i in range(n + 1):
            t = i / n
            ang = 2 * math.pi * turns * t
            pts.append(p + (q - p) * t + rad * (math.cos(ang) * e1 + math.sin(ang) * e2))
        tube(id_, "アームのバネ", step, "chrome", pts, 0.9, frm, sub=True, arrow=False, ex=ex, seg=3, rs=5,
             where="アームの重さを支えるバネ")

    for side, sgn, cname in (("l", -1, "左"), ("r", 1, "右")):
        g = side_rig(cams_by[cname], sgn)
        out_v = np.array([float(sgn), 0.0, 0.0])          # 本体の外向き
        frm_m, frm_a = [sgn * R, 0, 0], [sgn * R, 0, 80]
        ex_m, ex_a = [sgn * 230, 0, e_up], [sgn * 300, 0, e_up + 60]
        B, E, tip, hd = g["B"], g["E"], g["tip"], g["hdir"]
        a_, r_, u_, rdir = g["a"], g["r"], g["u"], g["rod_dir"]
        px, py = g["px"], g["py"]
        nl = np.cross(hd, zu)                              # アームの面に垂直（横向き）
        if nl @ out_v < 0:
            nl = -nl
        s1, s2 = (E - B) / np.linalg.norm(E - B), (tip - E) / np.linalg.norm(tip - E)
        m1, m2 = np.cross(nl, s1), np.cross(nl, s2)       # アームの面の中で棒に垂直

        # 11 アーム台⑫（袖板の外側、天板の高さ）と、下の L 金具
        x0 = -mw if sgn < 0 else W
        box(f"12{side}", "⑫", "アーム台", S_MOUNT, "ply", [x0, D - md, Z - T, x0 + mw, D, Z], frm_m, ex=ex_m)
        for i, yy in enumerate((D - md + 28, D - md + 80)):
            xh0, xh1 = (-32, 0) if sgn < 0 else (W, W + 32)
            xv0, xv1 = (-3, 0) if sgn < 0 else (W, W + 3)
            box(f"MLh{side}{i}", "", "L 金具", S_MOUNT, "metal", [xh0, yy - 12, Z - T - 3, xh1, yy + 12, Z - T], frm_m, sub=True,
                arrow=False, dims="30〜40mm", where="アーム台⑫の下と、側板④の外側の面をつなぐ", ex=ex_m)
            box(f"MLv{side}{i}", "", "L 金具", S_MOUNT, "metal", [xv0, yy - 12, Z - T - 32, xv1, yy + 12, Z - T], frm_m, sub=True,
                arrow=False, dims="30〜40mm", where="アーム台⑫の下と、側板④の外側の面をつなぐ", ex=ex_m)

        # 12 アーム（クランプで台の後ろの縁を挟む）
        zt = 0                                             # アーム台の上面（天板の上面と同じ高さ）
        ct = zt + ARM["clamp_up"]
        box(f"cbody{side}", "", "スマホアームスタンド", S_ARM, "steel", [px - 16, D, Z + zt - 52, px + 16, D + 30, Z + ct], frm_a,
            dims="約 36cm × 2 段",
            where="クランプでアーム台⑫の後ろの縁を上下から挟み、下のねじを締める。机の天板用のクランプなので、縦の板には挟めない", ex=ex_a)
        kw_a = dict(sub=True, arrow=False, ex=ex_a)
        box(f"ctop{side}", "", "クランプ", S_ARM, "steel", [px - 20, D - 60, Z + zt, px + 20, D, Z + zt + 4], frm_a,
            where="アーム台⑫の上にのる板", **kw_a)
        box(f"clow{side}", "", "クランプ", S_ARM, "steel", [px - 14, D - 50, Z + zt - 52, px + 14, D, Z + zt - 40], frm_a,
            where="クランプの下のあご", **kw_a)
        sy_ = D - 35
        rod3(f"cscrew{side}", "クランプのねじ", S_ARM, "chrome", [px, sy_, zt - 92], [px, sy_, -3], 4, frm_a,
             where="下から回して締める", **kw_a)
        rod3(f"cpad{side}", "クランプのねじ", S_ARM, "chrome", [px, sy_, -3], [px, sy_, 0], 12, frm_a, where="台の裏に当たる丸い板", **kw_a)
        rod3(f"chandle{side}", "クランプのねじ", S_ARM, "chrome", [px - 32, sy_, zt - 92], [px + 32, sy_, zt - 92], 3, frm_a,
             where="ねじを回す取っ手", **kw_a)
        for k, xx in enumerate((px - 32, px + 32)):
            add(f"cball{side}{k}", "", "クランプのねじ", S_ARM, "chrome", "ball", frm_a, c=p3([xx, sy_, zt - 92]), r=6, **kw_a)
        rod3(f"cknobr{side}", "根元のつまみ", S_ARM, "chrome", [px, D + 30, zt - 8], [px, D + 42, zt - 8], 4, frm_a, **kw_a)
        box(f"cknob{side}", "", "根元のつまみ", S_ARM, "pblack", [px - 12, D + 42, Z + zt - 20, px + 12, D + 50, Z + zt + 4], frm_a,
            where="締めると、アームが左右に回らなくなる", **kw_a)
        rod3(f"post{side}", "根元の軸", S_ARM, "steel", [px, py, ct], [px, py, ct + ARM["post"]], 7, frm_a,
             where="ここを中心にアームが左右に回る", **kw_a)
        obox(f"bbase{side}", "", "アームの根元", S_ARM, "steel", B - zu * 12, [34, 38, bar + 10], hd, zu, frm_a, **kw_a)
        # 2 段のアーム（平行な 2 本の棒）
        for k, (q0, q1, s, m) in enumerate(((B, E, s1, m1), (E, tip, s2, m2))):
            ln = np.linalg.norm(q1 - q0)
            for j, o in enumerate((-gap / 2, gap / 2)):
                obox(f"bar{side}{k}{j}", "", "アーム（下の段）" if k == 0 else "アーム（上の段）", S_ARM, "steel",
                     (q0 + q1) / 2 + m * o, [ln + 12, bar, bar], s, m, frm_a,
                     where="2 本の棒とバネで、カメラの重さを支える", **kw_a)
        # 関節の板とボルト
        for k, (c, s, m, size) in enumerate(((E + s2 * 6, s2, m2, (52, 46, 3)), (tip - s2 * 8, s2, m2, (36, 30, 3)))):
            for j, o in enumerate((-1, 1)):
                obox(f"plate{side}{k}{j}", "", "アームの関節", S_ARM, "steel", c + nl * o * (bar / 2 + 2), size, s, m, frm_a,
                     where="ボルトで止まっている関節。きつさはボルトで調整", **kw_a)
        for k, c in enumerate((B, E, tip)):
            rod3(f"bolt{side}{k}", "アームの関節", S_ARM, "chrome", c - nl * (bar / 2 + 7), c + nl * (bar / 2 + 7), 3.5, frm_a, **kw_a)
        # バネ（下の段に 1 本、上の段に 2 本）
        spring(f"spl{side}", S_ARM, B + s1 * 28 - m1 * (gap / 2 + 9), B + s1 * (0.62 * ARM["L1"]) - m1 * (gap / 2 + 7), 4.2, 26, frm_a, ex_a)
        for j, o in enumerate((-5, 5)):
            spring(f"spu{side}{j}", S_ARM, E + s2 * 30 + m2 * (gap / 2 + 8) + nl * o, E + s2 * (0.5 * ARM["L2"]) + m2 * (gap / 2 + 6) + nl * o,
                   3.8, 24, frm_a, ex_a)
        # 先の軸・自由雲台・ホルダー（ホルダーで Brio の左右の端をじかに挟む）
        cw, chh, cd = CAM_BODY[cname]
        jaw, hh, ht = ARM["jaw"], ARM["holder_h"], ARM["holder_t"]
        rod3(f"tiprod{side}", "自由雲台", S_ARM, "chrome", tip, g["head_end"], 5, frm_a, where="アームの先の軸", **kw_a)
        rod3(f"head{side}", "自由雲台", S_ARM, "pblack", g["head_end"], g["ball"] - rdir * 4, 13, frm_a,
             where="つまみをゆるめると玉が回る。首を大きく倒すときは、切り欠き（溝）の向きに倒す", **kw_a)
        add(f"hball{side}", "", "自由雲台", S_ARM, "chrome", "ball", frm_a, c=p3(g["ball"]), r=8, **kw_a)
        rod3(f"hstud{side}", "自由雲台", S_ARM, "chrome", g["ball"], g["hold"] - a_ * (ht / 2), 3.5, frm_a, **kw_a)
        hm = (g["head_end"] + g["ball"]) / 2
        rod3(f"hknobr{side}", "自由雲台のつまみ", S_ARM, "chrome", hm + zu * 12, hm + zu * 22, 3, frm_a, **kw_a)
        obox(f"hknob{side}", "", "自由雲台のつまみ", S_ARM, "pblack", hm + zu * 26, [18, 8, 18], rdir, zu, frm_a, **kw_a)
        bc = g["body"]                                            # Brio 本体の中心
        jl = cd + ht - 3                                           # 爪の長さ（背板の後ろ → 本体の前の少し手前）
        obox(f"hback{side}", "", "スマホホルダー", S_ARM, "pblack", g["hold"], [cw + 2 * jaw + 6, hh, ht], r_, u_, frm_a,
             where="バネで左右の爪が開く。6〜10cm の物を挟める", **kw_a)
        for j, o in enumerate((-1, 1)):
            obox(f"hjaw{side}{j}", "", "スマホホルダー", S_ARM, "pblack",
                 g["hold"] - a_ * (ht / 2) + a_ * (jl / 2) + r_ * o * (cw / 2 + jaw / 2 + 0.5), [jaw, hh, jl], r_, u_, frm_a, **kw_a)
            obox(f"hlip{side}{j}", "", "スマホホルダー", S_ARM, "pblack", bc + a_ * (cd / 2 - 1) + r_ * o * (cw / 2 - 0.5),
                 [7, hh, 4], r_, u_, frm_a, **kw_a)

        # 13 カメラ（Brio をホルダーに前から差し込む。足は下に垂らしておく）
        frm_c = list(a_ * 150)
        add(f"cam{side}", "", f"{cname}のカメラ（Brio 100）", S_SCAM, "camera", "brio", frm_c, c=pos[cname], aim=aim[cname],
            body=list(CAM_BODY[cname]), off=BRIO_LENS_OFF, foot=list(BRIO_FOOT), dims=f"幅 73 × 高さ 32・画角 {P['fov_side']}°",
            where=f"足は下に垂らしたまま、ホルダーで左右の端を挟む。自由雲台の首で{'まな板' if side == 'l' else 'フライパン'}に向ける", ex=ex_a)

        # 14 ケーブル（カメラの背中 → ホルダーの下 → アームに沿わせる → 台の後ろ → 机の後ろ → PC）と面ファスナーのバンド
        so = nl * (bar / 2 + 6)                            # 棒の外側の面に沿わせる
        pts = [bc - a_ * (cd / 2 - 2) - u_ * (chh / 2 - 3) - r_ * 15, bc - a_ * (cd / 2 + 8) - u_ * (chh / 2 + 14) - r_ * 15,
               g["hold"] - a_ * 12 - u_ * (hh / 2 + 12),
               g["ball"] - zu * 18, g["head_end"] - zu * 14 + so]
        pts += [tip + (E - tip) * t_ + so for t_ in (0.06, 0.4, 0.75, 0.96)]
        pts += [E + (B - E) * t_ + so for t_ in (0.08, 0.5, 0.92)]
        pts += [np.array([px, D + 38, ct - 2]), np.array([px, D + 40, -40]), np.array([px - sgn * 30, D + 45, -146]),
                np.array([px - sgn * 45, D + 60, -158]), np.array([px - sgn * 55, D + 72, -330])]
        tube(f"cable{side}", "カメラのケーブル", S_CABLE, "usb", pts, 2.6, [0, 0, 80], arrow=False, ex=ex_a,
             where="カメラの背中からホルダーの下へ出し、アームに沿わせて後ろの PC へ。カメラのすぐ後ろは少したるませる")
        for k, (q0, q1, s, m, t) in enumerate(((tip, E, s2, m2, 0.35), (tip, E, s2, m2, 0.72), (E, B, s1, m1, 0.5))):
            obox(f"tie{side}{k}", "", "面ファスナーのバンド", S_CABLE, "velcro", q0 + (q1 - q0) * t + so * 0.45,
                 [12, gap + bar + 10, bar + 22], s, m, [0, 0, 80], arrow=False, ex=ex_a,
                 dims="幅 12mm くらい", where="ケーブルをアームの棒に巻いて留める")

        if side == "l":                                    # 寄って見るときの向き（左側で見せる）
            mid = (B + E + tip) / 3
            views = {
                "mount": {"target": list(p3([-mw / 2 - 20, D - md / 2, 0])), "off": [-720, -650, 430], "ex": ex_m},
                "arm": {"target": list(p3(mid + zu * 60 - out_v * -60)), "off": [-1500, -1150, 520], "ex": ex_a},
                "cam": {"target": list(p3((g["body"] + g["hold"]) / 2)),
                        "off": list(a_ * 440 + np.array([0.0, -250.0, 100.0])), "ex": ex_a},
                "cable": {"target": list(p3(mid - zu * 60)), "off": [-1000, 720, 380], "ex": ex_a},
            }
            arm_deg = (g["th1"], g["th2"])
            neck_deg = g["neck"]

    # 15 おもちゃを置く
    bx0, by0, bw, bd = P["board_x"], P["board_y"], P["board_w"], P["board_d"]
    box("board", "", "まな板（おもちゃ）", S_TOYS, "board", [bx0, by0, Z, bx0 + bw, by0 + bd, Z + P["board_t"]], [0, 0, R], round=8,
        dims=f"{bw} × {bd}（仮）", where="まな板のゾーン。カメラのキャリブレーションで 4 隅を登録する", ex=[0, 0, e_up])
    add("carrot", "", "にんじん（おもちゃ）", S_TOYS, "carrot", "carrot", [0, 0, R],
        c=[bx0 + bw / 2 - 10, by0 + bd / 2, Z + P["board_t"] + 20], len=150, r=20, gap=8, sub=True, arrow=False, ex=[0, 0, e_up + 70])
    pc = pan_center()
    add("pan", "", "フライパン（おもちゃ）", S_TOYS, "pan", "pan", [0, 0, R], c=[pc[0], pc[1], Z + T + 16], r=P["pan_r"], h=30,
        handle=[1.0, -0.4], dims=f"直径 {2 * P['pan_r']}（仮）", where=f"コンロの{'左右'[P['pan_on']]}のバーナーの上",
        ex=[0, 0, e_up + 130])
    mwx, mwy, mww, mwd, mwh = P["mw_x"], P["mw_y"], P["mw_w"], P["mw_d"], P["mw_h"]
    add("mw", "", "電子レンジ（おもちゃ）", S_TOYS, "mw", "microwave", [0, 0, R], box=[mwx, mwy, Z, mwx + mww, mwy + mwd, Z + mwh],
        dims=f"{mww} × {mwd} × {mwh}（仮）", where="天板の左奥に置く", ex=[0, 0, e_up])
    spx, spy, spw, spd, sph = P["spice_x"], P["spice_y"], P["spice_w"], P["spice_d"], P["spice_h"]
    for i in range(3):
        add(f"spice{i}", "", "調味料の容器", S_TOYS, "spice", "bottle", [0, 0, R], c=[spx + spw * (i + 0.5) / 3, spy + spd / 2, Z],
            r=24, h=sph, cap=i, sub=i != 1, arrow=i == 1, dims="おもちゃ（中に M5StickS3）", where="調味料の置き場", ex=[0, 0, e_up])
    add("plate", "", "皿", S_TOYS, "plate", "plate", [0, 0, R], c=[P["plate_x"] + P["plate_w"] / 2, P["plate_y"] + P["plate_d"] / 2, Z],
        r=72, dims="おもちゃ", where="できた料理を出す所", ex=[0, 0, e_up])

    # カメラの視野（天板の高さの面まで）
    frusta = []
    for c in cams:
        a, r, u = c["basis"]
        k = math.tan(math.radians(c["fov"]) / 2) / math.hypot(16, 9)
        corners = []
        for s1, s2 in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            d = a + r * (s1 * k * 16) + u * (s2 * k * 9)
            t = -c["pos"][2] / d[2] if d[2] < -1e-6 else 900 / np.linalg.norm(d)
            t = min(t, 1600 / np.linalg.norm(d))
            q = c["pos"] + d * t
            corners.append([round(float(q[0]), 1), round(float(q[1]), 1), round(float(q[2] + base), 1)])
        frusta.append({"name": c["name"], "pos": pos[c["name"]], "corners": corners})

    # 「3 つに分ける」: 下の台はそのまま、上の枠は真上へ（溝から抜ける向き）、モニターは後ろへ抜く、アームとカメラは横へ外す
    split_up, split_arm = 420, 330
    sp_mod = {"upper": [0, 0, split_up], "monitor": [0, 380, 950]}
    mod_of = {S_UPPER: "upper", S_JOIN: "upper", S_LEDGE: "upper", S_BAR: "upper", S_CAMC: "upper",
              S_MON: "monitor", S_ARM: "arm", S_SCAM: "arm", S_CABLE: "arm"}

    def rep_x(it):
        for k in ("c", "a"):
            if k in it:
                return it[k][0]
        if "box" in it:
            return (it["box"][0] + it["box"][3]) / 2
        if "pts" in it:
            return it["pts"][0][0]
        return W / 2

    for it in items:
        m = mod_of.get(it["step"])
        if m == "arm":
            it["split"] = [split_arm if rep_x(it) > W / 2 else -split_arm, 0, 80]
        elif m:
            it["split"] = sp_mod[m]
        elif it["step"] > 0:
            it["split"] = [0, 0, 0]

    def kg(nos):
        return sum(net_area(q) * q.t * q.qty for q in ps if q.no in nos) * 1e-9 * PLY_KG_M3

    modules = [
        {"text": f"下の台 約 {kg({'⑪a', '⑪b', '②', '④', '⑤', '⑬', '③', '⑨', '⑫'}):.0f} kg", "pos": [W / 2, -60, Z * 0.55]},
        {"text": f"上の枠 約 {kg({'①', '⑥', '⑦', '⑧', '⑩'}):.0f} kg", "pos": [W / 2, D, Z + bh + split_up + 70]},
        {"text": "モニター", "pos": [mx + P["mon_w"] + 90, D + 380 + P["mon_t"], Z + mz + 950 + P["mon_h"] / 2]},
        {"text": "アームとカメラ", "pos": [-split_arm - 60, 300, Z + 760]},
    ]
    for v in views.values():                                   # 寄って見る向きも、3 つに分けたときの動きに合わせる
        v["sp"] = [0, 0, 0] if v is views["mount"] else [-split_arm, 0, 80]

    steps = [
        {"title": "完成形", "view": "iso",
         "text": f"机の上に置く卓上型のキッチン。天板は床から {counter_h()}（机 {P['desk_h']} のとき）。"
                 "下の台・上の枠・モニターの 3 つに分けてしまえる。「組み立てを始める」で 1 工程ずつ見られる。",
         "tips": ["板をタップすると、寸法と付く所が出る", "「分解」をオンにすると、板どうしのつながり方が見やすい",
                  "「3 つに分ける」をオンにすると、しまうときの分け方（下の台・上の枠・モニター）が見られる"]},
        {"title": "底板を置く", "view": "iso",
         "text": "底板⑪a・⑪b を置く。下の台の板（前板・側板・中仕切り・後ろ板）は、すべてこの上に立てる。土台の中の基板と配線もこの上に置く。",
         "tips": ["底板は合板 1 枚に背板と並ばないので、中仕切りの所で 2 枚に分けてある。継ぎ目は工程 2 の角材で受ける",
                  "運ぶときは、下の台ごと下から持つ", "机に置くときは、底板の下に滑り止めシートを敷く"]},
        {"title": "土台の枠を付ける", "view": "iso",
         "text": "底板の上に前板②・側板④ 2 枚・中仕切り⑤・土台の後ろ板⑬を立てて、土台の枠を作る。中仕切りの右の面に角材を付け、"
                 "底板 右⑪b の左の縁を留める。",
         "tips": ["前板は先にミントグリーンに塗り、つまみの目盛り（切・弱・中・強）を描いておく",
                  "底板はボンドを使わずビスだけで留めると、あとで外して中を直せる"]},
        {"title": "つまみと基板", "view": "top",
         "text": "つまみの部品を前板の裏から差し込み、表からナットで締めてつまみを付ける。右の部屋の底板に基板（ESP32）を留めて配線し、"
                 "USB ケーブルを土台の後ろ板⑬の穴から外に出す。",
         "tips": ["天板をのせると中に手が入らない。配線はここで済ませる", f"土台の中の高さは {wall_h}（底板の上から天板の下まで）",
                  "左の部屋は空き。電子レンジにスイッチを付けるときだけ線を通す"]},
        {"title": "天板とコンロ板をのせる", "view": "iso",
         "text": "コンロ板⑨を天板③の上の位置に合わせ、天板の裏からビスで留める。天板を土台にのせ、上から前板・側板・中仕切り・後ろ板へビス。"
                 "背板を差し込む溝の角材を、天板の上と後ろ板の裏に付ける。ここまでで「下の台」ができる。",
         "tips": [f"溝の幅は背板の厚さと同じ {T}。前の角材は袖板の間いっぱいに付けるので、上の枠が左右にもずれない",
                  f"後ろの角材は 3 つに分け、天板より {L['guide'][1]} 上に出す。間にパチン錠の受けを付ける",
                  f"バーナー（直径 {2 * P['burner_r']}）と五徳は、おもちゃの部品か 3D プリントで作る"]},
        {"title": "上の枠を組む", "view": "backiso",
         "text": "背板①は窓をくり抜いた 1 枚板。裏に縦の受け⑦を L 金具で留め、左右の端に袖板⑥を付ける。袖板の外側に取っ手を付ける。"
                 "これが「上の枠」で、下の台とは別の 1 つの部品になる。",
         "tips": [f"窓の大きさは {L['win_w']} × {L['win_h']}。ジグソーで線の少し内側を切り、ヤスリで線まで仕上げる",
                  "くり抜いた板から、コンロ板⑨とアーム台⑫ 2 枚を取る", "継ぎ目がないので、運ぶときに割れにくい",
                  "持ち上げるときは取っ手を持つ。縦の受け⑦はつかまない"]},
        {"title": "上の枠を差し込んで留める", "view": "backiso",
         "text": "上の枠を真上から下ろし、背板の下の縁を天板の後ろの溝に差し込む。背板の裏と後ろ板⑬の裏をまたぐパチン錠 2 個を掛ける。",
         "tips": ["工具はいらない。外すときはパチン錠のレバーを起こして、取っ手を持って真上に抜く",
                  "溝で毎回同じ位置に戻るので、中央カメラの位置もずれにくい",
                  "金具は板の面にネジで付けるだけで、木口には何も打たない（薄い板が割れないように）"]},
        {"title": "下の受け木", "view": "backiso",
         "text": "下の受け木⑧を縦の受けの間にはさみ、縦の受けの外側からビス 2 本ずつで留める。モニターはこの上にのる。",
         "tips": []},
        {"title": "モニターを後ろからはめる", "view": "backiso",
         "text": "後ろから、モニターを下の受け木⑧にのせ、画面を窓に押し当てる。前から見て、画面が窓の真ん中に来ているか確かめる。",
         "tips": ["モニターのケーブルは背板の後ろから、そのまま本体の後ろの PC へ"]},
        {"title": "押さえ桟で留める", "view": "backiso",
         "text": "モニターの背面に薄いスポンジを貼り、押さえ桟⑩を縦の受けの間の後ろの端にはさむ。縦の受けの後ろの木口に付けた回転留めを回して押さえる。",
         "tips": ["工具はいらない。しまうときは回転留めを戻して押さえ桟を外し、モニターを後ろに抜く",
                  "回転留めはスポンジを押す分の力しかかからないので、ネジ 1 本で足りる"]},
        {"title": "中央のカメラ", "view": "iso",
         "text": "中央カメラに付いているクリップを、背板①の上の縁に挟む。カメラを下に傾けて、まな板とコンロの真ん中を向ける。",
         "tips": [f"カメラは水平から下に約 {clip['tilt']:.0f}° 向ける。クリップの角度が足りないときは、小さな自由雲台を足す",
                  f"中央カメラの画角は {P['fov_center']}° にする"]},
        {"title": "アーム台を付ける", "view": views["mount"],
         "text": "側板④の外側に、上の面を天板にそろえてアーム台⑫を横向きに付ける。左右のカメラのアームは、この台の後ろの縁にクランプで挟む。",
         "tips": ["アームのクランプは机の天板を上下から挟む形なので、縦の板には挟めない。だから横向きの台を足す",
                  "台は下の台の側板に L 金具 2 個で留める。上の枠とはつながないので、上の枠を外しても台は残る"]},
        {"title": "アームを挟む", "view": views["arm"],
         "text": "スマホアームスタンドのクランプで、アーム台⑫の後ろの縁を上下から挟み、下のねじを締める。下の段を立て、上の段を前に出して、"
                 "先のホルダーをカメラの位置まで持っていく。",
         "tips": [f"アームは 2 段で、1 段 約 36cm。この位置だと、下の段は水平から約 {arm_deg[0]:.0f}°、上の段は約 {arm_deg[1]:.0f}°",
                  "根元のつまみを締めると、アームが左右に回らなくなる", "右側も同じ形で、左右対称"]},
        {"title": "カメラをホルダーで挟む", "view": views["cam"],
         "text": "Brio の足（モニターに掛ける部分）は下に垂らしたまま、カメラの左右の端をホルダーで挟む。ホルダーの背板は本体の背中に当てる。レンズは端に近いので、爪がかからない深さで挟む。"
                 "自由雲台のつまみをゆるめて、左はまな板、右はフライパンに向けてから締める。",
         "tips": ["Brio 100 の横幅は仕様で約 73mm。ホルダーは 6〜10cm を挟めるので、長い辺を左右から挟む。挟む前に手元のカメラで測る",
                  f"アームの先の軸は前へ斜め下に出るだけで、横には向かない。カメラを下に向けるのは自由雲台の首で、約 {neck_deg:.0f}° 倒す。"
                  "大きく倒すときは、首を切り欠き（溝）の向きに倒す",
                  "ケーブルはホルダーの下から出す。背板や爪で押しつぶさない",
                  "「カメラの視野」をオンにすると、どこが映るかがわかる"]},
        {"title": "ケーブルを留める", "view": views["cable"],
         "text": "カメラの背中から出るケーブルを、ホルダーの下からアームの棒に沿わせ、面ファスナーのバンドで留める。根元から台の後ろへ下ろし、"
                 "机の後ろの PC へつなぐ。",
         "tips": ["カメラのすぐ後ろは少したるませる。ピンと張ると、ケーブルに引っぱられてカメラの向きが変わる",
                  "関節の所は、アームを動かしても突っぱらないように少し余らせる", "付属のケーブルで届かないときは、USB の延長ケーブルを足す"]},
        {"title": "おもちゃを置いて完成", "view": "iso",
         "text": "まな板・フライパン・電子レンジ・調味料・皿を置いて完成。キッチンの前の縁を机の前の縁にそろえる。",
         "tips": ["まな板やフライパンの位置は、カメラのゾーン（キャリブレーション）と合わせる"]},
    ]
    assert len(steps) == S_TOYS + 1, "工程の数と steps の数が合わない"
    dims = {"W": P["W"], "D": P["D"], "H": P["base_h"] + P["back_h"], "depth_total": round(L["depth_total"]),
            "counter": counter_h(), "desk_h": P["desk_h"]}
    front = {"w": W, "h": wall_h, "knobs": [float(k) for k in kxs]}
    return {"dims": dims, "front": front, "items": items, "steps": steps, "cams": frusta, "modules": modules}


def write_3d(L, cams, ps):
    tpl = (ROOT / "tools" / "kitchen_3d_template.html").read_text(encoding="utf-8")
    data = json.dumps(model3d(L, cams, ps), ensure_ascii=False, separators=(",", ":"))
    out = ROOT / "docs" / "kitchen_3d.html"
    out.write_text(tpl.replace("/*__DATA__*/null", data), encoding="utf-8")
    return out


def update_doc(sections):
    if not DOC.exists():
        print("設計書が無いので表の書き込みは飛ばす:", DOC)
        return
    text = DOC.read_text(encoding="utf-8")
    for name, body in sections.items():
        pat = re.compile(rf"(<!-- auto:{name} -->\n).*?(<!-- /auto:{name} -->)", re.S)
        text, n = pat.subn(lambda m: m.group(1) + body + "\n" + m.group(2), text)
        if n == 0:
            print(f"設計書に auto:{name} の印が無いので飛ばした")
    DOC.write_text(text, encoding="utf-8")
    print("更新:", DOC)


def main():
    setup_font()
    IMG.mkdir(parents=True, exist_ok=True)
    L = layout()
    cams, vis = analyze(L)
    ps = parts(L)
    sheets = sheet_layout(ps)
    warnings = check(L, cams, vis, sheets, ps)
    notes = coverage_lines(cams, vis) + ["注意: " + m for m in warnings]
    draw_overview(L, cams, vis, notes)
    draw_parts(ps, L)
    draw_cutlayout(sheets, ps)
    out3d = write_3d(L, cams, ps)
    update_doc({"dims": md_dims(L, ps, warnings), "cutlist": md_cutlist(ps, sheets), "camera": md_camera(L, cams, vis)})
    for line in notes:
        print(line)
    print("保存:", IMG / "kitchen_plan.png", IMG / "kitchen_parts.png", IMG / "kitchen_cutlayout.png", out3d)


if __name__ == "__main__":
    main()
