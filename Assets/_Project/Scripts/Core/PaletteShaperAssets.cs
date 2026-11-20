using UnityEngine;
using UnityEngine.Tilemaps;

namespace PaletteShaper.Core
{
    /// <summary>
    /// 런타임에서 쓰는 스프라이트/타일/머티리얼 참조 묶음.
    /// "Palette Shaper/4. Create Runtime Assets" 메뉴가 Assets/_Project/Data/PaletteShaperAssets.asset 으로 생성·갱신한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Palette Shaper/Assets Database", fileName = "PaletteShaperAssets")]
    public class PaletteShaperAssets : ScriptableObject
    {
        [Header("Player (24x24, Bottom-Center)")]
        public Sprite[] playerIdle;
        public Sprite[] playerRun;
        public Sprite[] playerJump;
        public Sprite[] playerFall;
        public Sprite[] playerLand;
        public Sprite[] playerTurn;
        [Tooltip("잔상 실루엣 20프레임 (Idle4+Run6+Jump2+Fall2+Land3+Turn3 순서)")]
        public Sprite[] playerGhostCrimson;
        public Sprite[] playerGhostAmber;
        public Sprite[] playerGhostMono;

        [Header("Tiles")]
        public TileBase tileBaseStone;
        public TileBase tileBaseStoneEdgeRight;
        public TileBase tileBaseStoneEdgeBottom;
        public TileBase tileBaseStoneEdgeBoth;
        public TileBase tileCrimsonSolid;
        public TileBase tileCrimsonSolidInactive;
        public TileBase tileAmberSolid;
        public TileBase tileAmberSolidInactive;

        [Header("Gimmicks")]
        public Sprite[] crimsonCore;
        public Sprite spikeActive;
        public Sprite spikeInactive;
        public Sprite bouncePadInactive;
        public Sprite bouncePadActive;
        public Sprite sideBouncePadInactive;
        public Sprite sideBouncePadActive;
        public Sprite gearPlatformActive;
        public Sprite gearPlatformInactive;
        public Sprite railTrack;

        [Header("Gimmicks - Amber (Chapter 2)")]
        public Sprite[] amberCore;
        public Sprite amberSpikeActive;
        public Sprite amberSpikeInactive;
        [Tooltip("활성 상승 기류 4프레임 루프")]
        public Sprite[] amberUpdraft;
        public Sprite amberUpdraftInactive;

        [Header("Props / BG")]
        public Sprite exitDoor;
        [Tooltip("0 = 기본 벽돌, 1~3 = 금/점/덩굴 변종")]
        public TileBase[] bgWallTiles;

        [Header("UI")]
        [Tooltip("순서 = GameConstants.UI_FONT_GLYPHS")]
        public Sprite[] uiFont;
        public Sprite uiBar;
        [Tooltip("0 = 잠김, 1 = MONOCHROME, 2 = CRIMSON")]
        public Sprite[] uiPaletteGauge;
        [Tooltip("Q 슬롯: 0 = 잠김, 1 = 꺼짐, 2 = CRIMSON 켜짐")]
        public Sprite[] uiSlotCrimson;
        [Tooltip("E 슬롯: 0 = 잠김, 1 = 꺼짐, 2 = AMBER 켜짐")]
        public Sprite[] uiSlotAmber;

        [Header("VFX")]
        public Material waveMaterial;
    }
}
