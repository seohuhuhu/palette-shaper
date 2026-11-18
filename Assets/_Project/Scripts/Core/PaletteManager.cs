using System;
using UnityEngine;

namespace PaletteShaper.Core
{
    public enum PaletteState
    {
        MONOCHROME,
        CRIMSON,
        AMBER,
    }

    /// <summary>
    /// MONOCHROME / CRIMSON / AMBER 상태 머신 (한 번에 한 색만 켜짐).
    /// - Q(1): CRIMSON 켜기 — 이미 CRIMSON이면 끔(MONOCHROME), AMBER면 즉시 CRIMSON으로 교체
    /// - E(2): AMBER 켜기 — 이미 AMBER면 끔, CRIMSON이면 즉시 AMBER로 교체
    /// - 0: 어떤 상태든 즉시 MONOCHROME
    /// - 전환 순서: PaletteChanging(플레이어 끼임 보정) → 충돌 규칙 토글 → PaletteChanged(기믹 갱신) → 원형 파동·틴트.
    /// - Player(6) ↔ Crimson_Solid(8)는 CRIMSON일 때만, Player(6) ↔ Amber_Solid(10)는 AMBER일 때만 충돌한다.
    /// - 해금: CrimsonUnlocked(Room 1 코어 *), AmberUnlocked(Room 6 앰버 코어 @). 리스폰은 해금 유지 + MONOCHROME 초기화.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PaletteManager : MonoBehaviour
    {
        public static PaletteManager Instance { get; private set; }
        public static bool IsCrimsonNow => Instance != null && Instance.State == PaletteState.CRIMSON;
        public static bool IsAmberNow => Instance != null && Instance.State == PaletteState.AMBER;

        [SerializeField] PaletteState initialState = PaletteState.MONOCHROME;
        [Tooltip("Room 1의 크림슨 코어(*)를 먹기 전까지는 false")]
        [SerializeField] bool switchUnlocked;
        [Tooltip("Room 6의 앰버 코어(@)를 먹기 전까지는 false")]
        [SerializeField] bool amberUnlocked;
        [SerializeField] Material waveMaterial;
        [SerializeField] Transform waveOrigin;
        [Tooltip("파동 시작점 보정(플레이어 피벗이 발바닥이므로 몸 중앙으로)")]
        [SerializeField] Vector2 waveOffset = new Vector2(0f, 0.5f);

        CrimsonTintOverlay tint;

        public PaletteState State { get; private set; }
        public bool IsCrimson => State == PaletteState.CRIMSON;
        public bool IsAmber => State == PaletteState.AMBER;
        /// <summary>Q(크림슨) 해금 여부 (기존 이름 유지)</summary>
        public bool SwitchUnlocked => switchUnlocked;
        public bool CrimsonUnlocked => switchUnlocked;
        public bool AmberUnlocked => amberUnlocked;
        public CrimsonTintOverlay Tint => tint;

        /// <summary>충돌 규칙이 바뀌기 직전. 인자는 다음 상태.</summary>
        public event Action<PaletteState> PaletteChanging;
        /// <summary>충돌 규칙이 바뀐 직후. 인자는 새 상태.</summary>
        public event Action<PaletteState> PaletteChanged;
        public event Action SwitchUnlockedChanged;
        /// <summary>플레이어 입력으로 색이 바뀐 순간 (리스폰·Room 시작 초기화는 제외). Game Juice / 사운드용</summary>
        public event Action<PaletteState> PaletteSwitched;

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
            tint.Show(State, Vector2.zero, false);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            // 에디터에서 런타임 변경이 프로젝트 충돌 매트릭스에 남지 않도록 기본값(충돌 ON)으로 복원
            Physics2D.IgnoreLayerCollision(GameConstants.LAYER_PLAYER, GameConstants.LAYER_CRIMSON_SOLID, false);
            Physics2D.IgnoreLayerCollision(GameConstants.LAYER_PLAYER, GameConstants.LAYER_AMBER_SOLID, false);
        }

        void Update()
        {
            if (GameInput.OffPressed) SetState(PaletteState.MONOCHROME);
            else if (GameInput.SwitchPressed) TryToggle(PaletteState.CRIMSON);
            else if (GameInput.AmberPressed) TryToggle(PaletteState.AMBER);
        }

        public void SetWaveOrigin(Transform origin) => waveOrigin = origin;

        /// <summary>Q 동작 (기존 호출 호환)</summary>
        public bool TrySwitch() => TryToggle(PaletteState.CRIMSON);

        /// <summary>해당 색이 켜져 있으면 끄고, 아니면 그 색으로 즉시 교체. 잠긴 색이면 무시.</summary>
        public bool TryToggle(PaletteState color)
        {
            if (color == PaletteState.MONOCHROME) { SetState(PaletteState.MONOCHROME); return true; }
            if (!IsUnlocked(color)) return false;
            SetState(State == color ? PaletteState.MONOCHROME : color);
            return true;
        }

        public bool IsUnlocked(PaletteState color) =>
            color == PaletteState.MONOCHROME ||
            (color == PaletteState.CRIMSON && switchUnlocked) ||
            (color == PaletteState.AMBER && amberUnlocked);

        /// <summary>리스폰·Room 시작 시 호출: 연출 없이 MONOCHROME으로 되돌린다.</summary>
        public void ResetToMonochrome()
        {
            SetState(PaletteState.MONOCHROME, false);
            // 이미 MONOCHROME이라 SetState가 아무것도 안 했더라도 연출은 항상 강제 정리
            RadialWaveEffect.ClearAll();
            if (tint != null) tint.Show(PaletteState.MONOCHROME, Vector2.zero, false);
        }

        public void UnlockSwitch()
        {
            if (switchUnlocked) return;
            switchUnlocked = true;
            SwitchUnlockedChanged?.Invoke();
        }

        public void UnlockAmber()
        {
            if (amberUnlocked) return;
            amberUnlocked = true;
            SwitchUnlockedChanged?.Invoke();
        }

        /// <summary>Room 시작 시 해금 상태를 지정 (Room 1: 둘 다 잠김, 2~6: 크림슨만, 7~: 둘 다).</summary>
        public void SetUnlocks(bool crimson, bool amber)
        {
            if (switchUnlocked == crimson && amberUnlocked == amber) return;
            switchUnlocked = crimson;
            amberUnlocked = amber;
            if (!IsUnlocked(State)) SetState(PaletteState.MONOCHROME, false);
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
            if (tint != null) tint.Show(next, center, playWave);
            if (playWave && waveOrigin != null)
                RadialWaveEffect.Spawn(center, RingColor(next), waveMaterial);
            if (playWave) PaletteSwitched?.Invoke(next);
        }

        /// <summary>어두운 유적 배경 위 2px 링: CRIMSON = Light Coral, AMBER = Amber Highlight, MONOCHROME = Warm Gray</summary>
        public static Color RingColor(PaletteState state) =>
            state == PaletteState.CRIMSON ? (Color)GameConstants.LIGHT_CORAL :
            state == PaletteState.AMBER ? (Color)GameConstants.AMBER_HIGHLIGHT :
            (Color)GameConstants.WARM_GRAY;

        static void ApplyCollisionRules(PaletteState state)
        {
            Physics2D.IgnoreLayerCollision(GameConstants.LAYER_PLAYER, GameConstants.LAYER_CRIMSON_SOLID,
                state != PaletteState.CRIMSON);
            Physics2D.IgnoreLayerCollision(GameConstants.LAYER_PLAYER, GameConstants.LAYER_AMBER_SOLID,
                state != PaletteState.AMBER);
        }
    }
}
