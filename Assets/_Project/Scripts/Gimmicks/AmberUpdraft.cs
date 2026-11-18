using System.Collections.Generic;
using PaletteShaper.Core;
using UnityEngine;

namespace PaletteShaper.Gimmicks
{
    /// <summary>
    /// 앰버 상승 기류(U) 1칸. AMBER 상태에서만 작동한다.
    /// 플레이어 히트박스가 U 칸과 겹치고 AMBER가 켜져 있으면, PlayerController2D가 중력 대신
    /// velocity.y를 UPDRAFT_ACCEL로 UPDRAFT_MAX_SPEED(7.5)까지 끌어올린다(빠르면 같은 가속으로 7.5까지 감속).
    /// 영역을 벗어나거나 다른 색으로 바꾸면 즉시 일반 중력.
    /// 세로로 이어진 U는 칸마다 같은 애니메이션 위상이라 하나의 기류 기둥처럼 보인다.
    /// 판정은 트리거가 아니라 칸 영역(Rect) 겹침으로 계산 → 물리 콜백 순서와 무관하게 결정적.
    /// </summary>
    public class AmberUpdraft : PaletteObject
    {
        static readonly List<AmberUpdraft> all = new List<AmberUpdraft>();

        [SerializeField] Sprite[] activeFrames;
        [SerializeField] float fps = 10f;

        Rect area;

        public Rect Area => area;

        public void ConfigureUpdraft(SpriteRenderer target, Sprite[] frames, Sprite inactive, Rect cellArea)
        {
            activeFrames = frames;
            area = cellArea;
            SetOwnColor(PaletteState.AMBER);
            ConfigureSprite(target, frames != null && frames.Length > 0 ? frames[0] : null, inactive);
        }

        /// <summary>AMBER가 켜져 있고 bounds가 어떤 기류 칸과 겹치면 true.</summary>
        public static bool Affects(Bounds bounds)
        {
            if (!PaletteManager.IsAmberNow) return false;
            const float eps = 0.001f;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null) continue;
                Rect r = u.area;
                if (bounds.max.x > r.xMin + eps && bounds.min.x < r.xMax - eps &&
                    bounds.max.y > r.yMin + eps && bounds.min.y < r.yMax - eps)
                    return true;
            }
            return false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!all.Contains(this)) all.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            all.Remove(this);
        }

        protected override void Update()
        {
            base.Update();
            if (!IsActive || spriteRenderer == null || activeFrames == null || activeFrames.Length == 0) return;
            // 모든 기류 칸이 같은 시계를 써서 세로 기둥 애니메이션이 이어지게
            int frame = (int)(Time.time * fps) % activeFrames.Length;
            spriteRenderer.sprite = activeFrames[frame];
        }
    }
}
