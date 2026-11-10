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

        [Header("Tiles")]
        public TileBase tileBaseStone;
        public TileBase tileBaseStoneEdgeRight;
        public TileBase tileBaseStoneEdgeBottom;
        public TileBase tileBaseStoneEdgeBoth;
        public TileBase tileCrimsonSolid;
        public TileBase tileCrimsonSolidInactive;

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

        [Header("VFX")]
        public Material waveMaterial;
    }
}
