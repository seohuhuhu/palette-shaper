using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// 색 상태의 배경 틴트 원판. CRIMSON = 버건디(#750518, 25%), AMBER = 머스터드(#D4A359, 22%).
    /// 전환 순간 플레이어 위치에서 반경 0 → 25 Units로 0.2초 동안 퍼지고 그대로 유지된다(화면 전체를 덮음).
    /// 색 → 다른 색으로 바로 바꾸면 새 색으로 원판이 0부터 다시 퍼지고, MONOCHROME으로 돌아가면 0.2초 동안 사라진다.
    /// 배경(BG_Wall)과 소품 사이에 그려서 지형·기믹·플레이어는 색이 바래지 않는다.
    /// [동기화] 매 프레임 LateUpdate에서 PaletteManager.State와 표시 상태를 대조해,
    /// 어긋나면 즉시 강제 동기화하고 경고 로그를 남긴다(표시 상태가 State와 항상 일치하도록 보장).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CrimsonTintOverlay : MonoBehaviour
    {
        const int Segments = 64;

        MeshRenderer meshRenderer;
        Mesh mesh;
        Color32 baseColor;
        PaletteState shownState = PaletteState.MONOCHROME;
        float radius;
        float alpha;
        float targetRadius;
        float targetAlpha;
        float radiusSpeed;
        float alphaSpeed;
        bool animating;

        /// <summary>현재 표시 목표 상태 (애니메이션 중이어도 목표 기준).</summary>
        public PaletteState ShownState => shownState;
        public bool ShowingCrimson => shownState == PaletteState.CRIMSON;
        /// <summary>실제로 화면에 그려지는 알파 (렌더러가 꺼져 있으면 0).</summary>
        public float VisibleAlpha => meshRenderer != null && meshRenderer.enabled ? alpha : 0f;
        public float Radius => radius;
        public bool IsAnimating => animating;
        public Color32 CurrentColor => baseColor;
        /// <summary>State와 표시가 어긋나 강제 동기화한 횟수 (테스트 검증용).</summary>
        public int DesyncCount { get; private set; }

        public static Color32 TintColor(PaletteState state) =>
            state == PaletteState.AMBER ? GameConstants.AMBER_MAIN : GameConstants.BURGUNDY_RED;

        public static float TintAlpha(PaletteState state) =>
            state == PaletteState.CRIMSON ? GameConstants.CRIMSON_TINT_ALPHA :
            state == PaletteState.AMBER ? GameConstants.AMBER_TINT_ALPHA : 0f;

        public static CrimsonTintOverlay Create(Transform parent, Material material)
        {
            var go = new GameObject("Fx_PaletteTint");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            mr.sortingLayerName = GameConstants.SORT_BG_WALL;
            mr.sortingOrder = 100; // 배경 벽돌 위, BG_Props(레일·석문) 아래
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var overlay = go.AddComponent<CrimsonTintOverlay>();
            overlay.Init();
            return overlay;
        }

        void Init()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            baseColor = TintColor(PaletteState.CRIMSON);
            mesh = BuildDisc();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            ApplyImmediate(PaletteState.MONOCHROME);
        }

        /// <summary>상태에 맞춰 틴트를 켜거나 바꾸거나 끈다. animate=false면 즉시 반영(진행 중 애니메이션도 중단).</summary>
        public void Show(PaletteState state, Vector2 center, bool animate)
        {
            if (mesh == null) Init();
            if (!animate)
            {
                if (state != PaletteState.MONOCHROME) transform.position = new Vector3(center.x, center.y, 0f);
                ApplyImmediate(state);
                return;
            }

            PaletteState previous = shownState;
            shownState = state;
            if (state != PaletteState.MONOCHROME)
            {
                transform.position = new Vector3(center.x, center.y, 0f);
                // 꺼져 있었거나 다른 색이었으면 새 색 원판이 0부터 퍼짐
                if (alpha <= 0f || previous != state) radius = 0f;
                baseColor = TintColor(state);
                targetRadius = GameConstants.WAVE_MAX_RADIUS;
                targetAlpha = TintAlpha(state);
                alpha = targetAlpha;                 // 원판이 커지면서 드러나므로 알파는 바로 목표값
            }
            else
            {
                targetRadius = radius;               // 반경은 유지한 채 알파만 사라짐
                targetAlpha = 0f;
            }
            radiusSpeed = GameConstants.WAVE_MAX_RADIUS / GameConstants.WAVE_DURATION;
            alphaSpeed = Mathf.Max(GameConstants.CRIMSON_TINT_ALPHA, GameConstants.AMBER_TINT_ALPHA) / GameConstants.WAVE_DURATION;
            animating = true;
            Apply();
        }

        /// <summary>기존 호출 호환: true = CRIMSON, false = MONOCHROME</summary>
        public void Show(bool on, Vector2 center, bool animate) =>
            Show(on ? PaletteState.CRIMSON : PaletteState.MONOCHROME, center, animate);

        void ApplyImmediate(PaletteState state)
        {
            shownState = state;
            bool on = state != PaletteState.MONOCHROME;
            if (on) baseColor = TintColor(state);
            radius = on ? GameConstants.WAVE_MAX_RADIUS : 0f;
            alpha = TintAlpha(state);
            targetRadius = radius;
            targetAlpha = alpha;
            animating = false;
            Apply();
        }

        void Update()
        {
            if (!animating) return;
            radius = Mathf.MoveTowards(radius, targetRadius, radiusSpeed * Time.deltaTime);
            alpha = Mathf.MoveTowards(alpha, targetAlpha, alphaSpeed * Time.deltaTime);
            Apply();
            if (Mathf.Approximately(radius, targetRadius) && Mathf.Approximately(alpha, targetAlpha))
            {
                radius = targetRadius;
                alpha = targetAlpha;
                animating = false;
                Apply();
            }
        }

        // 모든 Update(입력·전환) 이후에 State와 대조 → 어긋나면 즉시 강제 동기화
        void LateUpdate()
        {
            var pm = PaletteManager.Instance;
            if (pm == null) return;
            PaletteState want = pm.State;
            bool wantVisible = want != PaletteState.MONOCHROME;
            bool settledWrong = !animating && (wantVisible
                ? VisibleAlpha <= 0f || !SameColor(baseColor, TintColor(want))
                : VisibleAlpha > 0f);
            if (want != shownState || settledWrong)
            {
                DesyncCount++;
                Debug.LogWarning($"[Tint] 상태 불일치 감지 → 강제 동기화 (State={pm.State}, 표시={shownState}, alpha={alpha:0.00}, radius={radius:0.0})");
                ApplyImmediate(want);
            }
        }

        static bool SameColor(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        void Apply()
        {
            meshRenderer.enabled = alpha > 0.001f && radius > 0.001f;
            transform.localScale = new Vector3(Mathf.Max(radius, 0.0001f), Mathf.Max(radius, 0.0001f), 1f);
            var c = baseColor;
            c.a = (byte)Mathf.RoundToInt(alpha * 255f);
            var colors = new Color32[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = c;
            mesh.colors32 = colors;
        }

        static Mesh BuildDisc()
        {
            var m = new Mesh { name = "CrimsonTintDisc" };
            var verts = new Vector3[Segments + 1];
            var uvs = new Vector2[Segments + 1];
            var tris = new int[Segments * 3];
            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                verts[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                uvs[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
                tris[i * 3] = 0;
                tris[i * 3 + 1] = (i + 1) % Segments + 1;
                tris[i * 3 + 2] = i + 1;
            }
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }
    }
}
