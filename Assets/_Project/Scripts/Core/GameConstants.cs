using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// Palette Shaper 전역 상수. 1 Unity Unit = 16 px = 1 Tile.
    /// 값은 기획 명세(Master Specification)를 그대로 옮긴 것이므로 임의 수정 금지.
    /// </summary>
    public static class GameConstants
    {
        // ───────── 1. 픽셀 아트 / 렌더링 규격 ─────────
        public const int PIXELS_PER_UNIT = 16;
        public const int TILE_SIZE_PX = 16;
        public const int REF_RESOLUTION_X = 320;
        public const int REF_RESOLUTION_Y = 180;
        public const int ROOM_WIDTH_TILES = 20;   // 320 px
        public const int ROOM_HEIGHT_TILES = 11;  // 176 px (상하 4px 레터박스)
        public const float CAMERA_ORTHO_SIZE = 5.625f; // 참고값: PixelPerfectCamera가 자동 계산함

        // 스프라이트 규격 (px)
        public const int PLAYER_CANVAS_PX = 24;
        public static readonly Vector2 PLAYER_HITBOX_SIZE = new Vector2(0.75f, 1.0f); // 12x16 px
        public const int GEAR_PLATFORM_WIDTH_PX = 32;
        public const int GEAR_PLATFORM_HEIGHT_PX = 16;
        public const int EXIT_DOOR_WIDTH_PX = 32;
        public const int EXIT_DOOR_HEIGHT_PX = 48;

        // ───────── 고정 6색 팔레트 ─────────
        // Monochrome Base
        public static readonly Color32 CREAM_IVORY   = new Color32(0xF4, 0xF1, 0xEA, 0xFF); // #F4F1EA 플레이어/하이라이트
        public static readonly Color32 WARM_GRAY     = new Color32(0xB9, 0xB2, 0xA6, 0xFF); // #B9B2A6 배경 중간톤/비활성 점선
        public static readonly Color32 DEEP_CHARCOAL = new Color32(0x2B, 0x29, 0x27, 0xFF); // #2B2927 고정 지형/외곽선
        // Crimson Gimmick
        public static readonly Color32 LIGHT_CORAL   = new Color32(0xFF, 0x6B, 0x7A, 0xFF); // #FF6B7A 코어 발광/하이라이트
        public static readonly Color32 BURGUNDY_RED  = new Color32(0x75, 0x05, 0x18, 0xFF); // #750518 크림슨 활성 메인
        public static readonly Color32 DARK_WINE     = new Color32(0x3D, 0x02, 0x0B, 0xFF); // #3D020B 크림슨 그림자/내부 외곽선

        // Amber Gimmick (Chapter 2, 2026-11)
        public static readonly Color32 AMBER_HIGHLIGHT = new Color32(0xFF, 0xE8, 0x85, 0xFF); // #FFE885 코어 발광/하이라이트, 앰버 파동 링
        public static readonly Color32 AMBER_MAIN      = new Color32(0xD4, 0xA3, 0x59, 0xFF); // #D4A359 앰버 활성 메인, 배경 틴트
        public static readonly Color32 AMBER_SHADOW    = new Color32(0x6E, 0x4E, 0x1E, 0xFF); // #6E4E1E 앰버 그림자/외곽선

        public static readonly Color32[] PALETTE =
        {
            CREAM_IVORY, WARM_GRAY, DEEP_CHARCOAL, LIGHT_CORAL, BURGUNDY_RED, DARK_WINE,
            AMBER_HIGHLIGHT, AMBER_MAIN, AMBER_SHADOW
        };

        // ───────── 2. Sorting Layers (뒤 → 앞, Default는 맨 뒤) ─────────
        public const string SORT_DEFAULT          = "Default";
        public const string SORT_BG_WALL          = "BG_Wall";
        public const string SORT_BG_PROPS         = "BG_Props";
        public const string SORT_GIMMICK_INACTIVE = "Gimmick_Inactive";
        public const string SORT_TERRAIN_BASE     = "Terrain_Base";
        public const string SORT_GIMMICK_ACTIVE   = "Gimmick_Active";
        public const string SORT_PLAYER           = "Player";
        public const string SORT_VFX_WAVE         = "VFX_Wave";
        public const string SORT_UI               = "UI";

        public static readonly string[] SORTING_LAYER_ORDER =
        {
            SORT_BG_WALL, SORT_BG_PROPS, SORT_GIMMICK_INACTIVE, SORT_TERRAIN_BASE,
            SORT_GIMMICK_ACTIVE, SORT_PLAYER, SORT_VFX_WAVE, SORT_UI
        };

        // ───────── Physics 2D Layers ─────────
        public const int LAYER_PLAYER         = 6;
        public const int LAYER_TERRAIN_BASE   = 7;
        public const int LAYER_CRIMSON_SOLID  = 8;
        public const int LAYER_CRIMSON_HAZARD = 9;
        public const int LAYER_AMBER_SOLID    = 10; // AMBER일 때만 Player와 충돌
        public const int LAYER_AMBER_HAZARD   = 11; // AMBER일 때만 사망 판정

        public const string LAYER_NAME_PLAYER         = "Player";
        public const string LAYER_NAME_TERRAIN_BASE   = "Terrain_Base";
        public const string LAYER_NAME_CRIMSON_SOLID  = "Crimson_Solid";
        public const string LAYER_NAME_CRIMSON_HAZARD = "Crimson_Hazard";
        public const string LAYER_NAME_AMBER_SOLID    = "Amber_Solid";
        public const string LAYER_NAME_AMBER_HAZARD   = "Amber_Hazard";

        // ───────── 4. 플레이어 및 기믹 물리 상수 (Unit 기준) ─────────
        public const float RUN_SPEED = 6.0f;            // 96 px/s
        public const float GROUND_ACCEL = 75.0f;
        public const float GROUND_DECEL = 75.0f;
        public const float AIR_ACCEL = 50.0f;
        public const float AIR_DECEL = 50.0f;
        public const float JUMP_HEIGHT = 3.25f;         // 52 px
        public const float TIME_TO_JUMP_APEX = 0.32f;
        public const float GRAVITY = -63.48f;           // -2 * JUMP_HEIGHT / TIME_TO_JUMP_APEX^2
        public const float JUMP_VELOCITY = 20.31f;      // sqrt(2 * |GRAVITY| * JUMP_HEIGHT)
        public const float VARIABLE_JUMP_MULT = 0.45f;  // 상승 중 점프 키 해제 시 Y 속도에 곱함
        public const float MAX_FALL_SPEED = -15.0f;     // -240 px/s
        public const float COYOTE_TIME = 0.10f;
        public const float JUMP_BUFFER_TIME = 0.10f;
        public const float BOUNCE_PAD_VELOCITY = 28.75f; // 약 6.5타일 상승
        // 2026-11 튜닝(사용자 결정): 발사 후 0.25초만 수평 속도 유지, 이후 AIR_DECEL * 0.4로 서서히 감속 (원안 20.0, 21.25)
        public static readonly Vector2 SIDE_BOUNCE_VEC = new Vector2(13.5f, 15.0f); // Y 15: Room 5 R(row 3) 발사 시 천장 안 닿고 G(row 4) 착지 (사용자 허용 범위 14~16)
        public const float SIDE_BOUNCE_HOLD_TIME = 0.25f;
        public const float SIDE_BOUNCE_DRAG_MULT = 0.4f;
        public const float GEAR_PLATFORM_SPEED = 3.0f;  // 초당 3타일 왕복
        // 앰버 상승 기류(U): AMBER + 히트박스가 U 칸과 겹치면 중력 대신 위로 가속, 상승 속도 상한 7.5 (상한보다 빠르면 같은 가속으로 7.5까지 감속)
        public const float UPDRAFT_ACCEL = 85.0f;
        public const float UPDRAFT_MAX_SPEED = 7.5f;

        // ───────── 6. 메커니즘 연출/보정 상수 ─────────
        public const float WAVE_DURATION = 0.2f;        // 원형 파동 확장 시간
        public const float WAVE_MAX_RADIUS = 25.0f;     // 0 → 25 Units
        public const float OVERLAP_UP_SNAP_MAX = 0.375f; // 6 px 이하면 위로 올려 착지
        public const float RESPAWN_DELAY = 0.5f;        // 사망 후 리스폰 대기 (리스폰 시 팔레트는 MONOCHROME으로 초기화)
        public const float HAZARD_HITBOX_SIZE = 0.5f;
        /// <summary>대각선 패드(R) 트리거: 가로 24px(스프라이트보다 좌우 4px씩 넓게) × 세로 14px — 상승 중 수평 이동에도 잡히도록</summary>
        public static readonly Vector2 SIDE_BOUNCE_TRIGGER_SIZE = new Vector2(1.5f, 0.875f);
        /// <summary>R 발사 직후 가시 판정 무시 시간 (BouncePad > Hazard)</summary>
        public const float SIDE_BOUNCE_HAZARD_GRACE = 0.1f;
        /// <summary>코너 슬립 보정: 블록 모서리에 이만큼(3px) 이하로 걸리면 멈추지 않고 비켜 지나간다</summary>
        public const float CORNER_NUDGE_MAX = 0.2f;
        // 낙하 중 턱 보조: 마지막으로 서 있던 높이보다 LEDGE_ASSIST_MIN_RISE 이상 높은 발판 모서리에
        // 발이 LEDGE_ASSIST_MAX(8px) 이하로 못 미치면 올려 세워 준다. (같은 높이 홈은 건너뛰지 않음)
        public const float LEDGE_ASSIST_MAX = 0.5f;
        public const float LEDGE_ASSIST_MIN_RISE = 0.5f;

        // ───────── Game Juice (GDD 7.6) ─────────
        public const float POP_IN_SCALE = 1.18f;        // 실체화 순간 시작 스케일
        public const float POP_IN_DURATION = 0.08f;     // → 1.0까지 수축 시간
        public const float GHOST_INTERVAL = 0.035f;     // 잔상 생성 간격
        public const float GHOST_LIFETIME = 0.15f;      // 잔상 페이드아웃
        public const float GHOST_ALPHA = 0.45f;
        public const float GHOST_MIN_SPEED = 10f;       // 패드 발사 중 잔상이 나오는 최소 속도 (기류 안에서는 속도 무관)
        public const int AMBIENT_MOTE_COUNT = 16;
        public const float HITSTOP_PAD = 0.03f;
        public const float SHAKE_PAD_TIME = 0.1f;    public const int SHAKE_PAD_PX = 2;
        public const float SHAKE_SWITCH_TIME = 0.06f; public const int SHAKE_SWITCH_PX = 1;
        public const float SHAKE_DEATH_TIME = 0.15f;  public const int SHAKE_DEATH_PX = 3;
   // 가시 Trigger 8x8 px (2026-11: 12px→8px로 더 관대하게)
        public const float CRIMSON_TINT_ALPHA = 0.25f;  // CRIMSON 상태 배경 틴트 (#750518, 25%)
        public const float AMBER_TINT_ALPHA = 0.22f;    // AMBER 상태 배경 틴트 (#D4A359, 22%)
        public const int CHAPTER_SIZE = 5;              // Room 1~5 = STAGE 1-n, Room 6~10 = STAGE 2-n
        public const int AMBER_UNLOCK_ROOM = 6;         // 이 Room의 앰버 코어(@)로 E 해금, 이후 Room은 해금 상태로 시작
        public const float BG_VARIANT_CHANCE = 0.18f;   // 배경 벽돌 변종(금/점/덩굴) 비율
        public const float ROOM_TRANSITION_DELAY = 0.4f; // 출구 도달 후 다음 Room 로드까지

        // ───────── Landing Squash (시각 연출 전용 — 히트박스/물리와 무관, 2026-11-18) ─────────
        // 자식 Visual의 localScale만 정수 px 단계로 바꾼다. scale = (폭px / 12, 높이px / 16), 피벗 = 발바닥 중앙.
        public const int LAND_BODY_W_PX = 12;              // 슬라임 몸통 기준 폭 (히트박스와 같음)
        public const int LAND_BODY_H_PX = 16;              // 슬라임 몸통 기준 높이
        // 2026-11-20: 착지는 Land 3프레임 스프라이트로 표현. localScale 눌림은 끔 (단계 계산 LastLandTier는 유지 → PlayerAnimator가 사용)
        public static readonly bool LAND_USE_SCALE_SQUASH = false;
        public const int WALL_NUDGE_MAX_PX = 2;            // 벽 앞 시각 보정 최대 px (PlayerController2D.WallNudgePx)
        public const float LAND_MIN_FALL_SPEED = 2f;       // 이 속도(u/s) 이하로 내려앉으면 연출 없음
        // 단계 판정 1) 낙하 거리 h = 공중 최고점 − 착지 y (Unit)
        public const float LAND_TIER2_MIN_DROP = 1.25f;    // h < 1.25 → 1px (턱, 탭 점프)
        public const float LAND_TIER3_MIN_DROP = 2.5f;     // 1.25 ≤ h < 2.5 → 2px
        public const float LAND_TIER4_MIN_DROP = 3.5f;     // 2.5 ≤ h < 3.5 → 3px (평지 풀점프 3.25), h ≥ 3.5 또는 패드 발사 후 → 4px
        // 단계 판정 2) 착지 직전 낙하 속도 |vy| 상한 (기류 등으로 천천히 내려앉은 경우를 약하게)
        public const float LAND_CAP1_SPEED = 8f;           // |vy| < 8 → 최대 1px
        public const float LAND_CAP2_SPEED = 13f;          // |vy| < 13 → 최대 2px
        public const int LAND_MAX_WIDEN_PX = 2;            // 폭 증가 상한 (좌우 1px씩, 벽에 붙으면 열린 쪽으로만 2px)

        public readonly struct LandSquashKey
        {
            public readonly int widthPx, heightPx;
            public readonly float duration; // 초 (프레임레이트 무관)
            public LandSquashKey(int widthPx, int heightPx, float duration)
            { this.widthPx = widthPx; this.heightPx = heightPx; this.duration = duration; }
        }

        /// <summary>단계(1~4px)별 키프레임: 눌림 유지 → 중간 → 반동(스트레치) → 원래 모양(배열 끝)
        /// 2026-11-18: 폭 14px 단계는 nearest 열 복제로 한쪽 눈이 굵어져(짝짝이) 폭을 12로 고정. 옆으로 퍼지는 눌림은 PixelLab 착지 전용 프레임으로 대체 예정.</summary>
        public static readonly LandSquashKey[][] LAND_SQUASH_KEYS =
        {
            new[] { new LandSquashKey(12, 15, 0.04f) },
            new[] { new LandSquashKey(12, 14, 0.05f), new LandSquashKey(12, 15, 0.03f), new LandSquashKey(12, 17, 0.05f) },
            new[] { new LandSquashKey(12, 13, 0.06f), new LandSquashKey(12, 15, 0.03f), new LandSquashKey(12, 17, 0.06f) },
            new[] { new LandSquashKey(12, 12, 0.08f), new LandSquashKey(12, 14, 0.04f), new LandSquashKey(12, 18, 0.07f), new LandSquashKey(12, 17, 0.03f) },
        };

        // ───────── UI ─────────
        public const string GAME_TITLE = "PALETTE SHAPER";
        public const string UI_FONT_GLYPHS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-:.! ";
        public const int HUD_BAR_HEIGHT_PX = 12;

        // ───────── 7. 레벨 데이터 심볼 ─────────
        public const char SYM_EMPTY = '.';
        public const char SYM_STONE = '#';
        public const char SYM_PLAYER_SPAWN = 'P';
        public const char SYM_EXIT = 'E';
        public const char SYM_CORE = '*';
        public const char SYM_CRIMSON_BLOCK = 'C';
        public const char SYM_CRIMSON_WALL = 'W';
        public const char SYM_CRIMSON_SPIKE = 'X';
        public const char SYM_BOUNCE_VERTICAL = 'B';
        public const char SYM_BOUNCE_DIAGONAL = 'R';
        public const char SYM_GEAR_START = 'G';
        public const char SYM_GEAR_RAIL = '=';
        public const char SYM_AMBER_CORE = '@';
        public const char SYM_AMBER_BLOCK = 'A';
        public const char SYM_AMBER_SPIKE = 'Y';
        public const char SYM_AMBER_UPDRAFT = 'U';

        public static readonly string[][] ROOM_MAPS = new string[][]
        {
            // Room 1: 출구(E)를 row 2로 내리고 row 1을 비워 석문(16x32) 노출 및 진입 헤드룸 확보 (2026-11 수정)
            new string[] {
                "####################",
                "#..................#",
                "#.................E#",
                "#..............CC###",
                "#............CC..###",
                "#.......*..CC....###",
                "#......####......###",
                "#....##..........###",
                "#P.##............###",
                "#####...############",
                "####################"
            },
            // Room 2: 실체와 환영 (중앙 크림슨 장벽 'W'를 흑백으로 통과 후, 크림슨 징검다리 'C' 활성화)
            new string[] {
                "####################",
                "#.......WW.........#",
                "#.......WW........E#",
                "#.......WW.......###",
                "#.......WW....CC.###",
                "#.......WW..CC...###",
                "#P......WWCC.....###",
                "#########........###",
                "#########.#......###",
                "#########........###",
                "####################"
            },
            // Room 3: 공중에서의 선택 (체공 중 흑백 전환해 가시 'X' 통과 -> 낙하 중 크림슨 켜서 'C' 착지)
            // 2026-11 수정: 전체를 2줄 아래로(천장 박치기 제거로 체공 시간 확보), X는 천장까지 연결
            new string[] {
                "####################",
                "#...........XX.....#",
                "#...........XX.....#",
                "#...........XX.....#",
                "#...........XX.....#",
                "#...........XX....E#",
                "#.......CC..XX...###", // 2026-11 난이도 재조정: C2를 한 칸 왼쪽(col 8~9)으로 → 가시까지 거리↑ (첫 Q 여유 0.22→0.26초, 두 번째 Q 0.24→0.18초)
                "#....CC..........###", // 2026-11 난이도 완화: 맨 아래 가시 칸 제거 (내린 착지 발판에 서도 안 겹치게)
                "#P............CCC###", // 2026-11 난이도 완화: 착지 C를 한 칸 내리고 col 14~16 3칸 (공중 Q 창 0.14~0.26초 → 0.20~0.32초)
                "###..............###",
                "###..............###"
            },
            // Room 4: G를 row 3으로 내리고 레일 연장(G========), E를 row 2로 내려 천장 헤드룸 2칸 확보 (2026-11 수정)
            new string[] {
                "####################",
                "#..................#",
                "#.................E#",
                "#......G========.###",
                "#................###",
                "#................###",
                "#................###",
                "#................###",
                "#P...B...........###",
                "########.........###",
                "########............"
            },
            // Room 5: B는 row 9 홈, 가시 row 5, R row 3, G row 4, E row 3 (2026-11 수정)
            // 원본 메시지의 row 3은 19글자였어서 E가 col 18(벽 바로 왼쪽)에 오도록 점 하나를 보충
            new string[] {
                "####################",
                "#..................#",
                "#..................#",
                "#...R.............E#",
                "#..XXXX..G=====..###", // 2026-11 확정: X row 4, G col 9 + 레일 col 10~14
                "#................###",
                "#................###",
                "#................###",
                "#P...............###",
                "####B##..........###",
                "#######............."
            },
            // ───── Chapter 2: AMBER ─────
            // Room 6 (STAGE 2-1): 황금빛 각성 — 크림슨 계단 C → 앰버 코어 @ → E로 A·U 켜서 출구
            new string[] {
                "####################",
                "#..................#",
                "#.................E#", // 2026-11-20 사용자 요청: 기류 맨 위 칸(row 2) 제거 → 천장에 붙지 않고 2-3처럼 둥둥 뜸 (턱 여유는 +0.02~0.10칸으로 빠듯 — 플레이 확인용)
                "#..............U.###",
                "#..........AA..U.###",
                "#.......@......U.###",
                "#......####....U.###",
                "#....CC####....U.###",
                "#P.CC..####....U.###",
                "#####..#########.#.#",
                "####################"
            },
            // Room 7 (STAGE 2-2): 교차하는 다리 — C → (E) A → (Q) C, 이색 가시 장벽 X·Y
            new string[] {
                "####################",
                "#.........X....Y...#", // 2026-11 난이도 조정: 가시 기둥 X(col 10)·Y(col 15)를 1칸 폭으로
                "#.........X....Y...#", //   → 발판 끝에서 뛰어도 공중 전환 여유 0.10초 → 0.28초 (두 구간 모두)
                "#.........X....Y..E#",
                "#.........X....Y.###",
                "#.........AAA..YCC##", // 2026-11: A를 가시 밑 col 10까지 확장 (짧은 점프가 A 옆면에 걸려 수직 낙하하던 문제 → 틈 3칸→2칸)
                "#.....CC.......Y.###", //          A 아래에 매달리던 X 한 칸은 제거
                "#...AA.............#", // 2026-11-18: 첫 AA를 col 4~5로 (시작 턱에서 걸어 내려갈 때 A 밑 모서리에 머리가 걸리던 문제), CC는 col 6~7 유지
                "#P.................#",
                "###................#",
                "####################"
            },
            // Room 8 (STAGE 2-3): 바람과 붉은 가시 — AMBER로 기류 상승(크림슨 X 무해) → A → (Q) C → 출구
            new string[] {
                "####################",
                "#..................#",
                "#.................E#",
                "#.............CC.###", // 2026-11 봇 튜닝: C를 col 14~15로 (C→출구 2칸 틈은 천장 때문에 점프 비거리 1.44칸 vs 필요 1.25칸 → 프레임 단위 타이밍)
                "#....UUAAAAAA....###", // 2026-11: A를 col 7~12로 (원안 col 11~12는 기류 탈출 수평 1.44칸으로 불가, col 8~12도 정지 상태 출발이면 0.015칸 모자람 → 기류 바로 옆에 붙임)
                "#....UU..YY......###",
                "#....XXXX........###",
                "#....UU..........###",
                "#P...UU..........###",
                "#####UU#############",
                "####################"
            },
            // Room 9 (STAGE 2-4): 탄성과 부유 — B(CRIMSON)로 Y 통과 → 최고점에서 E → 기류 → A → 출구
            new string[] {
                "####################",
                "#..................#",
                "#.................E#",
                "#........UU.AAAA.###", // 2026-11 확정: A를 col 12~15로 (기류→A 여유 확보), G 없음
                "#........UU......###",
                "#..YYYY..UU......###",
                "#........UU......###",
                "#........UU......###",
                "#P..B............###",
                "#######..........###",
                "#######............."
            },
            // Room 10 (STAGE 2-5): 이중주 — E로 기류 진입(X 무해) → R 위에서 Q 발사 → 공중에서 E → A 착지 → 출구
            new string[] {
                "####################",
                "#..................#",
                "#..................#",
                "#....R............E#",
                "#....U.....AAAA..###", // 2026-11 확정: A를 col 11~14로 (R 비거리 약 6칸)
                "#..XXXX..YY......###",
                "#....U...YY......###",
                "#....U...........###",
                "#P...U...........###", // 2026-11 확정: B 제거 (B→X 0.087초, 기류로 진입)
                "#######..........###",
                "#######............."
            }
        };
    }
}
