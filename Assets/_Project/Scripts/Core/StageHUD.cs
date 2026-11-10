using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// 상단 인게임 HUD 바 (320x12 px, 카메라 상단 고정, UI Sorting Layer).
    /// 왼쪽: 팔레트 아이콘 + 6색 게이지 (잠김 / MONOCHROME / CRIMSON)
    /// 가운데: 게임 타이틀, 오른쪽: STAGE 1-n
    /// 화면 중앙 메시지(예: ALL CLEAR!)도 표시한다.
    /// 모든 요소가 320x180 렌더 텍스처 안에 그려져 픽셀 크기가 게임과 같다.
    /// </summary>
    public class StageHUD : MonoBehaviour
    {
        [SerializeField] PaletteShaperAssets assets;
        [SerializeField] Camera targetCamera;

        SpriteRenderer gauge;
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

            // 팔레트 게이지 (44x10 px, 왼쪽 여백 3px)
            gauge = new GameObject("PaletteGauge").AddComponent<SpriteRenderer>();
            gauge.transform.SetParent(transform, false);
            gauge.transform.localPosition = new Vector3(-halfW + (3f + 22f) * Px, barY, 0f);
            gauge.sortingLayerName = GameConstants.SORT_UI;
            gauge.sortingOrder = 5;

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
            if (gauge == null || assets.uiPaletteGauge == null || assets.uiPaletteGauge.Length < 3) return;
            var pm = PaletteManager.Instance;
            int frame = pm == null || !pm.SwitchUnlocked ? 0 : pm.IsCrimson ? 2 : 1;
            gauge.sprite = assets.uiPaletteGauge[frame];
        }
    }
}
