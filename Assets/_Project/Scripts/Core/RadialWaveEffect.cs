using System.Collections.Generic;
using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// 팔레트 전환 시 원형 파동 링: 0.2초 동안 반경 0 → 25 Units, VFX_Wave Sorting Layer.
    /// 선 두께 2px. 끝나면 스스로 파괴된다.
    /// 새 전환/리스폰 시 ClearAll()로 진행 중인 링을 즉시 제거해 이전 상태 색의 링이 남지 않게 한다.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class RadialWaveEffect : MonoBehaviour
    {
        const int Segments = 96;
        static readonly List<RadialWaveEffect> active = new List<RadialWaveEffect>();

        LineRenderer line;
        float elapsed;

        public static int ActiveCount => active.Count;

        public static RadialWaveEffect Spawn(Vector3 center, Color color, Material material)
        {
            var go = new GameObject("Fx_RadialWave");
            go.transform.position = center;
            var lr = go.AddComponent<LineRenderer>();
            if (material != null) lr.sharedMaterial = material;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = Segments;
            lr.widthMultiplier = 2f / GameConstants.PIXELS_PER_UNIT;
            lr.startColor = color;
            lr.endColor = color;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingLayerName = GameConstants.SORT_VFX_WAVE;
            var fx = go.AddComponent<RadialWaveEffect>();
            fx.line = lr;
            fx.SetRadius(0f);
            return fx;
        }

        /// <summary>진행 중인 모든 파동 링을 즉시 제거.</summary>
        public static void ClearAll()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (i >= active.Count) continue;
                var fx = active[i];
                if (fx != null)
                {
                    fx.gameObject.SetActive(false);
                    Destroy(fx.gameObject);
                }
            }
            active.Clear();
        }

        void Awake()
        {
            if (line == null) line = GetComponent<LineRenderer>();
        }

        void OnEnable() => active.Add(this);
        void OnDisable() => active.Remove(this);

        void Update()
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / GameConstants.WAVE_DURATION);
            SetRadius(k * GameConstants.WAVE_MAX_RADIUS);
            if (k >= 1f) Destroy(gameObject);
        }

        void SetRadius(float radius)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
        }
    }
}
