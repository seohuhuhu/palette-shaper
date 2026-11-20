"""process_pixel_art.py 단위 테스트 (합성 이미지).  실행: python art/scripts/test_process_pixel_art.py"""
import json
import os
import shutil
import sys
import tempfile
import unittest

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import process_pixel_art as P  # noqa: E402

ART = os.path.dirname(HERE)


def hexes(path):
    return {tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) for h in open(path).read().split()}


class PipelineTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.art = os.path.join(self.tmp, "art")
        os.makedirs(os.path.join(self.art, "reports"))
        for f in ("palette.hex", "ramps.json"):
            shutil.copy(os.path.join(ART, f), self.art)
        self.palette = hexes(os.path.join(self.art, "palette.hex"))
        ramps = json.load(open(os.path.join(self.art, "ramps.json"), encoding="utf-8"))["ramps"]
        self.ramp = {k: {P.hex2rgb(c) for c in v["colors"]} for k, v in ramps.items()}

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def run_p(self, *argv):
        return P.main(list(argv) + ["--art-dir", self.art])

    def save(self, arr, name):
        p = os.path.join(self.tmp, name)
        Image.fromarray(arr.astype(np.uint8), "RGBA" if arr.shape[-1] == 4 else "RGB").save(p)
        return p

    def colors(self, path):
        a = np.array(Image.open(path).convert("RGBA"))
        return a, {tuple(c) for c in a[a[..., 3] == 255][:, :3]}

    def slime_raw(self):
        """초록 배경 + 크림 몸통(그림자·하이라이트) + 진한 외곽선·눈 + 붉은 얼룩 1px + 외딴 1px, 4배 확대."""
        g = np.zeros((32, 32, 3)) + (40, 200, 60)
        body = np.zeros((32, 32), bool)
        for y in range(12, 28):
            half = min(6, 2 + (y - 12) // 2)
            body[y, 16 - half:16 + half] = True
        g[body] = (238, 232, 218)
        g[24:27, 18:22] = (190, 182, 170)            # 그림자
        g[14:16, 14:16] = (255, 254, 248)            # 하이라이트
        g[20, 16] = (205, 70, 80)                    # 붉은 얼룩: 램프 밖(크림슨)으로 튀면 안 됨
        edge = body & ~(np.roll(body, 1, 0) & np.roll(body, -1, 0) & np.roll(body, 1, 1) & np.roll(body, -1, 1))
        g[edge] = (28, 24, 30)                       # 외곽선
        g[19:21, 13] = g[19:21, 18] = (28, 24, 30)   # 눈
        g[3, 3] = (238, 232, 218)                    # 외딴 잔여 픽셀
        return np.kron(g, np.ones((4, 4, 1)))

    # ── 테스트 ──
    def test_character_full_pass(self):
        raw = self.save(self.slime_raw(), "slime.png")
        out = os.path.join(self.tmp, "Spr_Test.png")
        rc = self.run_p("--input", raw, "--output", out, "--role", "character", "--ramps", "ivory_slime", "--bottom-2px")
        self.assertEqual(rc, 0)
        a, cols = self.colors(out)
        self.assertEqual(a.shape[:2], (24, 24))
        self.assertTrue(cols <= self.ramp["ivory_slime"] | {(0x18, 0x14, 0x17)}, cols)  # 크림슨으로 튀지 않음
        self.assertFalse(((a[..., 3] > 0) & (a[..., 3] < 255)).any())
        ys, xs = np.nonzero(a[..., 3])
        self.assertEqual(ys.max(), 23)                                  # 발바닥 = 맨 아래 행
        self.assertAlmostEqual((xs.min() + xs.max() + 1) / 2, 12, delta=0.5)  # 가로 중앙
        self.assertEqual(a[3 * 0 + 3, 3, 3], 0)                         # 외딴 픽셀 제거됨

    def test_outer_outline_keeps_body_pixels(self):
        """outer: 외곽선 없는 원본의 몸통 픽셀(1px 꼬리 끝 포함)은 한 픽셀도 선으로 덮이지 않는다."""
        a = np.zeros((24, 24, 4))
        a[10:20, 6:16] = (240, 225, 184, 255)
        a[7:10, 12] = (240, 225, 184, 255)         # 1px 폭 꼬리
        a[14, 9] = a[14, 15] = (40, 34, 32, 255)    # 눈 (오른쪽 눈은 몸통 가장자리)
        out = os.path.join(self.tmp, "Spr_O.png")
        rc = self.run_p("--input", self.save(a, "o.png"), "--output", out, "--role", "character", "--bottom-2px")
        self.assertEqual(rc, 0)
        b, _ = self.colors(out)
        ink = (b[..., :3] == (0x18, 0x14, 0x17)).all(-1) & (b[..., 3] == 255)
        body = (b[..., 3] == 255) & ~ink
        self.assertEqual(int(body.sum()), 10 * 10 + 3 - 2)  # 몸통+꼬리 그대로 (눈 2px만 잉크)
        # 원본 몸통 영역(x 6~15, y 7~19)이 출력 어디에 놓였는지 찾고, 눈 두 칸이 잉크로 남았는지 확인
        ys, xs = np.nonzero(body)
        y0, x0 = ys.min(), xs.min()          # 꼬리 꼭대기(원본 y7) / 몸통 왼쪽(원본 x6)
        for ey, ex in ((14, 9), (14, 15)):   # 오른쪽 눈(x15)은 몸통 가장자리 — inner 방식이면 선에 먹힘
            self.assertTrue(ink[y0 + ey - 7, x0 + ex - 6], (ey, ex))

    def test_outer_no_double_outline(self):
        """outer: 원본에 외곽선이 이미 있으면 벗기고 다시 둘러서 1겹만 남는다."""
        raw = self.save(self.slime_raw(), "s2.png")
        out = os.path.join(self.tmp, "Spr_D.png")
        self.assertEqual(self.run_p("--input", raw, "--output", out, "--role", "character"), 0)
        b, _ = self.colors(out)
        op = b[..., 3] == 255
        ink = (b[..., :3] == (0x18, 0x14, 0x17)).all(-1) & op
        edge = P.boundary_mask(op)
        inner = op & ~edge
        # 경계 바로 안쪽(2번째 줄)에 잉크가 이어져 있으면 2겹 — 눈(2+2px) 말고는 없어야 함
        self.assertLessEqual(int((ink & inner).sum()), 4)

    def test_align_none_keeps_position_and_checks_margin(self):
        """align=none: 조합된 위치(피벗 기준)를 그대로 두고, 외곽선 여백 침범은 FAIL."""
        a = np.zeros((24, 24, 4))
        a[14:22, 3:11] = (240, 225, 184, 255)     # 일부러 왼쪽에 둔 몸통 (바닥 21행)
        out = os.path.join(self.tmp, "Spr_N.png")
        self.assertEqual(self.run_p("--input", self.save(a, "n.png"), "--output", out, "--role", "character",
                                    "--align", "none", "--bottom-2px"), 0)
        b, _ = self.colors(out)
        body = (b[..., 3] == 255) & ~(b[..., :3] == (0x18, 0x14, 0x17)).all(-1)
        ys, xs = np.nonzero(body)
        self.assertEqual((xs.min(), ys.max()), (3, 21))          # 옮기지 않음
        a2 = np.zeros((24, 24, 4)); a2[14:23, 0:8] = (240, 225, 184, 255)   # 여백(왼1·아래2) 침범
        out2 = os.path.join(self.tmp, "Spr_N2.png")
        self.assertEqual(self.run_p("--input", self.save(a2, "n2.png"), "--output", out2, "--role", "character",
                                    "--align", "none", "--bottom-2px"), 1)

    def test_semi_transparent_edges_binarized(self):
        a = np.zeros((16, 16, 4))
        a[4:12, 4:12] = (89, 77, 67, 255)
        a[5:11, 5:11] = (166, 151, 133, 255)
        a[3, 4:12, :] = (166, 151, 133, 90)   # 반투명 가장자리 (alpha<128 → 지워짐)
        a[12, 4:12, :] = (166, 151, 133, 200)  # 반투명 (alpha>=128 → 불투명)
        out = os.path.join(self.tmp, "Spr_P.png")
        rc = self.run_p("--input", self.save(a, "p.png"), "--output", out, "--role", "prop_sandstone")
        self.assertEqual(rc, 0)
        b, _ = self.colors(out)
        self.assertFalse(((b[..., 3] > 0) & (b[..., 3] < 255)).any())

    def test_prop_outline_from_alpha_boundary_not_darkness(self):
        a = np.zeros((16, 16, 4))
        a[3:13, 4:12] = (201, 187, 167, 255)  # 밝은 사암, 바깥선 없음
        a[7, 5:11] = (89, 77, 67, 255)        # 안쪽 진한 선 (최암부) → 완화돼야 함
        out = os.path.join(self.tmp, "Spr_Q.png")
        self.assertEqual(self.run_p("--input", self.save(a, "q.png"), "--output", out, "--role", "prop_sandstone"), 0)
        b, _ = self.colors(out)
        op = b[..., 3] == 255
        edge = P.boundary_mask(op)
        line = np.array((0x59, 0x4D, 0x43))
        self.assertTrue((b[..., :3][edge] == line).all())                       # 경계 전부 선 색
        inner = op & ~edge
        ys, xs = np.nonzero(op)
        inner[ys.max() - 1] = False                                              # 아랫선 2px 줄은 제외
        self.assertFalse((b[..., :3][inner] == line).all(-1).any())              # 안쪽엔 최암부 없음
        bottom = ys.max()
        for x in np.nonzero(op[bottom])[0]:
            self.assertTrue((b[bottom - 1, x, :3] == line).all())               # 아랫선 2px

    def test_terrain_lineless(self):
        a = np.zeros((16, 16, 4)) + (166, 151, 133, 255)
        a[0, :] = a[:, 0] = (24, 20, 23, 255)   # 검은 테두리
        a[15, :] = (89, 77, 67, 255)            # 최암부 테두리
        out = os.path.join(self.tmp, "Spr_T.png")
        self.assertEqual(self.run_p("--input", self.save(a, "t.png"), "--output", out, "--role", "terrain"), 0)
        b, cols = self.colors(out)
        border = np.concatenate([b[0], b[-1], b[:, 0], b[:, -1]])[:, :3]
        for c in [(0x18, 0x14, 0x17), (0x59, 0x4D, 0x43), (0x3D, 0x02, 0x0B), (0x38, 0x26, 0x1C)]:
            self.assertFalse((border == c).all(-1).any(), c)

    def test_gimmick_inactive_keeps_dash(self):
        a = np.zeros((16, 16, 4))
        a[2, 2:14:2] = a[13, 2:14:2] = (184, 37, 58, 255)
        a[2:14:2, 2] = a[2:14:2, 13] = (184, 37, 58, 255)
        out = os.path.join(self.tmp, "Spr_I.png")
        self.assertEqual(self.run_p("--input", self.save(a, "i.png"), "--output", out, "--role", "gimmick_crimson",
                                    "--inactive", "--min-island", "0"), 0)
        _, cols = self.colors(out)
        self.assertEqual(cols, {(0xB8, 0x25, 0x3A)})

    def test_validate_only_reports_failures(self):
        a = np.zeros((16, 16, 4))
        a[4:12, 4:12] = (0xC9, 0xBB, 0xA7, 255)
        a[6, 6] = (10, 200, 10, 255)          # 팔레트 밖
        a[3, 5] = (0xC9, 0xBB, 0xA7, 100)     # 반투명
        p = self.save(a, "bad.png")
        rc = self.run_p("--validate-only", "--input", p, "--role", "prop_sandstone")
        self.assertEqual(rc, 1)
        rep = json.load(open(os.path.join(self.art, "reports", "bad.json"), encoding="utf-8"))
        bad = {c["check"]: c for c in rep["checks"] if not c["pass"]}
        self.assertEqual(bad["반투명 픽셀 == 0"]["count"], 1)
        self.assertEqual(bad["팔레트 밖 색 == 0"]["coords"], [[6, 6]])
        self.assertTrue(any("바깥선" in k for k in bad))

    def test_fail_writes_no_output(self):
        a = np.zeros((40, 40, 4))
        a[1:38, 1:38] = (0xF4, 0xF1, 0xEA, 255)   # 37px: 24x24 프레임 초과, 정수배 확대본도 아님
        out = os.path.join(self.tmp, "Spr_Big.png")
        Image.new("RGBA", (24, 24)).save(out)                    # 예전 PASS 결과가 남아 있다고 가정
        rc = self.run_p("--input", self.save(a, "big.png"), "--output", out, "--role", "character")
        self.assertEqual(rc, 1)
        self.assertFalse(os.path.exists(out))                    # FAIL이면 출력 경로에 아무것도 남지 않음
        self.assertTrue(os.path.exists(out + ".stale"))

    def test_strip_frames(self):
        raw = self.slime_raw()
        strip = np.concatenate([raw, raw, raw], axis=1)
        out = os.path.join(self.tmp, "Spr_S_Strip3.png")
        rc = self.run_p("--input", self.save(strip, "strip.png"), "--in-frames", "3", "--output", out,
                        "--role", "character", "--bottom-2px")
        self.assertEqual(rc, 0)
        self.assertEqual(Image.open(out).size, (72, 24))


if __name__ == "__main__":
    unittest.main(verbosity=2)
