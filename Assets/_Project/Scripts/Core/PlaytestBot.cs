using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using PaletteShaper.Gimmicks;
using PaletteShaper.Player;
using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// [에디터 테스트용] Room 1~5를 위치 조건 기반 입력 스크립트로 실제 물리 그대로 플레이해 클리어 가능 여부를 검증한다.
    /// 키보드 대신 GameInput.Override로 입력을 넣는다(사람과 같은 조작 경로).
    /// 메뉴 "Palette Shaper/Debug/Run Auto Playtest"로 실행. 결과는 콘솔 [Playtest] 로그와 화면 중앙 메시지.
    /// 프레임 단위로 정확한 입력이므로 "통과 = 물리적으로 가능"이지, 사람에게 쉽다는 뜻은 아니다.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class PlaytestBot : MonoBehaviour, IGameInputSource
    {
        const int AttemptsPerRoom = 4;
        const float RoomTimeout = 30f;
        const int MainCaseCount = 5; // 앞 5개(Room 1~5 본 동선)만 최종 판정에 사용

        // ── 입력 상태 ──
        float horizontal;
        bool jumpHeld;
        bool pendingJumpPress, pendingJumpRelease, pendingSwitch;
        bool jumpPressedFrame, jumpReleasedFrame, switchFrame;

        public float Horizontal => horizontal;
        public bool JumpPressed => jumpPressedFrame;
        public bool JumpHeld => jumpHeld;
        public bool JumpReleased => jumpReleasedFrame;
        public bool SwitchPressed => switchFrame;

        PlayerController2D P => PlayerController2D.Active;
        PaletteManager PM => PaletteManager.Instance;
        RoomManager RM => RoomManager.Instance;
        float X => P.BodyPosition.x;
        float Feet => P.BodyPosition.y;

        // 커스텀 검증 케이스(출구 도달이 아닌 자체 판정): 스크립트가 customRun=true로 시작하고 verdict를 채운다
        bool customRun;
        bool? verdict;
        string verdictNote;

#if UNITY_EDITOR
        public const string SessionKey = "PaletteShaper.AutoPlaytest";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (!UnityEditor.SessionState.GetBool(SessionKey, false)) return;
            UnityEditor.SessionState.SetBool(SessionKey, false);
            new GameObject("PlaytestBot").AddComponent<PlaytestBot>();
        }
#endif

        void OnEnable() => GameInput.Override = this;

        void OnDisable()
        {
            if (ReferenceEquals(GameInput.Override, this)) GameInput.Override = null;
        }

        void Start() => StartCoroutine(RunAll());

        // 코루틴에서 예약한 "이번 프레임 눌림" 입력을 다음 프레임 Update 단계에서 공개
        void Update()
        {
            jumpPressedFrame = pendingJumpPress;
            jumpReleasedFrame = pendingJumpRelease;
            switchFrame = pendingSwitch;
            pendingJumpPress = pendingJumpRelease = pendingSwitch = false;
        }

        void LateUpdate() => jumpPressedFrame = jumpReleasedFrame = switchFrame = false;

        void ResetInput()
        {
            horizontal = 0f;
            jumpHeld = false;
            pendingJumpPress = pendingJumpRelease = pendingSwitch = false;
        }

        // ───────────── 실행 ─────────────
        IEnumerator RunAll()
        {
            yield return null;
            yield return null;
            if (RM == null || P == null || PM == null)
            {
                Debug.LogError("[Playtest] RoomManager/Player/PaletteManager를 찾지 못했습니다. MainScene에서 실행하세요.");
                yield break;
            }

            var summary = new List<string>();
            var cases = new List<(string label, int room, Func<IEnumerator> script)>
            {
                ("Room 1", 1, Room1),
                ("Room 2", 2, Room2),
                ("Room 3", 3, Room3),
                ("Room 4", 4, Room4),
                ("Room 5", 5, () => Room5(6.9f, 11.9f, 12.25f, false)),
                // 보조 검증: CRIMSON을 켠 채 B 홈으로 걸어 들어가는 동선 (최종 판정에는 포함 안 함)
                ("Room 5 낙하동선 A", 5, () => Room5(6.9f, 12.35f, 12.7f, true)),
                ("Room 5 낙하동선 B", 5, () => Room5(6.9f, 12.7f, 13.05f, true)),
                ("Room 5 낙하동선 C", 5, () => Room5(6.9f, 13.05f, 13.4f, true)),
                // 버그 회귀: 빠른 Q 연타·CRIMSON 사망·사망 중 Q 입력 후 배경 틴트가 State와 일치하는지
                ("틴트 동기화", 5, TintSyncTest),
                // R 잡기 여유 측정 (우선순위 규칙 + R 가로 24px): holdDelay<0 = 방향키 안 누름
                ("R 조기Q(가시 겹침, feet 6.1)", 5, () => RCatchTest(-1f, 6.1f)),
                ("R →홀드 0.15초 후, Q feet 6.3", 5, () => RCatchTest(0.15f, 6.3f)),
                ("R →홀드 0.1초 후, Q feet 6.1", 5, () => RCatchTest(0.1f, 6.1f)),
                ("R →홀드 즉시, Q feet 6.1 (참고)", 5, () => RCatchTest(0f, 6.1f)),

            };

            foreach (var c in cases)
            {
                bool cleared = false;
                string lastFail = "";
                for (int attempt = 1; attempt <= AttemptsPerRoom && !cleared; attempt++)
                {
                    RM.LoadRoom(c.room);
                    ResetInput();
                    customRun = false;
                    verdict = null;
                    verdictNote = null;
                    yield return new WaitForSeconds(0.3f);

                    float start = Time.time;
                    var routine = StartCoroutine(c.script());
                    string reason = null;
                    while (true)
                    {
                        if (customRun)
                        {
                            if (verdict.HasValue) { cleared = verdict.Value; reason = verdictNote; break; }
                            if (Time.time - start > RoomTimeout) { reason = "시간 초과"; break; }
                            yield return null;
                            continue;
                        }
                        if (RM.ExitReached) { cleared = true; break; }
                        if (P.IsDead) { reason = "사망"; break; }
                        if (Time.time - start > RoomTimeout) { reason = "시간 초과"; break; }
                        yield return null;
                    }
                    StopCoroutine(routine);
                    ResetInput();

                    string line = cleared
                        ? $"{c.label}: PASS ({Time.time - start:0.0}초, 시도 {attempt}){(verdictNote != null ? " - " + verdictNote : "")}"
                        : $"{c.label}: 시도 {attempt} 실패 - {reason} @({X:0.00}, {Feet:0.00})";
                    Debug.Log($"[Playtest] {line}");
                    if (cleared) summary.Add(line);
                    else lastFail = line;

                    customRun = false;
                    if (P.IsDead) yield return new WaitForSeconds(GameConstants.RESPAWN_DELAY + 0.2f);
                    yield return new WaitForSeconds(GameConstants.ROOM_TRANSITION_DELAY + 0.2f);
                }
                if (!cleared) summary.Add($"{c.label}: FAIL ({lastFail})");
            }

            bool allMainPass = summary.GetRange(0, MainCaseCount).TrueForAll(s => s.Contains("PASS"));
            var sb = new StringBuilder($"[Playtest] 결과 {(allMainPass ? "ALL PASS" : "FAILED")}: ");
            sb.Append(string.Join(" | ", summary));
            Debug.Log(sb.ToString());
            foreach (var s in summary) Debug.Log($"[Playtest][요약] {s}");

            RM.LoadRoom(1);
            var hud = FindAnyObjectByType<StageHUD>();
            if (hud != null) hud.ShowCenterMessage(allMainPass ? "AUTO TEST PASS" : "AUTO TEST FAIL");
            enabled = false;
        }

        // ───────────── 동작 헬퍼 ─────────────
        IEnumerator Until(Func<bool> condition)
        {
            while (!condition()) yield return null;
        }

        void TapJump()
        {
            pendingJumpPress = true;
            jumpHeld = true;
        }

        void ReleaseJump()
        {
            jumpHeld = false;
            pendingJumpRelease = true;
        }

        IEnumerator Jump(float hold)
        {
            TapJump();
            float t = 0f;
            while (t < hold) { t += Time.deltaTime; yield return null; }
            ReleaseJump();
        }

        IEnumerator SwitchTo(PaletteState state)
        {
            if (PM.State == state) yield break;
            pendingSwitch = true;
            yield return Until(() => PM.State == state);
        }

        /// <summary>목표 x까지 달린 뒤 멈춘다 (감속 거리 고려).</summary>
        IEnumerator RunTo(float target, float tolerance = 0.06f)
        {
            while (true)
            {
                float dx = target - X;
                float vx = P.Velocity.x;
                float stop = vx * vx / (2f * GameConstants.GROUND_DECEL) + 0.02f;
                if (Mathf.Abs(dx) <= tolerance && Mathf.Abs(vx) < 0.2f) { horizontal = 0f; yield break; }
                if (Mathf.Abs(dx) <= stop && Mathf.Sign(dx) == Mathf.Sign(vx)) horizontal = 0f;
                else horizontal = Mathf.Sign(dx);
                yield return null;
            }
        }

        /// <summary>
        /// 오른쪽으로 달리다 triggerX에서 점프, stopX를 넘으면 방향키를 놓아 과도한 비행을 막고 landFeet 높이에 착지.
        /// </summary>
        IEnumerator JumpOnto(float triggerX, float landFeet, float stopX, float hold = 0.3f)
        {
            horizontal = 1f;
            yield return Until(() => X >= triggerX);
            StartCoroutine(Jump(hold));
            yield return Until(() => X >= stopX);
            horizontal = 0f;
            yield return Until(() => P.IsGrounded && Feet >= landFeet - 0.05f);
            horizontal = 0f;
            yield return Until(() => Mathf.Abs(P.Velocity.x) < 0.2f);
        }

        IEnumerator RunRightToExit()
        {
            horizontal = 1f;
            yield return Until(() => false);
        }

        // ───────────── Room 스크립트 (좌표: 방 좌하단 = 0,0 / Feet = 발바닥 높이) ─────────────
        IEnumerator Room1()
        {
            yield return JumpOnto(2.15f, 3f, 3.5f);   // ## (x3~5, top 3)
            yield return JumpOnto(4.15f, 4f, 5.5f);   // ## (x5~7, top 4)
            yield return JumpOnto(6.15f, 5f, 7.5f);   // #### (x7~11, top 5)
            yield return RunTo(10.3f);          // 지나가며 코어(*) 흡수
            yield return Until(() => PM.SwitchUnlocked);
            yield return SwitchTo(PaletteState.CRIMSON);
            yield return JumpOnto(10.2f, 6f, 11.5f);   // C (x11~13, top 6)
            yield return JumpOnto(12.15f, 7f, 13.5f);  // C (x13~15, top 7)
            yield return JumpOnto(14.15f, 8f, 15.5f);  // C (x15~17, top 8)
            yield return RunRightToExit();      // E (x18~19, feet 8)
        }

        IEnumerator Room2()
        {
            yield return RunTo(8.45f);          // W 안 (흑백이라 통과 가능), 바닥 col 8 위
            horizontal = 1f;
            StartCoroutine(Jump(0.3f));
            yield return Until(() => X >= 10.5f && Feet >= 5.25f);
            yield return SwitchTo(PaletteState.CRIMSON); // 공중에서 C 실체화
            yield return Until(() => P.IsGrounded);
            horizontal = 0f;
            yield return JumpOnto(11.15f, 6f, 12.5f);
            yield return JumpOnto(13.15f, 7f, 14.5f);
            yield return JumpOnto(15.2f, 8f, 17.2f);   // 1칸 틈 건너 출구 발판
            yield return RunRightToExit();
        }

        // 2026-11 확정 맵: C 발판 top 4 / 5, X는 천장까지, 출구 발판 top 5
        IEnumerator Room3()
        {
            yield return SwitchTo(PaletteState.CRIMSON);
            yield return JumpOnto(2.5f, 4f, 5.6f);    // C (x5~7, top 4)
            yield return RunTo(6.55f);
            yield return JumpOnto(6.55f, 5f, 9.6f);   // C (x9~11, top 5)
            yield return RunTo(9.4f);
            horizontal = 1f;
            yield return Until(() => X >= 11.25f);
            StartCoroutine(Jump(0.3f));
            yield return Until(() => Feet > 5.05f);
            yield return SwitchTo(PaletteState.MONOCHROME);          // X 통과
            yield return Until(() => X >= 14.3f && Feet >= 4.08f);
            yield return SwitchTo(PaletteState.CRIMSON);             // C (x14~16, top 4) 실체화 후 착지
            yield return Until(() => P.IsGrounded);
            horizontal = 0f;
            yield return JumpOnto(15.4f, 5f, 17.2f);                 // 출구 발판 (x17~)
            yield return RunRightToExit();
        }

        IEnumerator Room4()
        {
            yield return SwitchTo(PaletteState.CRIMSON);
            yield return RunTo(7.35f);
            var gear = FindAnyObjectByType<CrimsonGearPlatform>();
            yield return Until(() => gear.Direction < 0 && gear.Position.x <= 9.9f && gear.Position.x >= 9.3f);
            horizontal = -1f;                                         // 오른쪽에서 B로 진입 → 수직 발사
            yield return Until(() => P.Velocity.y > 15f);
            horizontal = 0f;
            yield return Until(() => Feet >= 8.05f);                  // 발판 높이 위로 올라선 뒤에만 오른쪽으로
            horizontal = 1f;
            yield return Until(() => P.IsGrounded);
            horizontal = 0f;
            yield return Until(() => gear.Position.x >= 14.6f);       // 오른쪽 끝까지 탑승
            horizontal = 1f;
            yield return Until(() => X >= 15.4f);
            yield return Jump(0.2f);
            yield return RunRightToExit();
        }

        /// <param name="rSwitchFeet">R에서 CRIMSON을 켤 발 높이 (가시와 겹치지 않는 높이)</param>
        /// <param name="dropRoute">true: 홈 앞에서 CRIMSON을 켜고 걸어 들어가 B에 닿는 즉시 발사</param>
        IEnumerator Room5(float rSwitchFeet, float freezeMin, float freezeMax, bool dropRoute)
        {
            // 톱니 발판은 CRIMSON일 때만 움직이므로, 착지 시점에 왼쪽 끝에 오도록 미리 위치를 맞춰 둔다 (파킹)
            var gear = FindAnyObjectByType<CrimsonGearPlatform>();
            yield return SwitchTo(PaletteState.CRIMSON);
            yield return Until(() => gear.Direction < 0 && gear.Position.x <= freezeMax && gear.Position.x >= freezeMin);
            yield return SwitchTo(PaletteState.MONOCHROME);
            float tStart;
            if (!dropRoute)
            {
                yield return RunTo(4.5f, 0.05f);                      // 흑백 상태로 B 홈에 들어감
                yield return Until(() => P.IsGrounded && Feet < 1.2f);
                tStart = Time.time;
                yield return SwitchTo(PaletteState.CRIMSON);          // 겹친 채 켜는 순간 B 발사
            }
            else
            {
                yield return RunTo(3.3f, 0.05f);                      // 홈 왼쪽 가장자리 앞에서 정지
                yield return SwitchTo(PaletteState.CRIMSON);          // 켠 채로
                tStart = Time.time;
                horizontal = 1f;                                      // 걸어 들어가 낙하 → B 접촉 즉시 발사
                yield return Until(() => X >= 4.35f || P.Velocity.y > 15f);
                horizontal = 0f;
            }
            yield return Until(() => P.Velocity.y > 15f);
            Debug.Log($"[Playtest][R5] drop={dropRoute} 발사까지 {Time.time - tStart:0.00}s, 발사 시 발판 x={gear.Position.x:0.00}");
            yield return SwitchTo(PaletteState.MONOCHROME);           // 가시 통과
            yield return Until(() => Feet >= rSwitchFeet);
            yield return SwitchTo(PaletteState.CRIMSON);              // R과 겹친 채 켜기 → 대각선 발사
            horizontal = 1f;
            var trace = new StringBuilder("[Playtest][R궤적] ");
            float traceNext = 0f;
            while (!P.IsGrounded && !P.IsDead)
            {
                if (Time.time >= traceNext)
                {
                    trace.Append($"p({X:0.0},{Feet:0.0}) g{gear.Position.x:0.0} | ");
                    traceNext = Time.time + 0.06f;
                }
                yield return null;
            }
            Debug.Log(trace.ToString());
            horizontal = 0f;
            yield return Until(() => gear.Position.x >= 13.7f);
            horizontal = 1f;
            yield return Until(() => X >= 14.9f);
            yield return Jump(0.25f);
            yield return RunRightToExit();
        }

        // ───────────── R 잡기 측정 ─────────────
        /// <summary>B 발사 → 흑백 → (holdDelay 후 → 홀드) → 발 높이 qFeet에서 CRIMSON. R이 발동되고 살아 있으면 PASS.</summary>
        IEnumerator RCatchTest(float holdDelay, float qFeet)
        {
            customRun = true;
            yield return RunTo(4.5f, 0.05f);
            yield return Until(() => P.IsGrounded && Feet < 1.2f);
            yield return SwitchTo(PaletteState.CRIMSON);
            yield return Until(() => P.Velocity.y > 15f);
            float tLaunch = Time.time;
            yield return SwitchTo(PaletteState.MONOCHROME);
            while (P.Velocity.y > 0f && Feet < qFeet)
            {
                if (holdDelay >= 0f && Time.time - tLaunch >= holdDelay) horizontal = 1f;
                yield return null;
            }
            if (holdDelay >= 0f && Time.time - tLaunch >= holdDelay) horizontal = 1f;
            float xAtQ = X, feetAtQ = Feet, tQ = Time.time - tLaunch;
            yield return SwitchTo(PaletteState.CRIMSON);
            float until = Time.time + 0.15f;
            bool launched = false;
            while (Time.time < until && !P.IsDead)
            {
                if (P.Velocity.x > 10f) launched = true;
                yield return null;
            }
            yield return new WaitForSeconds(0.2f);
            bool alive = !P.IsDead;
            verdictNote = $"Q 시점 t={tQ:0.000}s x={xAtQ:0.00} feet={feetAtQ:0.00} → R발동={launched}, 생존={alive}";
            verdict = launched && alive;
        }

        // ───────────── 틴트 동기화 회귀 테스트 ─────────────
        IEnumerator TintSyncTest()
        {
            customRun = true;
            var tint = PM.Tint;
            int startDesync = tint.DesyncCount;
            var fails = new List<string>();

            IEnumerator Check(string when)
            {
                yield return new WaitForSeconds(0.3f);        // 파동/페이드(0.2초) 정착 대기
                bool crimson = PM.IsCrimson;
                bool ok = crimson
                    ? tint.ShowingCrimson && tint.VisibleAlpha > 0.2f && tint.Radius >= GameConstants.WAVE_MAX_RADIUS - 0.01f
                    : !tint.ShowingCrimson && tint.VisibleAlpha <= 0f;
                if (!ok) fails.Add($"{when}: State={PM.State} alpha={tint.VisibleAlpha:0.00} r={tint.Radius:0.0}");
                if (RadialWaveEffect.ActiveCount > 0) fails.Add($"{when}: 파동 링 {RadialWaveEffect.ActiveCount}개 잔존");
                Debug.Log($"[Playtest][틴트] {when}: State={PM.State}, 틴트 alpha={tint.VisibleAlpha:0.00}, r={tint.Radius:0.0} → {(ok ? "일치" : "불일치")}");
            }

            // 1) 제자리 Q 연타 (1~7프레임 간격, 31회 → 홀수라 CRIMSON으로 끝남)
            for (int i = 0; i < 31; i++)
            {
                pendingSwitch = true;
                int frames = 1 + (i * 5) % 7;
                for (int k = 0; k < frames; k++) yield return null;
            }
            yield return Check("연타 31회 후");

            // 2) CRIMSON 상태로 B 발사 → 가시에 사망 → 사망 대기 중 Q 2회 → 리스폰
            yield return SwitchTo(PaletteState.MONOCHROME);
            yield return RunTo(4.5f, 0.05f);
            yield return Until(() => P.IsGrounded && Feet < 1.2f);
            yield return SwitchTo(PaletteState.CRIMSON);             // 발사 후 그대로 두면 가시에 찔림
            yield return Until(() => P.IsDead);
            yield return new WaitForSeconds(0.1f);
            pendingSwitch = true;                                      // 사망 중 → MONO
            yield return new WaitForSeconds(0.15f);
            pendingSwitch = true;                                      // 사망 중 → CRIMSON (파동 진행 중 리스폰)
            yield return Until(() => !P.IsDead);
            yield return Check("사망 중 Q 2회 후 리스폰");

            // 3) 다시 사망, 사망 중 Q 1회, 리스폰 직후 곧바로 Q 연타
            yield return RunTo(4.5f, 0.05f);
            yield return Until(() => P.IsGrounded && Feet < 1.2f);
            yield return SwitchTo(PaletteState.CRIMSON);
            yield return Until(() => P.IsDead);
            yield return new WaitForSeconds(0.2f);
            pendingSwitch = true;
            yield return Until(() => !P.IsDead);
            for (int i = 0; i < 6; i++) { pendingSwitch = true; yield return null; yield return null; }
            yield return Check("리스폰 직후 연타 6회");

            // 4) 파동 진행 중 Room 재로드
            yield return SwitchTo(PaletteState.MONOCHROME);
            pendingSwitch = true;
            yield return null;
            yield return null;
            RM.LoadRoom(5);
            yield return Check("파동 중 Room 재로드");

            int desync = tint.DesyncCount - startDesync;
            verdictNote = $"강제 동기화 발생 {desync}회, 불일치 {fails.Count}건";
            foreach (var msg in fails) Debug.LogWarning($"[Playtest][틴트] {msg}");
            verdict = fails.Count == 0;
        }
    }
}
