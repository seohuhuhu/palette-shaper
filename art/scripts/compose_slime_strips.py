#!/usr/bin/env python3
"""검사를 통과한 슬라임 포즈(art/raw/slime_ref/)를 조합해 애니메이션 스트립 원본을 만든다.

새 그림을 그리지 않는다. 포즈를 옮기고(높이), 몸통 한 줄을 빼고(1px 숨쉬기), 꼭지를 1px 들고,
기준 그림의 물방울 조각을 붙이는 것만 한다 → 생김새와 눈 크기가 프레임마다 흔들리지 않는다.

출력은 24x24 칸에 이미 피벗(발바닥 중앙 x=12) 기준으로 놓인 원본 스트립이다.
반드시 process_pixel_art.py --align none 으로 후처리·검사를 거친다.

  Idle 4F : 정면 몸통 정수리에 위로 선 물방울 꼭지, 1px 숨쉬기
  Run  6F : 옆 → 점프 준비(숙임) → 1px 떠서 전진 + 뒤로 물방울 → 최고점 2px → 하강 1px → 착지(옆)
  Jump 2F : 점프 준비 → 늘어남(급상승)
  Fall 2F : 공중 체공(jump) → 옆 모습에서 꼭지가 1px 들린 하강
  Land 3F : 눌림 A(최대, 1x1 눈) → 정면보다 1px 낮은 회복(숨쉬기) → 정면

사용: python art/scripts/compose_slime_strips.py --raw-dir art/raw/slime_ref --out-dir art/raw/slime_strips
"""
import argparse
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pixelize_reference as PX  # noqa: E402

FW = FH = 24
PIVOT_X = 12          # 발바닥 중앙
GROUND = 21           # 몸통 맨 아래 행 (22=바깥 외곽선, 23=아랫선 2px째)
EYE = (0x18, 0x14, 0x17)


class ComposeFail(Exception):
    pass


def load(raw_dir, name):
    return np.array(Image.open(os.path.join(raw_dir, name + ".png")).convert("RGBA"))


def eye_rows(img):
    ys, _ = np.nonzero(np.all(img[..., :3] == EYE, -1) & (img[..., 3] == 255))
    return (ys.min(), ys.max()) if len(ys) else (None, None)


def breathe(img):
    """눈 바로 아래 몸통 한 줄을 빼서 키를 1px 줄인다(발바닥 기준이면 머리가 1px 내려옴). 눈은 건드리지 않음."""
    e0, e1 = eye_rows(img)
    h = img.shape[0]
    r = e1 + 1 if e1 is not None and e1 + 1 < h - 2 else h // 2
    return np.concatenate([img[:r], img[r + 1:]], axis=0)


def lift_tip(img, ratio=0.6):
    """꼭지(몸통 위쪽의 좁은 부분)를 1px 위로 든다: 꼭지 밑동 한 줄을 복제해 끼운다. 눈은 그대로."""
    op = img[..., 3] == 255
    widths = op.sum(1)
    t = int(np.argmax(widths >= ratio * widths.max()))
    t = max(t, 1)
    e0, _ = eye_rows(img)
    if e0 is not None and t > e0:
        t = e0
    return np.concatenate([img[:t], img[t - 1:t], img[t:]], axis=0)


def widen_tip(drop):
    """물방울 꼭지를 Run 꼭지만큼 통통하게: 위 2줄은 3px 폭, 맨 아래(목)는 1px."""
    h, w = drop.shape[:2]
    op = drop[..., 3] == 255
    col = drop[op][0].copy()
    out = np.zeros((h, 3, 4), np.uint8)
    for y in range(h):
        row = drop[y][op[y]]
        c = row[0] if len(row) else col
        if y < h - 1:
            out[y, :] = c
        else:
            out[y, 1] = c
    return out


def round_tip(drop):
    """덩어리감을 더 키운 꼭지: 4px 폭 둥근 물방울(.##. / #### / .##.). 목(맨 아랫줄)은 2px."""
    op = drop[..., 3] == 255
    rows = [drop[y][op[y]] for y in range(drop.shape[0])]
    cols = [r[0] if len(r) else drop[op][0] for r in rows]
    shape = [".##.", "####", ".##."]
    out = np.zeros((3, 4, 4), np.uint8)
    for y, line in enumerate(shape):
        c = cols[min(y, len(cols) - 1)]
        for x, ch in enumerate(line):
            if ch == "#":
                out[y, x] = c
    return out


def attach_tip(body, tip):
    """몸통 정수리(맨 윗줄 가운데)에 꼭지를 세워 붙인 새 이미지. 꼭지 맨 아랫줄이 정수리 바로 위에 닿는다."""
    bh, bw = body.shape[:2]
    th, tw = tip.shape[:2]
    top = np.nonzero(body[0, :, 3] == 255)[0]
    cx = (top.min() + top.max()) / 2
    w = max(bw, tw)
    out = np.zeros((bh + th, w, 4), np.uint8)
    ox = (w - bw) // 2
    out[th:, ox:ox + bw] = body
    tb = np.nonzero(tip[-1, :, 3] == 255)[0]          # 꼭지 맨 아랫줄(목)의 중심을 정수리 중심에 맞춤
    tx = int(np.floor(ox + cx - (tb.min() + tb.max()) / 2 + 0.5))
    m = tip[..., 3] == 255
    out[:th, tx:tx + tw][m] = tip[m]
    return out


def place(canvas, img, dx=0, dy=0, x_center=PIVOT_X, bottom=GROUND):
    """img를 발바닥 중앙(x_center, bottom) 기준으로 놓는다. dy>0이면 위로 뜸."""
    h, w = img.shape[:2]
    x0 = x_center - w // 2 + dx
    y0 = bottom - h + 1 - dy
    if x0 < 1 or y0 < 1 or x0 + w > FW - 1 or y0 + h > FH - 2:
        raise ComposeFail(f"배치가 칸(외곽선 여백 포함)을 벗어남: {w}x{h} at ({x0},{y0})")
    m = img[..., 3] == 255
    canvas[y0:y0 + h, x0:x0 + w][m] = img[m]
    return x0, y0, w, h


def frame(*items):
    c = np.zeros((FH, FW, 4), np.uint8)
    boxes = []
    for img, kw in items:
        boxes.append(place(c, img, **kw))
    return c, boxes


def droplet_from_reference(ref_path, box, scale):
    """기준 그림에서 작은 물방울 조각(박스 안 가장 큰 덩어리)을 같은 배율로 줄인다."""
    im = np.array(Image.open(ref_path).convert("RGB")).astype(float)
    x0, y0, x1, y1 = box
    crop = im[y0:y1, x0:x1]
    fg = crop.mean(-1) > 90
    lab, n = PX.label(fg)
    if n == 0:
        raise ComposeFail("물방울 조각을 찾지 못함")
    sizes = [(lab == i).sum() for i in range(1, n + 1)]
    m = lab == (int(np.argmax(sizes)) + 1)
    ys, xs = np.nonzero(m)
    m = m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    rgb = crop[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h, w = m.shape
    W, H = max(1, round(w / scale)), max(2, round(h / scale))
    out = np.zeros((H, W, 4), np.uint8)
    for j in range(H):
        for k in range(W):
            mm = m[int(j * h / H):int((j + 1) * h / H), int(k * w / W):int((k + 1) * w / W)]
            if mm.mean() >= 0.4:
                c = rgb[int(j * h / H):int((j + 1) * h / H), int(k * w / W):int((k + 1) * w / W)][mm]
                out[j, k, :3] = np.median(c, 0)
                out[j, k, 3] = 255
    if not (out[..., 3] == 255).any():
        raise ComposeFail("물방울 조각이 축소 후 사라짐")
    return out


def particle(body):
    """달리기 때 뒤로 튀는 작은 물방울(가로 2px). 색은 몸통 중간 톤."""
    op = body[..., 3] == 255
    col = np.median(body[op & ~np.all(body[..., :3] == EYE, -1)][:, :3], 0).astype(np.uint8)
    p = np.zeros((1, 2, 4), np.uint8)
    p[..., :3] = col
    p[..., 3] = 255
    return p


def check_eyes(name, frames, expect):
    """프레임마다 눈 덩어리 수와 크기가 기대값과 같은지(눈 크기 일관성)."""
    report = []
    for i, f in enumerate(frames):
        eye = np.all(f[..., :3] == EYE, -1) & (f[..., 3] == 255)
        lab, n = PX.label(eye)
        shapes = []
        for k in range(1, n + 1):
            ys, xs = np.nonzero(lab == k)
            shapes.append(f"{np.ptp(xs) + 1}x{np.ptp(ys) + 1}")
        want = expect[i]
        ok = sorted(shapes) == sorted([want] * 2)
        report.append({"frame": i, "eyes": shapes, "expect": [want] * 2, "pass": ok})
        if not ok:
            raise ComposeFail(f"{name} frame {i}: 눈 {shapes} ≠ 기대 {[want] * 2}")
    return report


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--raw-dir", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--reference", default=os.path.join(os.path.dirname(HERE), "reference", "slime_reference.png"))
    ap.add_argument("--drop-box", default="105,85,150,122", help="기준 그림에서 정면 슬라임 머리 위 물방울이 있는 박스 x0,y0,x1,y1")
    ap.add_argument("--scale", type=float, default=8.5)
    ap.add_argument("--idle-tip", choices=["drop", "drop_wide", "drop_round"], default="drop_wide",
                    help="Idle·Land 정면 꼭지(위로 섬): drop=기준 그림 물방울 2px / drop_wide=3px 통통(기본, 2026-11-20 사용자 선택) / drop_round=4px 둥근 덩어리")
    args = ap.parse_args(argv)

    P = {n: load(args.raw_dir, n) for n in ("idle_front", "idle_side", "hop", "jump", "stretch", "squash_a")}
    drop = droplet_from_reference(args.reference, tuple(int(v) for v in args.drop_box.split(",")), args.scale)
    side = P["idle_side"]
    # Idle/Land 정면 꼭지: 정수리에 '위로 서 있는' 물방울 꼭지 (Run처럼 눕지 않아야 Idle과 구분됨 — 2026-11-20 사용자)
    tip = {"drop": drop, "drop_wide": widen_tip(drop), "drop_round": round_tip(drop)}[args.idle_tip]
    front = attach_tip(P["idle_front"], tip)
    front_b = breathe(front)
    side_lift = lift_tip(side)
    pt = particle(side)

    strips = {
        # Idle: 다른 포즈처럼 꼭지(기준 그림의 물방울 조각)를 정수리에 바로 붙인다 (2026-11-20 사용자 결정).
        # 몸통 1px 숨쉬기만: 머리+꼭지 0,0,+1,+1
        "Idle": [
            frame((front, {})),
            frame((front, {})),
            frame((front_b, {})),
            frame((front_b, {})),
        ],
        "Run": [
            frame((side, {})),
            frame((P["hop"], {})),
            frame((P["hop"], dict(dy=1, dx=1)), (pt, dict(dx=-9, dy=1))),
            frame((side, dict(dy=2, dx=1)), (pt, dict(dx=-10, dy=3))),
            frame((side, dict(dy=1))),
            frame((side, {})),
        ],
        "Jump": [
            frame((P["hop"], {})),
            frame((P["stretch"], {})),
        ],
        "Fall": [
            frame((P["jump"], {})),
            frame((side_lift, {})),
        ],
        # Land 2·3번(정면)도 Idle과 같은 꼭지를 붙여야 착지 → Idle 전환 때 꼭지가 갑자기 생기지 않는다
        "Land": [
            frame((P["squash_a"], {})),
            frame((front_b, {})),
            frame((front, {})),
        ],
    }
    for i, (f, _) in enumerate(strips["Idle"]):
        lab, n = PX.label(f[..., 3] == 255)
        if n != 1:
            raise ComposeFail(f"Idle frame {i}: 덩어리 {n}개 — 꼭지가 몸통에 붙어 있지 않음")
    eye_expect = {"Idle": ["1x2"] * 4, "Run": ["1x2"] * 6, "Jump": ["1x2"] * 2, "Fall": ["1x2"] * 2,
                  "Land": ["1x1", "1x2", "1x2"]}
    os.makedirs(args.out_dir, exist_ok=True)
    meta = {}
    for name, fr in strips.items():
        imgs = [f for f, _ in fr]
        eyes = check_eyes(name, imgs, eye_expect[name])
        strip = np.concatenate(imgs, axis=1)
        path = os.path.join(args.out_dir, f"slime_{name.lower()}_strip{len(imgs)}.png")
        Image.fromarray(strip, "RGBA").save(path)
        meta[name] = {"path": path, "frames": len(imgs), "eyes": eyes}
        print(f"{name}: {len(imgs)}프레임, 눈 {[e['eyes'] for e in eyes]} → {path}")
    with open(os.path.join(args.out_dir, "compose.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)


if __name__ == "__main__":
    try:
        main()
    except ComposeFail as e:
        print(f"FAIL: {e}")
        sys.exit(1)
