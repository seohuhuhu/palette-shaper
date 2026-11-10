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

        public static readonly Color32[] PALETTE =
        {
            CREAM_IVORY, WARM_GRAY, DEEP_CHARCOAL, LIGHT_CORAL, BURGUNDY_RED, DARK_WINE
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

        public const string LAYER_NAME_PLAYER         = "Player";
        public const string LAYER_NAME_TERRAIN_BASE   = "Terrain_Base";
        public const string LAYER_NAME_CRIMSON_SOLID  = "Crimson_Solid";
        public const string LAYER_NAME_CRIMSON_HAZARD = "Crimson_Hazard";

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
   // 가시 Trigger 8x8 px (2026-11: 12px→8px로 더 관대하게)
        public const float CRIMSON_TINT_ALPHA = 0.25f;  // CRIMSON 상태 배경 틴트 (#750518, 25%)
        public const float BG_VARIANT_CHANCE = 0.18f;   // 배경 벽돌 변종(금/점/덩굴) 비율
        public const float ROOM_TRANSITION_DELAY = 0.4f; // 출구 도달 후 다음 Room 로드까지

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
                "#........CC.XX...###",
                "#....CC.....XXCC.###",
                "#P...............###",
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
            }
        };
    }
}
