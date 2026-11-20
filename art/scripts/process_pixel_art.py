#!/usr/bin/env python3
"""Palette Shaper 도트 후처리 + 자동 검사 파이프라인.

PixelLab 원본(art/raw/)은 그대로 Unity에 넣지 않는다. 아래 단계를 순서대로, 건너뛰지 않고 실행한다.

  Step 0  규격 맞추기   : 배경 판별(임시 마스크)로 내용 영역을 찾고, 정수배 확대본이면 축소,
                          프레임 크기(캐릭터 24x24 등) 안에 발바닥 중앙/가운데 기준으로 배치. 안 들어가면 FAIL.
  Step 1  배경 정리     : 테두리와 이어진 배경색 제거 → 알파 이진화(<128 → 0, >=128 → 255) → 외딴 1px 잔여 제거.
  Step 2  램프 다시 칠하기 : 지정 램프 안에서만. 먼저 색상·채도로 재질 램프를 고르고(밝기는 보조),
                          그 램프 안에서 밝기 순서로 4색 중 하나에 매핑. 전체 팔레트 nearest 금지.
  Step 3  선 정리       : 바깥선은 투명 경계(4방향)에서만 찾는다. '어두운 색 = 선' 금지. 역할별 선 색 적용.
  Step 4  자동 검사     : 모든 픽셀을 세서 판정. 하나라도 실패하면 FAIL + 픽셀 수·좌표·원인 보고,
                          출력 PNG를 쓰지 않고 Unity Assets로 복사하지 않는다.

사용 예:
  python art/scripts/process_pixel_art.py --input art/raw/slime_idle.png --in-frames 4 \
      --output art/processed/Spr_Player_Idle_Strip4.png --role character --ramps ivory_slime --bottom-2px
  python art/scripts/process_pixel_art.py --validate-only --input Assets/_Project/Sprites/X.png --role terrain --ramps sandstone_terrain
"""
import argparse
import json
import os
import shutil
import sys

import numpy as np
from PIL import Image

ART_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT_DIR = os.path.dirname(ART_DIR)
SPRITES_DIR = os.path.join(PROJECT_DIR, "Assets", "_Project", "Sprites")

ROLES = ("character", "prop_sandstone", "prop_wood_clay", "gimmick_crimson", "gimmick_amber", "terrain")
DEFAULT_FRAME = {"character": (24, 24)}  # 그 외 16x16
ROLE_DEFAULT_RAMPS = {
    "character": ["ivory_slime"],
    "prop_sandstone": ["sandstone_terrain"],
    "prop_wood_clay": ["wood_and_clay"],
    "gimmick_crimson": ["crimson_gimmick"],
    "gimmick_amber": ["amber_gimmick"],
    "terrain": ["sandstone_terrain"],
}
MAX_COORDS = 20  # 리포트에 적을 좌표 수


# ───────────────────────── 색 유틸 ─────────────────────────
def hex2rgb(h):
    h = h.strip().lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def rgb2hex(c):
    return "#%02X%02X%02X" % tuple(int(v) for v in c[:3])


def _lin(c):
    c = c / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def oklab(rgb):
    """rgb: (...,3) 0~255 → (...,3) OKLab (L 0~1)."""
    rgb = np.asarray(rgb, dtype=np.float64)
    r, g, b = _lin(rgb[..., 0]), _lin(rgb[..., 1]), _lin(rgb[..., 2])
    l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l, m, s = np.cbrt(l), np.cbrt(m), np.cbrt(s)
    return np.stack([
        0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
        1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
        0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
    ], axis=-1)


def load_palette(path):
    with open(path, encoding="utf-8") as f:
        return [hex2rgb(x) for x in f.read().split() if x.strip()]


def load_ramps(path):
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    ramps = {k: [hex2rgb(c) for c in v["colors"]] for k, v in d["ramps"].items()}
    return ramps, d["rules"]


# ───────────────────────── 마스크 유틸 ─────────────────────────
def boundary_mask(opaque, outside_is_transparent=True):
    """불투명 픽셀 중 4방향 이웃이 투명(또는 프레임 밖)인 것."""
    pad = np.pad(opaque, 1, constant_values=not outside_is_transparent)
    up, down = pad[:-2, 1:-1], pad[2:, 1:-1]
    left, right = pad[1:-1, :-2], pad[1:-1, 2:]
    return opaque & ~(up & down & left & right)


def frame_slices(w, frames):
    fw = w // frames
    return [(i * fw, (i + 1) * fw) for i in range(frames)]


def detect_background(rgba, tol=12):
    """테두리와 이어진 배경 마스크. 알파가 이미 있으면 알파 기준, 아니면 테두리 최빈색 flood fill."""
    a = rgba[..., 3]
    if (a < 255).any():
        return a < 128
    h, w = a.shape
    rgb = rgba[..., :3].astype(int)
    border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
    vals, counts = np.unique(border, axis=0, return_counts=True)
    bg = vals[counts.argmax()]
    similar = (np.abs(rgb - bg).max(axis=-1) <= tol)
    mask = np.zeros_like(similar)
    stack = [(y, x) for y in range(h) for x in (0, w - 1)] + [(y, x) for x in range(w) for y in (0, h - 1)]
    while stack:
        y, x = stack.pop()
        if 0 <= y < h and 0 <= x < w and similar[y, x] and not mask[y, x]:
            mask[y, x] = True
            stack.extend(((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)))
    return mask


# ───────────────────────── Step 0 ─────────────────────────
def detect_block_scale(rgba, max_k=8):
    """정확히 정수배(최근접)로 확대된 그림이면 배율 k(모든 k×k 블록이 단색), 아니면 1."""
    h, w = rgba.shape[:2]
    for k in range(max_k, 1, -1):
        if h % k or w % k:
            continue
        blocks = rgba.reshape(h // k, k, w // k, k, 4)
        first = blocks[:, :1, :, :1, :]
        if (blocks == first).all():
            return k
    return 1


def remove_islands(mask, min_island):
    """이웃(8방향) 불투명 픽셀이 하나도 없는 외딴 픽셀 제거."""
    if min_island <= 0:
        return mask, 0
    pad = np.pad(mask, 1)
    neigh = sum(np.roll(np.roll(pad, dy, 0), dx, 1)[1:-1, 1:-1]
                for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dy or dx)
    lonely = mask & (neigh == 0)
    return mask & ~lonely, int(lonely.sum())


def block_downscale(rgba, k):
    h, w = rgba.shape[:2]
    return rgba.reshape(h // k, k, w // k, k, 4)[:, k // 2, :, k // 2, :].copy()


def step0_normalize(frames_in, fw, fh, anchor, align, log, min_island=1, margin=(0, 0, 0, 0)):
    """frames_in: RGBA 배열 리스트 → 규격 프레임 리스트. 크기 계산에서 외딴 잔여 픽셀은 무시.
    margin = (왼, 위, 오른, 아래): 바깥 외곽선(outer)을 두를 자리를 비워 둔다."""
    ml, mt, mr, mb = margin
    fw_in, fh_in = fw - ml - mr, fh - mt - mb
    scaled = []
    for i, fr in enumerate(frames_in):
        k = detect_block_scale(fr)
        if k > 1:
            fr = block_downscale(fr, k)
            log.append(f"frame {i}: {k}배 확대본 → 1배로 축소")
        scaled.append(fr)
    if align == "none":
        # 이미 규격 캔버스에서 조합된 프레임(compose_*): 위치를 그대로 두고, 크기·여백만 검사한다.
        out = []
        for i, fr in enumerate(scaled):
            if fr.shape[:2] != (fh, fw):
                raise PipelineFail("Step 0", f"frame {i}: align=none인데 크기 {fr.shape[1]}x{fr.shape[0]} ≠ {fw}x{fh}")
            content, _ = remove_islands(~detect_background(fr), min_island)
            ys, xs = np.nonzero(content)
            if len(xs) == 0:
                raise PipelineFail("Step 0", f"frame {i}: 내용이 없음(전부 배경)")
            if xs.min() < ml or ys.min() < mt or xs.max() > fw - 1 - mr or ys.max() > fh - 1 - mb:
                raise PipelineFail("Step 0", f"frame {i}: 내용이 외곽선 여백(왼{ml} 위{mt} 오른{mr} 아래{mb})을 침범")
            fr = fr.copy()
            fr[~content] = 0
            out.append(fr)
            log.append(f"frame {i}: 위치 유지(align=none), 내용 x{xs.min()}-{xs.max()} y{ys.min()}-{ys.max()}")
        return out
    boxes = []
    for i, fr in enumerate(scaled):
        content, _ = remove_islands(~detect_background(fr), min_island)
        ys, xs = np.nonzero(content)
        if len(xs) == 0:
            raise PipelineFail("Step 0", f"frame {i}: 내용이 없음(전부 배경)")
        boxes.append((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1, content))
    if align == "union":
        x0 = min(b[0] for b in boxes); y0 = min(b[1] for b in boxes)
        x1 = max(b[2] for b in boxes); y1 = max(b[3] for b in boxes)
        boxes = [(x0, y0, x1, y1, b[4]) for b in boxes]
    out = []
    for i, (fr, (x0, y0, x1, y1, content)) in enumerate(zip(scaled, boxes)):
        bw, bh = x1 - x0, y1 - y0
        if bw > fw_in or bh > fh_in:
            extra = f" (바깥 외곽선 자리 {ml + mr}x{mt + mb}px 포함)" if any(margin) else ""
            raise PipelineFail("Step 0", f"frame {i}: 내용 {bw}x{bh}px가 프레임 {fw}x{fh}에 들어가지 않음{extra}")
        crop = fr[y0:y1, x0:x1].copy()
        cmask = content[y0:y1, x0:x1]
        crop[~cmask] = 0  # 임시 배경은 투명으로 (실제 정리는 Step 1에서)
        canvas = np.zeros((fh, fw, 4), dtype=np.uint8)
        ox = fw // 2 - bw // 2 if anchor != "center" else (fw - bw) // 2
        ox = min(max(ox, ml), fw - mr - bw)
        oy = fh - mb - bh if anchor == "bottom" else (fh - bh) // 2
        oy = min(max(oy, mt), fh - mb - bh)
        canvas[oy:oy + bh, ox:ox + bw] = crop
        out.append(canvas)
        log.append(f"frame {i}: 내용 {bw}x{bh}px → ({ox},{oy}) 배치 [{anchor}]")
    return out


# ───────────────────────── Step 1 ─────────────────────────
def step1_cleanup(fr, min_island, log, idx):
    bg = detect_background(fr)
    fr = fr.copy()
    fr[bg] = 0
    a = fr[..., 3]
    semi = int(((a > 0) & (a < 255)).sum())
    fr[..., 3] = np.where(a >= 128, 255, 0).astype(np.uint8)
    fr[fr[..., 3] == 0] = 0
    op = fr[..., 3] == 255
    kept, removed = remove_islands(op, min_island)
    fr[op & ~kept] = 0
    log.append(f"frame {idx}: 반투명 {semi}px 이진화, 외딴 잔여 {removed}px 제거")
    return fr


# ───────────────────────── Step 2 ─────────────────────────
def step2_remap(fr, ramp_list, log, idx, chroma_weight=4.0):
    """ramp_list: [(name, [rgb x4])]. 색상·채도로 램프 선택 → 램프 안 밝기 순서 매핑."""
    fr = fr.copy()
    op = fr[..., 3] == 255
    if not op.any():
        return fr
    px = fr[..., :3][op].astype(np.float64)
    lab = oklab(px)
    best_ramp = np.zeros(len(px), dtype=int)
    if len(ramp_list) > 1:
        dists = []
        for _, cols in ramp_list:
            rl = oklab(np.array(cols, dtype=np.float64))
            d = (chroma_weight * ((lab[:, None, 1] - rl[None, :, 1]) ** 2 + (lab[:, None, 2] - rl[None, :, 2]) ** 2)
                 + (lab[:, None, 0] - rl[None, :, 0]) ** 2)
            dists.append(d.min(axis=1))
        best_ramp = np.argmin(np.stack(dists, axis=1), axis=1)
    out = np.zeros_like(px)
    counts = {}
    for ri, (name, cols) in enumerate(ramp_list):
        sel = best_ramp == ri
        if not sel.any():
            continue
        rl = oklab(np.array(cols, dtype=np.float64))[:, 0]  # 램프 밝기(어두운→밝은)
        li = lab[sel, 0]
        level = np.abs(li[:, None] - rl[None, :]).argmin(axis=1)
        out[sel] = np.array(cols, dtype=np.float64)[level]
        counts[name] = int(sel.sum())
    fr[..., :3][op] = out.astype(np.uint8)
    log.append(f"frame {idx}: 램프 배정 {counts}")
    return fr


# ───────────────────────── Step 3 ─────────────────────────
def bottom_contact(op):
    """땅에 닿는 최하단 행의 불투명 픽셀 (y, x) 목록."""
    ys, xs = np.nonzero(op)
    if len(ys) == 0:
        return []
    ymax = ys.max()
    return [(ymax, x) for x in np.nonzero(op[ymax])[0]]


def step3_lines(fr, role, line_rgb, ramp_cols, bottom2, inactive, log, idx, outline_mode="inner"):
    fr = fr.copy()
    op = fr[..., 3] == 255
    rgb = fr[..., :3]
    changed = 0
    if outline_mode == "outer" and role != "terrain" and not inactive:
        # 바깥 외곽선: 몸통에 4방향으로 닿는 '투명' 칸에 선을 두른다. 몸통 픽셀(꼬리 끝·가장자리 눈)은 그대로.
        # 원본에 이미 그려진 외곽선(투명 경계에 있는 선 색 픽셀)은 먼저 벗겨서 선이 2겹이 되지 않게 한다.
        edge = boundary_mask(op, outside_is_transparent=True)
        old_line = edge & np.all(rgb == np.array(line_rgb, dtype=np.uint8), axis=-1)
        fr[old_line] = 0
        op = fr[..., 3] == 255
        pad = np.pad(op, 1)
        near = pad[:-2, 1:-1] | pad[2:, 1:-1] | pad[1:-1, :-2] | pad[1:-1, 2:]
        ring = ~op & near
        fr[ring] = (*line_rgb, 255)
        added = int(ring.sum())
        thick = 0
        if bottom2:
            op2 = fr[..., 3] == 255
            for y, x in bottom_contact(op2):
                if y + 1 < fr.shape[0]:
                    fr[y + 1, x] = (*line_rgb, 255)
                    thick += 1
        log.append(f"frame {idx}: 원본 외곽선 {int(old_line.sum())}px 벗김 → 바깥 외곽선(outer) {added}px 추가"
                   f"({rgb2hex(line_rgb)}), 아랫선 보강 {thick}px")
        return fr
    if role == "terrain":
        # 무선: 프레임 가장자리/투명 경계에 '선 색'이 있으면 한 단계 밝은 톤으로
        edge = boundary_mask(op, outside_is_transparent=True)
        line_set = {tuple(c) for c in LINE_COLORS_ALL}
        dark, mid = ramp_cols[0], ramp_cols[1]
        for y, x in zip(*np.nonzero(edge)):
            c = tuple(int(v) for v in rgb[y, x])
            if c in line_set or c == tuple(dark):
                rgb[y, x] = mid
                changed += 1
        log.append(f"frame {idx}: 지형 가장자리 선 색 {changed}px → {rgb2hex(mid)}")
        return fr
    if role == "gimmick_crimson" and inactive:
        log.append(f"frame {idx}: 비활성 점선 유지(선 정리 없음)")
        return fr
    edge = boundary_mask(op, outside_is_transparent=True)
    # 안쪽 과도한 선 완화: 경계가 아닌데 선 색인 픽셀 → 한 단계 밝게 (캐릭터는 눈 같은 표정 픽셀이라 제외)
    softened = 0
    if role != "character":
        lighter = ramp_cols[1] if tuple(ramp_cols[0]) == tuple(line_rgb) else ramp_cols[0]
        inner_line = op & ~edge & np.all(rgb == np.array(line_rgb, dtype=np.uint8), axis=-1)
        rgb[inner_line] = lighter
        softened = int(inner_line.sum())
    changed = int((edge & ~np.all(rgb == np.array(line_rgb, dtype=np.uint8), axis=-1)).sum())
    rgb[edge] = line_rgb
    thick = 0
    if bottom2:
        for y, x in bottom_contact(op):
            if y - 1 >= 0 and op[y - 1, x] and tuple(rgb[y - 1, x]) != tuple(line_rgb):
                rgb[y - 1, x] = line_rgb
                thick += 1
    log.append(f"frame {idx}: 바깥선 {changed}px 정리({rgb2hex(line_rgb)}), 안쪽 선 완화 {softened}px, 아랫선 보강 {thick}px")
    return fr


LINE_COLORS_ALL = []  # main()에서 ramps.json의 역할별 선 색으로 채움


# ───────────────────────── Step 4 ─────────────────────────
class PipelineFail(Exception):
    def __init__(self, step, msg):
        super().__init__(f"[{step}] {msg}")
        self.step, self.msg = step, msg


def coords(mask, x_off=0):
    ys, xs = np.nonzero(mask)
    return [[int(x + x_off), int(y)] for y, x in list(zip(ys, xs))[:MAX_COORDS]]


def step4_validate(img, frames, role, palette, line_rgb, ramp_cols, max_colors, bottom2, inactive, expect_size):
    checks = []
    h, w = img.shape[:2]

    def add(name, ok, count=0, where=None, cause=""):
        checks.append({"check": name, "pass": bool(ok), "count": int(count), "coords": where or [], "cause": cause if not ok else ""})

    if expect_size:
        ew, eh = expect_size
        add("프레임 크기", w == ew * frames and h == eh, 0, None,
            f"이미지 {w}x{h}, 기대 {ew * frames}x{eh} ({frames}프레임)")
    a = img[..., 3]
    semi = (a > 0) & (a < 255)
    add("반투명 픽셀 == 0", semi.sum() == 0, semi.sum(), coords(semi), "0<alpha<255 픽셀")
    op = a == 255
    pal = np.array(palette, dtype=np.int32)
    rgb = img[..., :3].astype(np.int32)
    inpal = (rgb[:, :, None, :] == pal[None, None, :, :]).all(-1).any(-1)
    off = op & ~inpal
    add("팔레트 밖 색 == 0", off.sum() == 0, off.sum(), coords(off), "art/palette.hex에 없는 색")
    line = np.array(line_rgb if line_rgb else (0, 0, 0), dtype=np.int32)
    line_set = np.array(LINE_COLORS_ALL, dtype=np.int32)
    for fi, (x0, x1) in enumerate(frame_slices(w, frames)):
        fop = op[:, x0:x1]
        frgb = rgb[:, x0:x1]
        edge = boundary_mask(fop, outside_is_transparent=True)
        tag = f"[frame {fi}] "
        if role == "terrain":
            is_line = (frgb[:, :, None, :] == line_set[None, None, :, :]).all(-1).any(-1)
            bad = edge & is_line
            add(tag + "지형 무선(가장자리에 선 색 없음)", bad.sum() == 0, bad.sum(), coords(bad, x0), "가장자리 픽셀이 선 색")
        elif role == "gimmick_crimson" and inactive:
            rc = np.array(ramp_cols, dtype=np.int32)
            inr = (frgb[:, :, None, :] == rc[None, None, :, :]).all(-1).any(-1)
            bad = fop & ~inr
            add(tag + "비활성: 크림슨 램프 색만", bad.sum() == 0, bad.sum(), coords(bad, x0), "크림슨 램프 밖 색")
        else:
            bad = edge & ~(frgb == line).all(-1)
            add(tag + f"바깥선 = {rgb2hex(line_rgb)}만", bad.sum() == 0, bad.sum(), coords(bad, x0),
                "투명 경계 픽셀이 허용 선 색이 아님")
            if bottom2:
                miss = []
                for y, x in bottom_contact(fop):
                    if y - 1 >= 0 and fop[y - 1, x] and not (frgb[y - 1, x] == line).all():
                        miss.append([int(x + x0), int(y - 1)])
                add(tag + "아랫선 2px", len(miss) == 0, len(miss), miss[:MAX_COORDS], "땅에 닿는 열의 바로 위 픽셀이 선 색이 아님")
        cols = np.unique(frgb[fop].reshape(-1, 3), axis=0) if fop.any() else np.zeros((0, 3))
        n = len(cols)
        add(tag + f"색 수 <= {max_colors}", n <= max_colors, n, None,
            f"{n}색: " + ", ".join(rgb2hex(c) for c in cols))
    return checks


# ───────────────────────── main ─────────────────────────
def load_frames(paths, in_frames):
    frames = []
    for p in paths:
        im = np.array(Image.open(p).convert("RGBA"))
        n = in_frames if len(paths) == 1 else 1
        if im.shape[1] % n:
            raise PipelineFail("입력", f"{p}: 가로 {im.shape[1]}px를 {n}프레임으로 나눌 수 없음")
        for x0, x1 in frame_slices(im.shape[1], n):
            frames.append(im[:, x0:x1].copy())
    return frames


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--input", nargs="+", required=True, help="PNG 1개(가로 스트립 가능) 또는 프레임별 PNG 여러 개")
    ap.add_argument("--in-frames", type=int, default=1, help="입력이 가로 스트립일 때 프레임 수")
    ap.add_argument("--output", help="출력 PNG (가로 스트립). PASS일 때만 쓴다")
    ap.add_argument("--role", required=True, choices=ROLES)
    ap.add_argument("--ramps", help="쉼표 구분 램프 이름 (기본: 역할별)")
    ap.add_argument("--bottom-2px", action="store_true", help="땅에 닿는 아랫선 2px (prop_*는 항상 적용)")
    ap.add_argument("--inactive", action="store_true", help="gimmick_crimson 비활성(점선) 스프라이트")
    ap.add_argument("--frame", help="출력 프레임 크기 WxH (기본: character 24x24, 그 외 16x16)")
    ap.add_argument("--anchor", choices=["bottom", "center"], help="배치 기준 (기본: character/prop=bottom, 그 외 center)")
    ap.add_argument("--align", choices=["frame", "union", "none"], default="frame",
                    help="frame: 프레임별 정렬 / union: 전체 공통 박스 정렬 / none: 이미 규격 캔버스에 조합된 위치 그대로(여백만 검사)")
    ap.add_argument("--min-island", type=int, default=1, help="이웃 없는 외딴 픽셀 제거 (0=끔)")
    ap.add_argument("--outline-mode", choices=["inner", "outer"],
                    help="inner: 몸통 가장자리 1px를 선으로 덮음 / outer: 몸통 바깥 투명 칸에 선을 두름 (기본: character=outer, 그 외 inner)")
    ap.add_argument("--validate-only", action="store_true", help="Step 4 검사만 (입력은 완성 스프라이트)")
    ap.add_argument("--deploy", action="store_true", help="PASS일 때만 Assets/_Project/Sprites/로 복사")
    ap.add_argument("--report", help="리포트 JSON 경로 (기본 art/reports/<출력이름>.json)")
    ap.add_argument("--art-dir", default=ART_DIR, help=argparse.SUPPRESS)
    args = ap.parse_args(argv)

    palette = load_palette(os.path.join(args.art_dir, "palette.hex"))
    ramps, rules = load_ramps(os.path.join(args.art_dir, "ramps.json"))
    LINE_COLORS_ALL[:] = [hex2rgb(v) for v in rules["outline_by_role"].values() if v]
    max_colors = int(rules.get("max_colors_per_sprite", 5))
    role = args.role
    ramp_names = args.ramps.split(",") if args.ramps else ROLE_DEFAULT_RAMPS[role]
    for r in ramp_names:
        if r not in ramps:
            print(f"알 수 없는 램프: {r} (있는 것: {', '.join(ramps)})", file=sys.stderr)
            return 2
    line_hex = rules["outline_by_role"].get(role)
    line_rgb = hex2rgb(line_hex) if line_hex else None
    main_ramp = ramps[ramp_names[0]]
    bottom2 = args.bottom_2px or role.startswith("prop_")
    fw, fh = (int(v) for v in args.frame.lower().split("x")) if args.frame else DEFAULT_FRAME.get(role, (16, 16))
    anchor = args.anchor or ("bottom" if role == "character" or role.startswith("prop_") else "center")
    outline_mode = args.outline_mode or ("outer" if role == "character" else "inner")
    if outline_mode == "outer" and role != "terrain" and not args.inactive:
        margin = (1, 1, 1, 2 if bottom2 else 1)
    else:
        margin = (0, 0, 0, 0)

    # 램프 목록 구성. 캐릭터는 몸통 램프 + 선 색(눈 등 표정용)을 1색 램프로 허용.
    ramp_list = [(n, ramps[n]) for n in ramp_names]
    if role == "character" and line_rgb and not any(tuple(line_rgb) in [tuple(c) for c in cols] for _, cols in ramp_list):
        ramp_list.append(("line_ink", [line_rgb]))

    name = os.path.splitext(os.path.basename(args.output or args.input[0]))[0]
    report_path = args.report or os.path.join(args.art_dir, "reports", name + ".json")
    log, result = [], {"name": name, "role": role, "ramps": ramp_names, "inputs": args.input}
    try:
        if args.validate_only:
            img = np.array(Image.open(args.input[0]).convert("RGBA"))
            n_frames = args.in_frames
        else:
            raw = load_frames(args.input, args.in_frames)
            n_frames = len(raw)
            frames = step0_normalize(raw, fw, fh, anchor, args.align, log, args.min_island, margin)
            frames = [step1_cleanup(f, args.min_island, log, i) for i, f in enumerate(frames)]
            frames = [step2_remap(f, ramp_list, log, i) for i, f in enumerate(frames)]
            frames = [step3_lines(f, role, line_rgb, main_ramp, bottom2, args.inactive, log, i, outline_mode)
                      for i, f in enumerate(frames)]
            if anchor == "bottom" and args.align != "none":
                # 선 정리로 맨 아래 행이 달라졌을 수 있음 → 모든 프레임을 같은 양만큼 내려 발바닥을 프레임 맨 아래에 맞춤
                lowest = max(np.nonzero(f[..., 3] == 255)[0].max() for f in frames)
                d = fh - 1 - lowest
                if d > 0:
                    frames = [np.concatenate([np.zeros((d, fw, 4), np.uint8), f[:-d]], axis=0) for f in frames]
                    log.append(f"발바닥 재정렬: 전체 {d}px 아래로")
            img = np.concatenate(frames, axis=1)
        checks = step4_validate(img, n_frames, role, palette, line_rgb, main_ramp, max_colors, bottom2, args.inactive,
                                None if args.validate_only else (fw, fh))
        ok = all(c["pass"] for c in checks)
    except PipelineFail as e:
        checks, ok, img = [{"check": e.step, "pass": False, "count": 0, "coords": [], "cause": e.msg}], False, None

    result.update(status="PASS" if ok else "FAIL", outline_mode=outline_mode, log=log, checks=checks)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    if img is not None and not args.validate_only:
        preview = report_path[:-5] + (".png" if ok else ".rejected.png")
        Image.fromarray(img, "RGBA").save(preview)
        result["preview"] = preview
    if ok and args.output and not args.validate_only:
        os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
        Image.fromarray(img, "RGBA").save(args.output)
        result["output"] = args.output
        if args.deploy:
            dst = os.path.join(SPRITES_DIR, os.path.basename(args.output))
            shutil.copyfile(args.output, dst)
            result["deployed"] = dst
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    print(f"{result['status']}: {name} ({role}, ramps={','.join(ramp_names)})")
    for c in checks:
        mark = "ok " if c["pass"] else "FAIL"
        extra = "" if c["pass"] else f" — {c['count']}px {c['cause']} {c['coords'][:5]}"
        print(f"  [{mark}] {c['check']}{extra}")
    if not ok:
        print("  → 출력 PNG를 쓰지 않았고 Assets로 복사하지 않음")
        if args.output and os.path.exists(args.output) and not args.validate_only:
            os.replace(args.output, args.output + ".stale")  # 예전 PASS 결과가 남아 잘못 쓰이지 않게
            print(f"  → 이전 출력물을 {os.path.basename(args.output)}.stale 로 치움")
    print(f"  리포트: {report_path}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
