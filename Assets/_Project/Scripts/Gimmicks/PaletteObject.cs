using PaletteShaper.Core;
using PaletteShaper.Player;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace PaletteShaper.Gimmicks
{
    /// <summary>
    /// 팔레트 상태에 따라 모습이 바뀌는 색 오브젝트(크림슨/앰버)의 공통 베이스.
    /// - 자기 색(ownColor)이 켜짐: 활성 스프라이트/타일 + Gimmick_Active 레이어
    /// - 그 외(MONOCHROME 또는 다른 색): 점선 스프라이트/타일 + Gimmick_Inactive 레이어
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
        [Tooltip("이 오브젝트가 실체화되는 색 (기본 CRIMSON)")]
        [SerializeField] PaletteState ownColor = PaletteState.CRIMSON;
        [SerializeField] bool swapSortingLayer = true;
        [SerializeField] Collider2D[] collidersEnabledOnlyInCrimson;

        protected PaletteManager Manager { get; private set; }
        float popTimer = -1f;
        public bool IsCrimson => Manager != null && Manager.IsCrimson;
        public PaletteState OwnColor => ownColor;
        /// <summary>자기 색이 켜져 있는지 (크림슨 오브젝트는 IsCrimson과 같음)</summary>
        public bool IsActive => Manager != null && Manager.State == ownColor;

        /// <summary>Configure* 호출 전에 지정한다.</summary>
        public void SetOwnColor(PaletteState color)
        {
            ownColor = color;
            Refresh();
        }

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
            if (state == ownColor) popTimer = 0f; // 실체화 탄성 팝업 시작 (GDD 7.6-1)
            else if (popTimer >= 0f) { popTimer = -1f; ApplyPopScale(1f); }
            OnPaletteApplied(state, false);
        }

        /// <summary>
        /// 실체화 탄성 팝업: POP_IN_SCALE(1.18) → 1.0을 POP_IN_DURATION(0.08초) 동안.
        /// 표시용 자식 스프라이트나 표시 전용 타일맵만 키우고, 충돌 판정(부모 콜라이더·충돌 타일맵)은 건드리지 않는다.
        /// </summary>
        protected virtual void Update()
        {
            if (popTimer < 0f) return;
            popTimer += Time.deltaTime;
            float k = Mathf.Clamp01(popTimer / GameConstants.POP_IN_DURATION);
            float ease = 1f - (1f - k) * (1f - k); // ease-out
            ApplyPopScale(Mathf.Lerp(GameConstants.POP_IN_SCALE, 1f, ease));
            if (k >= 1f) popTimer = -1f;
        }

        void ApplyPopScale(float s)
        {
            if (spriteRenderer != null && spriteRenderer.transform != transform)
                spriteRenderer.transform.localScale = new Vector3(s, s, 1f);
            if (tilemap != null && tilemap.GetComponent<Collider2D>() == null)
            {
                var m = Matrix4x4.Scale(new Vector3(s, s, 1f));
                foreach (var cell in tilemap.cellBounds.allPositionsWithin)
                    if (tilemap.HasTile(cell)) tilemap.SetTransformMatrix(cell, m);
            }
        }

        /// <summary>상태 반영 후 호출. initial=true는 최초 바인딩 시.</summary>
        protected virtual void OnPaletteApplied(PaletteState state, bool initial) { }

        protected void Refresh()
        {
            ApplyVisuals(Manager != null ? Manager.State : PaletteState.MONOCHROME);
        }

        void ApplyVisuals(PaletteState state)
        {
            bool on = state == ownColor;
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
