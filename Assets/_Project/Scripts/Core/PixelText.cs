using System.Collections.Generic;
using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// 5x7 도트 폰트(Spr_UI_Font, 6x8 칸) 글리프를 SpriteRenderer로 늘어놓는 간단한 픽셀 텍스트.
    /// 글리프 순서는 GameConstants.UI_FONT_GLYPHS. 지원하지 않는 문자는 공백.
    /// </summary>
    public class PixelText : MonoBehaviour
    {
        public enum Align { Left, Center, Right }

        const float Advance = 6f / GameConstants.PIXELS_PER_UNIT; // 글자 간격 6px

        [SerializeField] Sprite[] glyphs;
        [SerializeField] string text = "";
        [SerializeField] Align align = Align.Left;
        [SerializeField] Color color = Color.white;
        [SerializeField] string sortingLayer = GameConstants.SORT_UI;
        [SerializeField] int sortingOrder = 10;

        readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();

        public string Text => text;

        public void Configure(Sprite[] fontGlyphs, Align alignment, int order)
        {
            glyphs = fontGlyphs;
            align = alignment;
            sortingOrder = order;
            Rebuild();
        }

        public void SetText(string value)
        {
            value = (value ?? "").ToUpperInvariant();
            if (value == text && pool.Count > 0) return;
            text = value;
            Rebuild();
        }

        public void SetColor(Color c)
        {
            color = c;
            foreach (var sr in pool) sr.color = c;
        }

        void Start() => Rebuild();

        void Rebuild()
        {
            if (glyphs == null || glyphs.Length == 0) return;
            while (pool.Count < text.Length)
            {
                var go = new GameObject("Glyph");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                pool.Add(sr);
            }

            float width = text.Length * Advance;
            float start = align == Align.Left ? 0f : align == Align.Center ? -width * 0.5f : -width;
            for (int i = 0; i < pool.Count; i++)
            {
                var sr = pool[i];
                bool used = i < text.Length;
                sr.gameObject.SetActive(used);
                if (!used) continue;
                int index = GameConstants.UI_FONT_GLYPHS.IndexOf(text[i]);
                if (index < 0) index = GameConstants.UI_FONT_GLYPHS.IndexOf(' ');
                sr.sprite = index < glyphs.Length ? glyphs[index] : null;
                sr.color = color;
                sr.sortingLayerName = sortingLayer;
                sr.sortingOrder = sortingOrder;
                // 글리프 피벗 = 6x8 칸 중앙 → 칸 왼쪽 + 3px
                sr.transform.localPosition = new Vector3(start + i * Advance + Advance * 0.5f, 0f, 0f);
            }
        }
    }
}
