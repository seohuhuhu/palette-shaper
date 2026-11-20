using PaletteShaper.Core;
using UnityEngine;

namespace PaletteShaper.Player
{
    /// <summary>
    /// 스프라이트 프레임 애니메이터 (2026-11-20 art 파이프라인 스프라이트 기준), 좌우는 flipX.
    ///   Idle(4F 반복) → 움직이기 시작하면 Turn(3F 1회, 장당 0.05초) → Run(6F 반복). Run에서 잠깐(0.15초 미만) 멈췄다 가면 Turn 생략
    ///   공중: Jump(2F) / Fall(2F) — 1회 재생 후 마지막 프레임 유지. 기류 안에서 천장에 눌리거나 떠 있을 때(상승 3u/s 미만)는 Fall
    ///   착지(Landed 이벤트): Land(3F 1회). 작은 착지(단계 1)는 2번째 프레임부터.
    ///   Land 중 이동 입력이 있으면 지금 프레임을 한 번 보여준 뒤 Run으로. 점프는 공중 상태가 되며 바로 끊긴다.
    /// PlayerController2D의 자식 "Visual"(SpriteRenderer)에 붙인다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerAnimator : MonoBehaviour
    {
        enum State { Idle, Turn, Run, Jump, Fall, Land }

        [SerializeField] PlayerController2D controller;
        [SerializeField] PaletteShaperAssets assets;
        [SerializeField] float idleFps = 6f;
        [SerializeField] float runFps = 12f;
        [SerializeField] float airFps = 8f;
        [SerializeField] float turnFps = 20f;          // 장당 0.05초, 전체 0.15초 (2026-11-20 비교 GIF로 사용자 선택)
        [SerializeField] float landFps = 10f;
        [Tooltip("이보다 짧게 멈췄다 다시 움직이면 Turn 없이 바로 Run (짧은 연타 때 Turn 반복 방지)")]
        [SerializeField] float turnMinIdleTime = 0.15f;
        [Tooltip("앰버 기류 안에서는 위로 이 속도(u/s) 이상일 때만 Jump(늘어남). 천장에 눌려 있거나 기류 꼭대기에서 떠 있을 땐 Fall 자세 (2026-11-20 사용자 요청: 2-1 천장 밑 늘어남 → 2-3처럼)")]
        [SerializeField] float updraftRiseMinVy = 3f;

        SpriteRenderer spriteRenderer;
        State state = State.Idle;
        float timer;
        int frame;
        bool finished;          // 1회 재생 상태가 마지막 프레임까지 다 보여줬는지
        bool pendingLand;
        int pendingLandStart;
        float idleTime;         // Idle 상태로 머문 시간

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sortingLayerName = GameConstants.SORT_PLAYER;
            if (controller == null) controller = GetComponentInParent<PlayerController2D>();
            idleTime = turnMinIdleTime;
        }

        void OnEnable() { if (controller != null) controller.Landed += OnLanded; }
        void OnDisable() { if (controller != null) controller.Landed -= OnLanded; }

        void OnLanded(float landingVy)
        {
            pendingLand = true;
            pendingLandStart = controller.LastLandTier <= 1 ? 1 : 0;
        }

        Sprite[] FramesOf(State s)
        {
            switch (s)
            {
                case State.Turn: return assets.playerTurn;
                case State.Run: return assets.playerRun;
                case State.Jump: return assets.playerJump;
                case State.Fall: return assets.playerFall;
                case State.Land: return assets.playerLand;
                default: return assets.playerIdle;
            }
        }

        float FpsOf(State s)
        {
            switch (s)
            {
                case State.Turn: return turnFps;
                case State.Run: return runFps;
                case State.Jump: case State.Fall: return airFps;
                case State.Land: return landFps;
                default: return idleFps;
            }
        }

        static bool Loops(State s) => s == State.Idle || s == State.Run;

        bool HasFrames(State s) { var f = FramesOf(s); return f != null && f.Length > 0; }

        void Enter(State s, int startFrame = 0)
        {
            if (!HasFrames(s))
            {
                // 새 스트립이 아직 없으면(Runtime Assets 미갱신) 기존 동작으로 대체
                if (s == State.Turn) s = State.Run;
                else if (s == State.Land) s = State.Idle;
            }
            if (s == State.Idle && state != State.Idle) idleTime = (state == State.Run) ? 0f : turnMinIdleTime;
            state = s;
            frame = Mathf.Clamp(startFrame, 0, Mathf.Max(0, FramesOf(s).Length - 1));
            timer = 0f;
            finished = false;
        }

        void LateUpdate()
        {
            if (controller == null || assets == null) return;

            bool moving = Mathf.Abs(controller.Velocity.x) > 0.1f;
            if (!controller.IsGrounded)
            {
                pendingLand = false;
                float riseMin = controller.InUpdraft ? updraftRiseMinVy : 0.01f;
                State air = controller.Velocity.y > riseMin ? State.Jump : State.Fall;
                if (state != air) Enter(air);
            }
            else
            {
                if (pendingLand) { pendingLand = false; Enter(State.Land, pendingLandStart); }

                switch (state)
                {
                    case State.Land:
                        if (finished) Enter(moving ? State.Run : State.Idle);
                        else if (moving && timer >= 1f / landFps) Enter(State.Run);
                        break;
                    case State.Turn:
                        if (!moving) Enter(State.Idle);
                        else if (finished) Enter(State.Run);
                        break;
                    case State.Idle:
                        if (moving) Enter(idleTime < turnMinIdleTime ? State.Run : State.Turn);
                        else idleTime += Time.deltaTime;
                        break;
                    case State.Run:
                        if (!moving) Enter(State.Idle);
                        break;
                    default: // Jump/Fall인데 Landed 이벤트 없이 내려앉음 (아주 낮은 착지)
                        Enter(moving ? State.Run : State.Idle);
                        break;
                }
            }

            var frames = FramesOf(state);
            if (frames == null || frames.Length == 0) return;

            timer += Time.deltaTime;
            float step = 1f / Mathf.Max(1f, FpsOf(state));
            while (timer >= step && !finished)
            {
                timer -= step;
                if (Loops(state)) frame = (frame + 1) % frames.Length;
                else if (frame < frames.Length - 1) frame++;
                else finished = true;
            }
            if (Loops(state)) timer = Mathf.Min(timer, step);

            spriteRenderer.sprite = frames[Mathf.Clamp(frame, 0, frames.Length - 1)];
            spriteRenderer.flipX = controller.Facing < 0;
        }
    }
}
