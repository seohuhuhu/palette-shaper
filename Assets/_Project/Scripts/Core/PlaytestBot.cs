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
        static int AttemptsPerRoom => OnlyCase != null ? 1 : 4;
        const float RoomTimeout = 30f;
        const float CustomTimeout = 150f; // 여러 조합을 도는 조사 케이스용
        static readonly string OnlyCase = null; // [디버그] 이 문자열로 시작하는 케이스만 실행, null이면 전체 실행
        const int MainCaseCount = 10; // 앞 10개(Room 1~10 본 동선)만 최종 판정에 사용

        // ── 입력 상태 ──
        float horizontal;
        bool jumpHeld;
        bool pendingJumpPress, pendingJumpRelease, pendingSwitch, pendingAmber, pendingOff;
        bool jumpPressedFrame, jumpReleasedFrame, switchFrame, amberFrame, offFrame;

        public float Horizontal => horizontal;
        public bool JumpPressed => jumpPressedFrame;
        public bool JumpHeld => jumpHeld;
        public bool JumpReleased => jumpReleasedFrame;
        public bool SwitchPressed => switchFrame;
        public bool AmberPressed => amberFrame;
        public bool OffPressed => offFrame;

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
            amberFrame = pendingAmber;
            offFrame = pendingOff;
            pendingJumpPress = pendingJumpRelease = pendingSwitch = pendingAmber = pendingOff = false;
        }

        void LateUpdate() => jumpPressedFrame = jumpReleasedFrame = switchFrame = amberFrame = offFrame = false;

        void ResetInput()
        {
            horizontal = 0f;
            jumpHeld = false;
            pendingJumpPress = pendingJumpRelease = pendingSwitch = pendingAmber = pendingOff = false;
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
                ("Room 3", 3, () => Room3(0)),
                ("Room 4", 4, Room4),
                ("Room 5", 5, () => Room5(6.9f, 11.9f, 12.25f, false)),
                ("Room 6", 6, Room6),
                ("Room 7", 7, () => Room7(false)),
                ("Room 8", 8, Room8),
                ("Room 9", 9, Room9),
                ("Room 10", 10, Room10),
                // 보조 검증: CRIMSON을 켠 채 B 홈으로 걸어 들어가는 동선 (최종 판정에는 포함 안 함)
                // Room 3 두 번째 Q 창 측정: 1 = 가시 벗어나자마자(가장 이른) / 2 = 하강 중 발 높이 3.15(가장 늦은 쪽)
                ("Room 7 공중 전환 최지연(가시 직전)", 7, () => Room7(true)),
                // 조사: C→(E)→A 구간에서 도약 위치·점프 길이별 결과 (A 옆면에 걸리는지)
                ("사망 후 잔상 없음(패드 발사 중)", 9, () => DeathGhostTest(false)),
                ("사망 후 잔상 없음(기류 안)", 10, () => DeathGhostTest(true)),
                // 조사: 2-2 C→(E)→A 도약 위치 × 점프 길이 × E 타이밍 (7.4 짧은 톡은 발판에 못 미치는 점프라 낙하가 정상)
                ("R7전환조사 도약 7.4", 7, () => Room7SwitchProbe(7.4f)),
                ("R7전환조사 도약 8.0", 7, () => Room7SwitchProbe(8.0f)),
                ("R7전환조사 도약 8.3", 7, () => Room7SwitchProbe(8.3f)),
                ("R7조사 도약 7.2", 7, () => Room7WallProbe(7.2f)),
                ("R7조사 도약 7.7", 7, () => Room7WallProbe(7.7f)),
                ("R7조사 도약 8.0", 7, () => Room7WallProbe(8.0f)),
                ("R7조사 도약 8.3", 7, () => Room7WallProbe(8.3f)),
                ("Room 3 두번째Q 최조기", 3, () => Room3(1)),
                ("Room 3 두번째Q 최지연(발 3.15)", 3, () => Room3(2)),
                ("Room 5 낙하동선 A", 5, () => Room5(6.9f, 12.35f, 12.7f, true)),
                ("Room 5 낙하동선 B", 5, () => Room5(6.9f, 12.7f, 13.05f, true)),
                ("Room 5 낙하동선 C", 5, () => Room5(6.9f, 13.05f, 13.4f, true)),
                // 버그 회귀: 빠른 Q 연타·CRIMSON 사망·사망 중 Q 입력 후 배경 틴트가 State와 일치하는지
                ("틴트 동기화", 5, TintSyncTest),
                ("틴트 동기화(앰버·교차)", 7, AmberTintTest),
                // 필수: Room 7 스폰 턱(top 2)에서 AMBER로 오른쪽으로 걸을 때 A(col 4~5) 밑면(y 3)에 머리가 걸리지 않고 바닥까지 내려가는지
                ("Room 7 AMBER로 턱에서 걸어 내려가기", 7, LedgeWalkProbe),
                // R 잡기 여유 측정 (우선순위 규칙 + R 가로 24px): holdDelay<0 = 방향키 안 누름
                ("R 조기Q(가시 겹침, feet 6.1)", 5, () => RCatchTest(-1f, 6.1f)),
                ("R →홀드 0.15초 후, Q feet 6.3", 5, () => RCatchTest(0.15f, 6.3f)),
                ("R →홀드 0.1초 후, Q feet 6.1", 5, () => RCatchTest(0.1f, 6.1f)),
                ("R →홀드 즉시, Q feet 6.1 (참고)", 5, () => RCatchTest(0f, 6.1f)),

            };

            foreach (var c in cases)
            {
                if (OnlyCase != null && !c.label.StartsWith(OnlyCase)) continue;
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
                            if (Time.time - start > CustomTimeout) { reason = "시간 초과"; break; }
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

            bool allMainPass = summary.Count >= MainCaseCount && summary.GetRange(0, MainCaseCount).TrueForAll(s => s.Contains("PASS"));
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

        /// <summary>사람과 같은 키로 전환: CRIMSON = Q, AMBER = E, MONOCHROME = 켜진 색의 키를 한 번 더.</summary>
        IEnumerator SwitchTo(PaletteState state)
        {
            if (PM.State == state) yield break;
            PaletteState key = state == PaletteState.MONOCHROME ? PM.State : state;
            if (key == PaletteState.AMBER) pendingAmber = true;
            else pendingSwitch = true;
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

        // 2026-11 확정 맵: C1 top 4(col 5~6), C2 top 5(col 8~9), X는 천장~row 6, 착지 C top 3(col 14~16), 출구 발판 top 5
        /// <param name="qMode">0 = 기본, 1 = 가시를 벗어나는 즉시 Q, 2 = 하강 중 발이 3.15까지 내려왔을 때 Q</param>
        IEnumerator Room3(int qMode)
        {
            yield return SwitchTo(PaletteState.CRIMSON);
            yield return JumpOnto(2.5f, 4f, 5.6f);    // C (x5~7, top 4)
            yield return RunTo(6.55f);
            yield return JumpOnto(6.4f, 5f, 8.6f);    // C2 (x8~10, top 5)
            yield return RunTo(8.5f);
            horizontal = 1f;
            yield return Until(() => X >= 10.3f);     // C2 오른쪽 끝 근처에서 도약 (sim 최적 10.35)
            StartCoroutine(Jump(0.3f));
            yield return Until(() => Feet > 5.05f);
            yield return SwitchTo(PaletteState.MONOCHROME);          // X 통과
            float tClear = 0f;
            if (qMode == 1) yield return Until(() => X - 0.375f > 13.75f);              // 왼쪽 끝이 마지막 가시 판정(x 13.75)을 벗어나는 순간
            else if (qMode == 2) { yield return Until(() => X - 0.375f > 13.75f); tClear = Time.time; yield return Until(() => P.Velocity.y < 0f && Feet <= 3.15f); }
            else yield return Until(() => X >= 14.3f && Feet >= 3.08f);
            if (qMode == 2) Debug.Log($"[Playtest][R3] 가시 벗어난 뒤 {Time.time - tClear:0.00}s 후 Q (발 {Feet:0.00}, x {X:0.00})");
            yield return SwitchTo(PaletteState.CRIMSON);             // C (x14~17, top 3) 실체화 후 착지
            yield return Until(() => P.IsGrounded);
            horizontal = 0f;
            yield return JumpOnto(15.6f, 5f, 17.2f);                 // 출구 발판 (x17~, top 5)
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

        // ───────────── Chapter 2 (AMBER) ─────────────
        /// <summary>오른쪽으로 달리다 triggerX에서 점프 → 발이 switchFeet를 넘으면 공중에서 색 전환 → stopX에서 방향키 놓고 landFeet에 착지.</summary>
        IEnumerator JumpSwitchOnto(float triggerX, PaletteState toState, float switchFeet, float landFeet, float stopX, float hold = 0.3f)
        {
            horizontal = 1f;
            yield return Until(() => X >= triggerX);
            StartCoroutine(Jump(hold));
            yield return Until(() => !P.IsGrounded && Feet > switchFeet);
            yield return SwitchTo(toState);
            yield return Until(() => X >= stopX || P.IsGrounded && Feet >= landFeet - 0.05f);
            horizontal = 0f;
            yield return Until(() => P.IsGrounded && Feet >= landFeet - 0.05f);
            yield return Until(() => Mathf.Abs(P.Velocity.x) < 0.2f);
        }

        IEnumerator TraceFor(string tag, float seconds)
        {
            var sb = new StringBuilder($"[Playtest][{tag}추적] ");
            float end = Time.time + seconds;
            while (Time.time < end && P != null && !P.IsDead)
            {
                sb.Append($"({X:0.00},{Feet:0.00},{(P.InUpdraft ? "U" : "")}{(P.IsGrounded ? "G" : "")}) ");
                yield return new WaitForSeconds(0.05f);
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>triggerX에서 점프 → 공중에서 X가 switchX를 넘을 때 전환 → stopX에서 방향키 놓고 landFeet 착지.</summary>
        IEnumerator JumpSwitchAtX(float triggerX, PaletteState toState, float entryX, float landFeet, float stopX)
        {
            horizontal = 1f;
            yield return Until(() => X >= triggerX);
            StartCoroutine(Jump(0.3f));
            // 가시 판정 진입 x(entryX) 직전의 '마지막 물리 스텝'에 전환: 다음 2스텝 안에 진입할 때 요청
            // (고정 x 기준은 물리 스텝 위상에 따라 한 스텝에 판정을 건너뛰어 버림)
            float step = Time.fixedDeltaTime;
            yield return Until(() => X + Mathf.Max(P.Velocity.x, 0f) * step * 2f >= entryX);
            float xReq = X;
            yield return SwitchTo(toState);
            Debug.Log($"[Playtest][최지연] 전환 요청 x {xReq:0.000} → 전환 x {X:0.000} (판정 진입 {entryX:0.000}, 여유 {(entryX - X) / Mathf.Max(0.01f, P.Velocity.x):0.000}초)");
            yield return Until(() => X >= stopX || P.IsGrounded && Feet >= landFeet - 0.05f);
            horizontal = 0f;
            yield return Until(() => P.IsGrounded && Feet >= landFeet - 0.05f);
            yield return Until(() => Mathf.Abs(P.Velocity.x) < 0.2f);
        }

        // 회귀: 패드 발사 중·기류 안에서 죽었을 때 죽은 자리에 잔상이 남지 않는지 (1-5 피드백)
        IEnumerator DeathGhostTest(bool updraft)
        {
            customRun = true;
            var juice = FindAnyObjectByType<JuiceManager>();
            if (updraft)
            {
                yield return SwitchTo(PaletteState.AMBER);   // Room 10: 기류(col 5) 안으로
                yield return RunTo(5.5f, 0.1f);
                yield return Until(() => P.InUpdraft);
            }
            else
            {
                yield return RunTo(5.2f, 0.05f);             // Room 9: B 위
                yield return SwitchTo(PaletteState.CRIMSON);
                yield return Until(() => P.Velocity.y > 12f);
            }
            yield return new WaitForSeconds(0.1f);
            int before = juice != null ? juice.ActiveGhosts : -1;
            P.Kill();
            yield return new WaitForSeconds(0.3f);
            int after = juice != null ? juice.ActiveGhosts : -1;
            verdictNote = $"사망 직전 잔상 {before}개 → 사망 0.3초 후 {after}개";
            verdict = juice != null && before > 0 && after == 0;
        }

        // 조사: C에서 뛰어 X 앞에서 E — 도약 위치 × 점프 길이 × E 누르는 x
        IEnumerator Room7SwitchProbe(float takeoff)
        {
            customRun = true;
            var sb = new StringBuilder();
            int falls = 0, total = 0;
            foreach (float hold in new[] { 0.04f, 0.08f, 0.12f, 0.2f, 0.3f })
            foreach (float sx in new[] { 9.2f, 9.5f, 9.7f, 9.85f })
            {
                RM.LoadRoom(7);
                ResetInput();
                yield return new WaitForSeconds(0.3f);
                yield return SwitchTo(PaletteState.AMBER);
                yield return JumpOnto(2.6f, 4f, 4.4f);
                yield return JumpSwitchOnto(5.0f, PaletteState.CRIMSON, 4.1f, 5f, 6.6f);
                yield return RunTo(6.6f);
                horizontal = 1f;
                yield return Until(() => X >= takeoff);
                StartCoroutine(Jump(hold));
                yield return new WaitForSeconds(0.02f);
                yield return Until(() => X >= sx || P.IsDead || P.IsGrounded);
                int fix0 = P.OverlapFixCount;
                float swX = X, swFeet = Feet;
                if (!P.IsDead) yield return SwitchTo(PaletteState.AMBER);
                bool fixedOverlap = P.OverlapFixCount != fix0;
                float t0 = Time.time;
                while (Time.time - t0 < 1.5f && !P.IsDead && !(P.IsGrounded && Time.time - t0 > 0.05f)) yield return null;
                string res = P.IsDead ? "사망" : P.IsGrounded && Feet >= 5.9f ? "A착지" : $"낙하({X:0.00},{Feet:0.00})";
                total++;
                if (res.StartsWith("낙하")) falls++;
                sb.Append($"h{hold:0.00} E@x{sx:0.00}(실제 {swX:0.00}, 발 {swFeet:0.00}){(fixedOverlap ? " [끼임보정 " + P.LastOverlapFix + "]" : "")} → {res} | ");
                horizontal = 0f;
                if (P.IsDead) yield return new WaitForSeconds(GameConstants.RESPAWN_DELAY + 0.2f);
            }
            verdictNote = $"낙하 {falls}/{total} :: " + sb;
            Debug.Log($"[Playtest][R7전환조사] 도약 {takeoff:0.0} → {verdictNote}");
            verdict = falls == 0;
        }

        IEnumerator Room7WallProbe(float takeoff)
        {
            customRun = true;
            var sb = new StringBuilder();
            bool allLanded = true;
            foreach (float hold in new[] { 0.06f, 0.12f, 0.18f, 0.3f })
            {
                RM.LoadRoom(7);
                ResetInput();
                yield return new WaitForSeconds(0.3f);
                yield return SwitchTo(PaletteState.AMBER);
                yield return JumpOnto(2.6f, 4f, 4.4f);
                yield return JumpSwitchOnto(5.0f, PaletteState.CRIMSON, 4.1f, 5f, 6.6f);
                yield return RunTo(6.6f);
                horizontal = 1f;
                yield return Until(() => X >= takeoff);
                StartCoroutine(Jump(hold));
                yield return new WaitForSeconds(0.1f);
                yield return SwitchTo(PaletteState.AMBER);
                float t0 = Time.time, apex = Feet, blockedX = -1f, blockedFeet = 0f;
                while (Time.time - t0 < 1.5f && !P.IsDead && !(P.IsGrounded && Time.time - t0 > 0.1f))
                {
                    apex = Mathf.Max(apex, Feet);
                    if (blockedX < 0f && X > 9.5f && Mathf.Abs(P.Velocity.x) < 1f) { blockedX = X; blockedFeet = Feet; }
                    yield return null;
                }
                bool landed = !P.IsDead && P.IsGrounded && Feet >= 5.9f;
                allLanded &= landed;
                sb.Append($"hold {hold:0.00}: 최고 발 {apex:0.00}, {(landed ? $"A 착지 x {X:0.00}" : P.IsDead ? "사망" : $"낙하 ({X:0.00},{Feet:0.00})")}{(blockedX > 0f ? $", 벽걸림 @({blockedX:0.00},{blockedFeet:0.00})" : "")} | ");
                horizontal = 0f;
                if (P.IsDead) yield return new WaitForSeconds(GameConstants.RESPAWN_DELAY + 0.2f);
            }
            verdictNote = sb.ToString();
            Debug.Log($"[Playtest][R7조사] 도약 {takeoff:0.0} → {verdictNote}");
            verdict = allLanded;
        }

        // Room 6: C 계단(CRIMSON) → 앰버 코어 @ → E → A → 기류 U → 출구
        IEnumerator Room6()
        {
            yield return SwitchTo(PaletteState.CRIMSON);
            yield return JumpOnto(2.15f, 3f, 3.5f);    // C (x3~5, top 3)
            yield return JumpOnto(4.15f, 4f, 5.5f);    // C (x5~7, top 4)
            yield return JumpOnto(6.15f, 5f, 7.5f);    // # (x7~11, top 5)
            yield return RunTo(9.6f);                  // @ (col 8) 흡수
            yield return Until(() => PM.AmberUnlocked);
            yield return SwitchTo(PaletteState.AMBER); // C 사라지고 A 실체화
            yield return JumpOnto(10.2f, 7f, 11.6f);   // A (x11~13, top 7)
            horizontal = 1f;
            yield return Until(() => X >= 12.5f);
            StartCoroutine(Jump(0.3f));
            yield return Until(() => X >= 15.2f);      // 기류 기둥(x15~16) 안
            horizontal = 0f;
            yield return Until(() => Feet >= 8.5f);    // 천장 아래까지 상승
            yield return RunRightToExit();             // 출구 발판(top 8)
        }

        // Room 7: A(E) → 공중 Q로 C → X 넘으며 공중 E로 A → Y 넘으며 공중 Q로 C → 출구
        /// <param name="late">true: 사람처럼 발판 끝에서 뛰고, 가시 판정 직전(0.02초 전)에야 전환</param>
        IEnumerator Room7(bool late)
        {
            yield return SwitchTo(PaletteState.AMBER);
            yield return JumpOnto(2.6f, 4f, 4.4f);                                         // A (x4~6, top 4)
            yield return JumpSwitchOnto(5.0f, PaletteState.CRIMSON, 4.1f, 5f, 6.6f);      // C (x6~8, top 5)
            yield return RunTo(6.6f);
            if (!late) yield return JumpSwitchOnto(8.2f, PaletteState.AMBER, 5.1f, 6f, 11.3f);           // X(col 10) 통과 → A (x11~13, top 6)
            else yield return JumpSwitchAtX(8.3f, PaletteState.AMBER, 9.875f, 6f, 11.3f);                  // 판정 진입 x 9.875 직전
            yield return RunTo(11.4f);
            if (!late) yield return JumpSwitchOnto(13.2f, PaletteState.CRIMSON, 6.1f, 6f, 16.1f);        // Y(col 15) 통과 → C (x16~17, top 6)
            else yield return JumpSwitchAtX(13.3f, PaletteState.CRIMSON, 14.875f, 6f, 16.1f);              // 판정 진입 x 14.875 직전
            horizontal = 1f;
            yield return Jump(0.3f);                                                       // 출구 발판(top 7)
            yield return RunRightToExit();
        }

        // Room 8: E로 기류 상승(X 무해) → A → 공중 Q로 C → 출구
        IEnumerator Room8()
        {
            yield return SwitchTo(PaletteState.AMBER);
            yield return RunTo(6.5f, 0.08f);           // 기류(x5~7) 안, A(x7~) 밑면에 머리가 걸리지 않는 위치에서 상승
            yield return Until(() => Feet >= 7.2f && P.Velocity.y > 0f);  // 기류 꼭대기(7) 위로 솟는 순간
            horizontal = 1f;
            yield return Until(() => P.IsGrounded && Feet >= 6.95f);  // A (x8~13, top 7)
            horizontal = 0f;
            yield return RunTo(11.8f);
            yield return JumpSwitchOnto(12.6f, PaletteState.CRIMSON, 7.1f, 8f, 14.5f);    // C (x14~16, top 8)
            yield return RunTo(14.6f);
            yield return JumpOnto(15.5f, 8f, 17.2f);                                     // 출구 발판 (x17~, top 8)
            yield return RunRightToExit();
        }

        // Room 9: B 위에서 Q 발사(Y 무해) → Y 위에서 E → 기류 → A → 출구
        IEnumerator Room9()
        {
            yield return RunTo(5.2f, 0.05f);           // 흑백으로 B(x4~5) 위에 섬
            yield return SwitchTo(PaletteState.CRIMSON); // B 발사
            yield return Until(() => P.Velocity.y > 15f);
            horizontal = 1f;
            yield return Until(() => Feet > 5.9f);     // Y(top 5.75) 위
            yield return SwitchTo(PaletteState.AMBER); // 이제 Y가 위험 → 위로 지나가며 기류로
            yield return Until(() => X >= 10.9f);      // 기류(x9~11) 오른쪽 끝에서 멈춰 대기
            horizontal = 0f;
            yield return Until(() => P.InUpdraft);
            yield return Until(() => Feet >= 8.1f && P.Velocity.y > 0f);  // 기류 꼭대기(8) 위로 솟는 순간
            horizontal = 1f;
            yield return Until(() => P.IsGrounded && Feet >= 7.95f);   // A (x12~16, top 8)
            yield return JumpOnto(15.4f, 8f, 17.2f);                   // 출구 발판
            yield return RunRightToExit();
        }

        // Room 10: E로 기류 상승(X 무해) → R 위에서 Q(대각선 발사) → 공중 E → A → 출구
        IEnumerator Room10()
        {
            yield return SwitchTo(PaletteState.AMBER);
            yield return RunTo(5.5f, 0.1f);
            yield return Until(() => Feet >= 6.95f);   // 기류 꼭대기 = R 칸
            yield return SwitchTo(PaletteState.CRIMSON); // 기류 꺼지며 R 발사
            yield return Until(() => P.Velocity.x > 10f);
            horizontal = 1f;
            yield return SwitchTo(PaletteState.AMBER);   // 공중에서 A 실체화
            var trace = new StringBuilder("[Playtest][R10궤적] ");
            while (!P.IsGrounded && !P.IsDead) { trace.Append($"({X:0.0},{Feet:0.0}) "); yield return new WaitForSeconds(0.05f); }
            Debug.Log(trace.ToString());
            horizontal = 0f;
            yield return Until(() => Mathf.Abs(P.Velocity.x) < 0.2f);
            yield return JumpOnto(14.6f, 7f, 17.2f);   // 출구 발판 (x17~, top 7)
            yield return RunRightToExit();
        }

        IEnumerator LedgeWalkProbe()
        {
            customRun = true;
            yield return SwitchTo(PaletteState.AMBER);
            horizontal = 1f;
            float t0 = Time.time, minVx = 99f;
            while (Time.time - t0 < 1.0f)
            {
                if (X > 3.2f && X < 6.5f) minVx = Mathf.Min(minVx, P.Velocity.x);
                yield return null;
            }
            horizontal = 0f;
            verdictNote = $"1초 후 위치 ({X:0.00}, {Feet:0.00}), A 아래 구간 최저 vx {minVx:0.00}";
            verdict = X > 6.5f && Feet < 1.2f && minVx > GameConstants.RUN_SPEED * 0.9f;
        }

        // ───────────── 앰버 틴트 동기화 회귀 테스트 (Room 7) ─────────────
        IEnumerator AmberTintTest()
        {
            customRun = true;
            var tint = PM.Tint;
            int startDesync = tint.DesyncCount;
            var fails = new List<string>();

            IEnumerator Check(string when)
            {
                yield return new WaitForSeconds(0.3f);
                Color32 want = CrimsonTintOverlay.TintColor(PM.State), shown = tint.CurrentColor;
                bool colored = PM.State != PaletteState.MONOCHROME;
                bool ok = tint.ShownState == PM.State && (colored
                    ? tint.VisibleAlpha > 0.2f && tint.Radius >= GameConstants.WAVE_MAX_RADIUS - 0.01f
                      && want.r == shown.r && want.g == shown.g && want.b == shown.b
                    : tint.VisibleAlpha <= 0f);
                if (!ok) fails.Add($"{when}: State={PM.State} 표시={tint.ShownState} alpha={tint.VisibleAlpha:0.00} r={tint.Radius:0.0}");
                if (RadialWaveEffect.ActiveCount > 0) fails.Add($"{when}: 파동 링 {RadialWaveEffect.ActiveCount}개 잔존");
                Debug.Log($"[Playtest][틴트A] {when}: State={PM.State}, 표시={tint.ShownState}, alpha={tint.VisibleAlpha:0.00} → {(ok ? "일치" : "불일치")}");
            }

            // 1) Q·E·0 섞어 연타 (1~5프레임 간격)
            for (int i = 0; i < 40; i++)
            {
                int k = (i * 7) % 5;
                if (k < 2) pendingSwitch = true; else if (k < 4) pendingAmber = true; else pendingOff = true;
                int frames = 1 + (i * 3) % 5;
                for (int f = 0; f < frames; f++) yield return null;
            }
            yield return Check("Q/E/0 연타 40회 후");

            // 2) Q → E → Q 매 프레임 교차
            pendingSwitch = true; yield return null;
            pendingAmber = true; yield return null;
            pendingSwitch = true; yield return null;
            pendingAmber = true; yield return null;
            yield return Check("Q-E-Q-E 매 프레임");

            // 3) Y(col 15) 아래까지 흑백으로 이동 → AMBER 켜고 점프해 Y에 닿아 사망 → 사망 중 Q, E → 리스폰
            yield return SwitchTo(PaletteState.MONOCHROME);
            yield return RunTo(15.5f);
            yield return SwitchTo(PaletteState.AMBER);
            StartCoroutine(Jump(0.3f));
            float tJump = Time.time;
            yield return Until(() => P.IsDead || Time.time - tJump > 1.5f);
            if (!P.IsDead) fails.Add("Y(앰버 가시)에 닿았는데 죽지 않음");
            yield return new WaitForSeconds(0.1f);
            pendingSwitch = true;
            yield return new WaitForSeconds(0.1f);
            pendingAmber = true;
            yield return Until(() => !P.IsDead);
            yield return Check("AMBER 사망 + 사망 중 Q·E 후 리스폰");

            int desync = tint.DesyncCount - startDesync;
            verdictNote = $"강제 동기화 발생 {desync}회, 불일치 {fails.Count}건";
            foreach (var msg in fails) Debug.LogWarning($"[Playtest][틴트A] {msg}");
            verdict = fails.Count == 0;
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
                bool colored = PM.State != PaletteState.MONOCHROME;
                Color32 want = CrimsonTintOverlay.TintColor(PM.State), shown = tint.CurrentColor;
                bool ok = tint.ShownState == PM.State && (colored
                    ? tint.VisibleAlpha > 0.2f && tint.Radius >= GameConstants.WAVE_MAX_RADIUS - 0.01f
                      && want.r == shown.r && want.g == shown.g && want.b == shown.b
                    : tint.VisibleAlpha <= 0f);
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
