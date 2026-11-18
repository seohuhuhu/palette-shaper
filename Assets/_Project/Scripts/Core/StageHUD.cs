using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// 상단 인게임 HUD 바 (320x12 px, 카메라 상단 고정, UI Sorting Layer).
    /// 왼쪽: [Q: CRIMSON] [E: AMBER] 슬롯 (각각 잠김 / 꺼짐 / 켜짐)
    /// 가운데: 게임 타이틀, 오른쪽: STAGE 1-n
    /// 화면 중앙 메시지(예: ALL CLEAR!)도 표시한다.
    /// 모든 요소가 320x180 렌더 텍스처 안에 그려져 픽셀 크기가 게임과 같다.
    /// </summary>
    public class StageHUD : MonoBehaviour
    {
        [SerializeField] PaletteShaperAssets assets;
        [SerializeField] Camera targetCamera;

        SpriteRenderer slotCrimson;
        SpriteRenderer slotAmber;
        PixelText title;
        PixelText stage;
        PixelText centerMessage;
        bool built;

        const float Px = 1f / GameConstants.PIXELS_PER_UNIT;

        public void Configure(PaletteShaperAssets db, Camera cam)
        {
            assets = db;
            targetCamera = cam;
        }

        void Start() => Build();

        void Build()
        {
            if (built || assets == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;
            built = true;

            transform.SetParent(targetCamera.transform, false);
            float halfH = GameConstants.REF_RESOLUTION_Y * 0.5f * Px;  // 5.625
            float halfW = GameConstants.REF_RESOLUTION_X * 0.5f * Px;  // 10
            float barH = GameConstants.HUD_BAR_HEIGHT_PX * Px;         // 0.75
            float barY = halfH - barH * 0.5f;
            transform.localPosition = new Vector3(0f, 0f, 10f);         // 카메라 앞(z=0 평면)

            // 바 배경
            var bar = new GameObject("Bar").AddComponent<SpriteRenderer>();
            bar.transform.SetParent(transform, false);
            bar.transform.localPosition = new Vector3(0f, barY, 0f);
            bar.sprite = assets.uiBar;
            bar.drawMode = SpriteDrawMode.Tiled;
            bar.size = new Vector2(halfW * 2f, barH);
            bar.sortingLayerName = GameConstants.SORT_UI;
            bar.sortingOrder = 0;

            // 팔레트 슬롯 2개 (각 26x10 px): 왼쪽 여백 3px, 슬롯 간격 4px
            slotCrimson = NewSlot("Slot_Q_Crimson", new Vector3(-halfW + (3f + 13f) * Px, barY, 0f));
            slotAmber = NewSlot("Slot_E_Amber", new Vector3(-halfW + (3f + 26f + 4f + 13f) * Px, barY, 0f));

            // 글자 (6x8 칸 중앙 피벗 → 정수 픽셀 위치 유지)
            float textY = barY;
            title = NewText("Title", new Vector3(0f, textY, 0f), PixelText.Align.Center);
            title.SetText(GameConstants.GAME_TITLE);
            stage = NewText("Stage", new Vector3(halfW - 3f * Px, textY, 0f), PixelText.Align.Right);
            centerMessage = NewText("CenterMessage", new Vector3(0f, 0.5f, 0f), PixelText.Align.Center);
            centerMessage.SetColor(GameConstants.LIGHT_CORAL);
            centerMessage.SetText("");
            Refresh();
        }

        SpriteRenderer NewSlot(string name, Vector3 localPos)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.transform.localPosition = localPos;
            sr.sortingLayerName = GameConstants.SORT_UI;
            sr.sortingOrder = 5;
            return sr;
        }

        PixelText NewText(string name, Vector3 localPos, PixelText.Align align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var t = go.AddComponent<PixelText>();
            t.Configure(assets.uiFont, align, 10);
            t.SetColor(GameConstants.CREAM_IVORY);
            return t;
        }

        public void SetStage(string label)
        {
            Build();
            if (stage != null) stage.SetText(label);
        }

        public void ShowCenterMessage(string message)
        {
            Build();
            if (centerMessage != null) centerMessage.SetText(message);
        }

        void LateUpdate() => Refresh();

        void Refresh()
        {
            var pm = PaletteManager.Instance;
            SetSlot(slotCrimson, assets.uiSlotCrimson, pm, PaletteState.CRIMSON);
            SetSlot(slotAmber, assets.uiSlotAmber, pm, PaletteState.AMBER);
        }

        /// <summary>0 = 잠김, 1 = 해금됐지만 꺼짐, 2 = 켜짐</summary>
        public static int SlotFrame(PaletteManager pm, PaletteState color) =>
            pm == null || !pm.IsUnlocked(color) ? 0 : pm.State == color ? 2 : 1;

        static void SetSlot(SpriteRenderer slot, Sprite[] frames, PaletteManager pm, PaletteState color)
        {
            if (slot == null || frames == null || frames.Length < 3) return;
            slot.sprite = frames[SlotFrame(pm, color)];
        }
    }
}
