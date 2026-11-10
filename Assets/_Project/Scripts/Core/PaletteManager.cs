using System;
using UnityEngine;

namespace PaletteShaper.Core
{
    public enum PaletteState
    {
        MONOCHROME,
        CRIMSON,
    }

    /// <summary>
    /// MONOCHROME ↔ CRIMSON 상태 머신.
    /// - 전환 순서: PaletteChanging(이전 충돌 규칙 상태, 플레이어 끼임 보정) → 충돌 규칙 토글 → PaletteChanged(기믹 갱신) → 원형 파동.
    /// - Player(6) ↔ Crimson_Solid(8) 충돌은 MONOCHROME일 때 Physics2D.IgnoreLayerCollision으로 끈다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PaletteManager : MonoBehaviour
    {
        public static PaletteManager Instance { get; private set; }
        public static bool IsCrimsonNow => Instance != null && Instance.State == PaletteState.CRIMSON;

        [SerializeField] PaletteState initialState = PaletteState.MONOCHROME;
        [Tooltip("Room 1의 크림슨 코어(*)를 먹기 전까지는 false")]
        [SerializeField] bool switchUnlocked;
        [SerializeField] Material waveMaterial;
        [SerializeField] Transform waveOrigin;
        [Tooltip("파동 시작점 보정(플레이어 피벗이 발바닥이므로 몸 중앙으로)")]
        [SerializeField] Vector2 waveOffset = new Vector2(0f, 0.5f);

        CrimsonTintOverlay tint;

        public PaletteState State { get; private set; }
        public bool IsCrimson => State == PaletteState.CRIMSON;
        public bool SwitchUnlocked => switchUnlocked;
        public CrimsonTintOverlay Tint => tint;


        /// <summary>충돌 규칙이 바뀌기 직전. 인자는 다음 상태.</summary>
        public event Action<PaletteState> PaletteChanging;
        /// <summary>충돌 규칙이 바뀐 직후. 인자는 새 상태.</summary>
        public event Action<PaletteState> PaletteChanged;
        public event Action SwitchUnlockedChanged;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PaletteShaper] PaletteManager가 둘 이상입니다. 나중 것은 비활성화합니다.", this);
                enabled = false;
                return;
            }
            Instance = this;
            State = initialState;
            ApplyCollisionRules(State);
            tint = CrimsonTintOverlay.Create(transform, waveMaterial);
            tint.Show(State == PaletteState.CRIMSON, Vector2.zero, false);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            // 에디터에서 런타임 변경이 프로젝트 충돌 매트릭스에 남지 않도록 기본값(충돌 ON)으로 복원
            Physics2D.IgnoreLayerCollision(GameConstants.LAYER_PLAYER, GameConstants.LAYER_CRIMSON_SOLID, false);
        }

        void Update()
        {
            if (GameInput.SwitchPressed) TrySwitch();
        }

        public void SetWaveOrigin(Transform origin) => waveOrigin = origin;

        public bool TrySwitch()
        {
            if (!switchUnlocked) return false;
            SetState(IsCrimson ? PaletteState.MONOCHROME : PaletteState.CRIMSON);
            return true;
        }

        /// <summary>리스폰·Room 시작 시 호출: 연출 없이 MONOCHROME으로 되돌린다.</summary>
        public void ResetToMonochrome()
        {
            SetState(PaletteState.MONOCHROME, false);
            // 이미 MONOCHROME이라 SetState가 아무것도 안 했더라도 연출은 항상 강제 정리
            RadialWaveEffect.ClearAll();
            if (tint != null) tint.Show(false, Vector2.zero, false);
        }

        public void UnlockSwitch()
        {
            if (switchUnlocked) return;
            switchUnlocked = true;
            SwitchUnlockedChanged?.Invoke();
        }

        public void SetState(PaletteState next, bool playWave = true)
        {
            if (next == State) return;
            PaletteChanging?.Invoke(next);
            State = next;
            ApplyCollisionRules(next);
            PaletteChanged?.Invoke(next);

            RadialWaveEffect.ClearAll(); // 이전 상태 색의 링이 남지 않게 즉시 제거
            
Vector2 center = waveOrigin != null ? (Vector2)waveOrigin.position + waveOffset : Vector2.zero;
            if (tint != null) tint.Show(next == PaletteState.CRIMSON, center, playWave);
            if (playWave && waveOrigin != null)
            {
                // 어두운 유적 배경 위: CRIMSON = 밝은 코랄 링, MONOCHROME = Warm Gray 링 (2px)
                Color ring = next == PaletteState.CRIMSON ? (Color)GameConstants.LIGHT_CORAL : GameConstants.WARM_GRAY;
                RadialWaveEffect.Spawn(center, ring, waveMaterial);
            }
        }

        static void ApplyCollisionRules(PaletteState state)
        {
            Physics2D.IgnoreLayerCollision(GameConstants.LAYER_PLAYER, GameConstants.LAYER_CRIMSON_SOLID,
                state == PaletteState.MONOCHROME);
        }
    }
}
