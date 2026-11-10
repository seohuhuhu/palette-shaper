using PaletteShaper.Core;
using PaletteShaper.Player;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace PaletteShaper.Gimmicks
{
    /// <summary>
    /// 팔레트 상태에 따라 모습이 바뀌는 크림슨 오브젝트의 공통 베이스.
    /// - CRIMSON: 활성 스프라이트/타일 + Gimmick_Active 레이어
    /// - MONOCHROME: 점선 스프라이트/타일 + Gimmick_Inactive 레이어
    /// 충돌 ON/OFF 자체는 PaletteManager의 레이어 충돌 토글이 담당하고,
    /// 필요한 경우(톱니 발판) collidersEnabledOnlyInCrimson으로 콜라이더를 직접 끈다.
    /// </summary>
    public class PaletteObject : MonoBehaviour
    {
        [Header("Sprite 교체")]
        [SerializeField] protected SpriteRenderer spriteRenderer;
        [SerializeField] protected Sprite activeSprite;
        [SerializeField] protected Sprite inactiveSprite;

        [Header("Tilemap 교체")]
        [SerializeField] Tilemap tilemap;
        [SerializeField] TilemapRenderer tilemapRenderer;
        [SerializeField] TileBase activeTile;
        [SerializeField] TileBase inactiveTile;

        [Header("공통")]
        [SerializeField] bool swapSortingLayer = true;
        [SerializeField] Collider2D[] collidersEnabledOnlyInCrimson;

        protected PaletteManager Manager { get; private set; }
        public bool IsCrimson => Manager != null && Manager.IsCrimson;

        public void ConfigureSprite(SpriteRenderer target, Sprite active, Sprite inactive)
        {
            spriteRenderer = target;
            activeSprite = active;
            inactiveSprite = inactive;
            Refresh();
        }

        public void ConfigureTilemap(Tilemap target, TilemapRenderer targetRenderer, TileBase active, TileBase inactive)
        {
            tilemap = target;
            tilemapRenderer = targetRenderer;
            activeTile = active;
            inactiveTile = inactive;
            Refresh();
        }

        public void SetCrimsonOnlyColliders(params Collider2D[] colliders)
        {
            collidersEnabledOnlyInCrimson = colliders;
            Refresh();
        }

        protected virtual void OnEnable() => Bind();
        protected virtual void Start() => Bind();

        protected virtual void OnDisable()
        {
            if (Manager != null) Manager.PaletteChanged -= HandlePaletteChanged;
            Manager = null;
        }

        void Bind()
        {
            if (Manager != null) return;
            var m = PaletteManager.Instance;
            if (m == null) return;
            Manager = m;
            m.PaletteChanged += HandlePaletteChanged;
            ApplyVisuals(m.State);
            OnPaletteApplied(m.State, true);
        }

        void HandlePaletteChanged(PaletteState state)
        {
            ApplyVisuals(state);
            OnPaletteApplied(state, false);
        }

        /// <summary>상태 반영 후 호출. initial=true는 최초 바인딩 시.</summary>
        protected virtual void OnPaletteApplied(PaletteState state, bool initial) { }

        protected void Refresh()
        {
            ApplyVisuals(Manager != null ? Manager.State : PaletteState.MONOCHROME);
        }

        void ApplyVisuals(PaletteState state)
        {
            bool on = state == PaletteState.CRIMSON;
            string layer = on ? GameConstants.SORT_GIMMICK_ACTIVE : GameConstants.SORT_GIMMICK_INACTIVE;

            if (spriteRenderer != null)
            {
                var target = on ? activeSprite : inactiveSprite;
                if (target != null) spriteRenderer.sprite = target;
                if (swapSortingLayer) spriteRenderer.sortingLayerName = layer;
            }

            if (tilemap != null && activeTile != null && inactiveTile != null)
            {
                if (on) tilemap.SwapTile(inactiveTile, activeTile);
                else tilemap.SwapTile(activeTile, inactiveTile);
                if (swapSortingLayer && tilemapRenderer != null) tilemapRenderer.sortingLayerName = layer;
            }

            if (collidersEnabledOnlyInCrimson != null)
            {
                foreach (var c in collidersEnabledOnlyInCrimson)
                    if (c != null) c.enabled = on;
            }
        }

        protected static PlayerController2D PlayerOf(Collider2D other)
        {
            if (other == null) return null;
            var body = other.attachedRigidbody;
            return body != null ? body.GetComponent<PlayerController2D>() : null;
        }
    }
}
