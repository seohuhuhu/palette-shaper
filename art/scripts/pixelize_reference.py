#!/usr/bin/env python3
"""참고 그림(고해상도 일러스트) → 도트 원본(art/raw/) 변환.

PixelLab이 참고 이미지를 받지 못해 모양이 달라지는 문제를 피하려고, 사용자가 준 그림 자체를 줄여 원본으로 쓴다.
결과는 '원본(raw)'일 뿐이다. 반드시 process_pixel_art.py(Step 0~4)를 거쳐야 한다.

  1) 배경(어두운 단색)과 분리된 포즈 덩어리를 찾는다 (면적 --min-area 이상). 떨어져 있는 작은 조각(물방울·먼지)은 버린다.
  2) 덩어리 안쪽 구멍(눈 등)은 몸통에 포함한다.
  3) 모든 포즈에 같은 배율 1/--scale 로 면적 평균 축소한다. 칸의 절반 이상이 몸통이면 불투명.
     칸 안에 어두운 픽셀(눈)이 25% 이상이면 어두운 색, 아니면 밝은 픽셀의 중앙값.

사용:
  python art/scripts/pixelize_reference.py --input art/reference/slime_reference.png --scale 8.5 --out-dir art/raw/slime_ref
"""
import argparse
import json
import os

import numpy as np
from PIL import Image


def label(mask):
    """4방향 연결 성분 라벨링 (scipy 없이)."""
    h, w = mask.shape
    lab = np.zeros((h, w), dtype=np.int32)
    n = 0
    for y in range(h):
        for x in range(w):
            if mask[y, x] and not lab[y, x]:
                n += 1
                stack = [(y, x)]
                lab[y, x] = n
                while stack:
                    cy, cx = stack.pop()
                    for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not lab[ny, nx]:
                            lab[ny, nx] = n
                            stack.append((ny, nx))
    return lab, n


def fill_holes(m):
    """테두리와 이어지지 않은 빈 칸(구멍)을 채운다."""
    outside = ~m
    lab, _ = label(outside)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]])))
    border.discard(0)
    keep_out = np.isin(lab, list(border))
    return ~keep_out


def find_eyes(rgb, body, eye_threshold, min_area=20, max_area=400, max_aspect=3.0):
    """몸통 안의 '아주 어두운' 둥근 덩어리(눈) 중심 (x, y) 목록 — 원본 좌표.
    음영 선(가늘고 긴 중간 어둠)이나 가장자리 테두리는 밝기·면적·가로세로비로 걸러낸다."""
    dark = (rgb.mean(-1) < eye_threshold) & body
    lab, n = label(dark)
    eyes = []
    for i in range(1, n + 1):
        ys, xs = np.nonzero(lab == i)
        if not (min_area <= len(ys) <= max_area):
            continue
        w, h = np.ptp(xs) + 1, np.ptp(ys) + 1
        if max(w, h) / min(w, h) > max_aspect:
            continue
        cy, cx = ys.mean(), xs.mean()
        y0, y1 = max(0, int(cy) - 12), int(cy) + 12
        x0, x1 = max(0, int(cx) - 12), int(cx) + 12
        if body[y0:y1, x0:x1].mean() > 0.6:   # 몸통에 둘러싸인 것만 (바닥 그림자 등 제외)
            eyes.append((cx, cy))
    return sorted(eyes)


def clamp_inside(op, ex, ey, ew, eh, max_shift=3):
    """눈 칸 전부가 몸통 안쪽(4방향 이웃도 몸통)이 되도록 최대 3px 안쪽으로 옮긴다. 못 찾으면 (None, None)."""
    H, W = op.shape
    pad = np.pad(op, 1)
    interior = op & pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:]
    best = None
    for dy in range(-max_shift, max_shift + 1):
        for dx in range(-max_shift, max_shift + 1):
            x, y = ex + dx, ey + dy
            if x < 0 or y < 0 or x + ew > W or y + eh > H:
                continue
            if interior[y:y + eh, x:x + ew].all():
                d = abs(dx) + abs(dy)
                if best is None or d < best[0]:
                    best = (d, x, y)
    return (best[1], best[2]) if best else (None, None)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--input", required=True)
    ap.add_argument("--scale", type=float, required=True, help="축소 배율 (8.5 → 1/8.5)")
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--fg-threshold", type=float, default=90, help="RGB 평균이 이 값보다 밝으면 몸통")
    ap.add_argument("--dark-threshold", type=float, default=110, help="몸통 안 이 값보다 어두우면 눈 같은 어두운 부분")
    ap.add_argument("--min-area", type=int, default=2000, help="이보다 작은 덩어리는 버림(물방울·먼지)")
    ap.add_argument("--names", help="쉼표 구분 포즈 이름 (위→아래, 왼→오른 순서)")
    ap.add_argument("--eye-size", default="1x2",
                    help="눈 크기 WxH 고정 (예 1x2). 축소하면 눈이 칸 경계에 걸려 1~2px로 들쭉날쭉해지므로 도장처럼 찍는다. 'off'면 끔")
    ap.add_argument("--eye-size-map", default="", help="포즈별 눈 크기 예외 (예 squash_a=1x1,squash_b=1x1)")
    ap.add_argument("--eye-threshold", type=float, default=70, help="눈으로 볼 어두운 정도(RGB 평균 미만)")
    ap.add_argument("--eye-color", default="181417", help="눈 색 HEX (기본: 캐릭터 선 색)")
    args = ap.parse_args(argv)
    eye_map = dict(kv.split("=") for kv in args.eye_size_map.split(",") if kv)
    eye_rgb = tuple(int(args.eye_color[i:i + 2], 16) for i in (0, 2, 4))

    im = np.array(Image.open(args.input).convert("RGB")).astype(float)
    fg = im.mean(-1) > args.fg_threshold
    lab, n = label(fg)
    comps = []
    for i in range(1, n + 1):
        ys, xs = np.nonzero(lab == i)
        if len(ys) >= args.min_area:
            comps.append((i, ys.min(), ys.max() + 1, xs.min(), xs.max() + 1, len(ys)))
    # 행(위→아래) 묶은 뒤 왼→오른
    comps.sort(key=lambda c: (c[1] // 150, c[3]))
    names = args.names.split(",") if args.names else [f"pose_{k + 1:02d}" for k in range(len(comps))]
    os.makedirs(args.out_dir, exist_ok=True)
    meta = []
    f = args.scale
    for name, (i, y0, y1, x0, x1, area) in zip(names, comps):
        m = fill_holes(lab[y0:y1, x0:x1] == i)
        rgb = im[y0:y1, x0:x1]
        h, w = m.shape
        W, H = max(1, round(w / f)), max(1, round(h / f))
        out = np.zeros((H, W, 4), dtype=np.uint8)
        for j in range(H):
            ya, yb = int(j * h / H), int((j + 1) * h / H)
            for k in range(W):
                xa, xb = int(k * w / W), int((k + 1) * w / W)
                mm = m[ya:yb, xa:xb]
                if mm.mean() < 0.5:
                    continue
                c = rgb[ya:yb, xa:xb][mm]
                dark = c.mean(-1) < args.dark_threshold
                if dark.mean() > 0.25:
                    col = c[dark].mean(0)
                elif (~dark).any():
                    col = np.median(c[~dark], 0)
                else:
                    col = c.mean(0)
                out[j, k, :3] = np.clip(col, 0, 255)
                out[j, k, 3] = 255
        eyes_out = []
        size = eye_map.get(name, args.eye_size)
        if size != "off":
            ew, eh = (int(v) for v in size.lower().split("x"))
            eyes = find_eyes(rgb, m, args.eye_threshold)
            op = out[..., 3] == 255
            light = out[op][:, :3].astype(float)
            dark_px = op & (out[..., :3].astype(float).mean(-1) < args.dark_threshold)
            if (~dark_px & op).any():
                fill = np.median(out[op & ~dark_px][:, :3], 0)
                out[dark_px, :3] = fill.astype(np.uint8)       # 축소로 생긴 들쭉날쭉한 눈을 먼저 지움
            for cx, cy in eyes:
                ex = int(round(cx / w * W - ew / 2))
                ey = int(round(cy / h * H - eh / 2))
                ex, ey = clamp_inside(op, ex, ey, ew, eh)
                if ex is None:
                    raise SystemExit(f"[눈 배치 실패] {name}: 눈 {ew}x{eh}을 몸통 안쪽에 둘 자리가 없음 "
                                     f"(원본 중심 {cx:.0f},{cy:.0f}). --eye-size-map으로 이 포즈만 작게 지정할 것")
                out[ey:ey + eh, ex:ex + ew, :3] = eye_rgb
                eyes_out.append([ex, ey, ew, eh])
        path = os.path.join(args.out_dir, name + ".png")
        Image.fromarray(out, "RGBA").save(path)
        meta.append({"name": name, "src_box": [int(x0), int(y0), int(x1), int(y1)], "size": [W, H], "area": int(area),
                     "eyes": eyes_out})
        print(f"{name}: 원본 {x1 - x0}x{y1 - y0}px → {W}x{H}px, 눈 {len(eyes_out)}개 {size}  ({path})")
    with open(os.path.join(args.out_dir, "pixelize.json"), "w", encoding="utf-8") as fh:
        json.dump({"input": args.input, "scale": f, "poses": meta}, fh, ensure_ascii=False, indent=2)


if __name__ == "__main__":
    main()
