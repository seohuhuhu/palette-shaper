using System;
using System.Collections;
using PaletteShaper.Core;
using PaletteShaper.Gimmicks;
using UnityEngine;

namespace PaletteShaper.Player
{
    /// <summary>
    /// Unit 기반 정밀 플랫포머 컨트롤러 (1 Unit = 16 px).
    /// - 물리 엔진 중력 대신(gravityScale 0) GameConstants 상수로 속도를 직접 계산한다.
    /// - 벽/천장 충돌은 Rigidbody2D 해석 결과를 매 스텝 다시 읽어 반영한다.
    /// - 코요테 타임, 점프 버퍼, 가변 점프, 탄성 발사, 톱니 발판 탑승, 크림슨 끼임 밀어내기, 사망/리스폰.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public class PlayerController2D : MonoBehaviour
    {
        public static PlayerController2D Active { get; private set; }

        [Tooltip("SpriteRenderer가 붙은 자식(스쿼시/스트레치 대상)")]
        [SerializeField] Transform visual;
        [Tooltip("이 Y 아래로 떨어지면 사망 (방 바닥 아래 1 Unit)")]
        [SerializeField] float deathY = -1f;

        [Header("Squash & Stretch")]
        [SerializeField] Vector2 jumpStretch = new Vector2(0.8f, 1.2f);
        // 착지 스쿼시는 GameConstants.LAND_* 정수 px 키프레임으로 처리 (StartLandSquash)
        [SerializeField] Vector2 bounceStretch = new Vector2(0.75f, 1.3f);
        [SerializeField] float scaleRecoverSpeed = 14f;

        public bool IsGrounded { get; private set; }
        public bool IsDead { get; private set; }
        public int Facing { get; private set; } = 1;
        public Vector2 Velocity => velocity;
        public Vector2 SpawnPoint { get; private set; }
        public Collider2D GroundCollider { get; private set; }
        public Bounds HitboxBounds => box.bounds;
        /// <summary>이번 물리 스텝에 앰버 상승 기류의 영향을 받았는지</summary>
        public bool InUpdraft { get; private set; }
        float hazardGraceUntil = -10f;
        /// <summary>대각선 패드(R) 발사 직후 잠깐 가시 판정을 무시 (BouncePad > Hazard 우선순위)</summary>
        public bool HazardImmune => Time.time < hazardGraceUntil;
        public void GrantHazardGrace(float seconds) => hazardGraceUntil = Mathf.Max(hazardGraceUntil, Time.time + seconds);

        /// <summary>보간 없는 물리 위치 (피벗 = 발바닥 중앙)</summary>
        public Vector2 BodyPosition => rb != null ? rb.position : (Vector2)transform.position;
        public string DebugState => $"jumps {debugJumpCount}  last rise {LastJumpRise:0.000} u ({LastJumpRise * GameConstants.PIXELS_PER_UNIT:0.0} px)";
        int debugJumpCount;
        float debugTakeoffY, debugApexY;
        bool halfGravityNextStep;
        /// <summary>마지막 점프/발사의 실측 상승 높이 (명세 JUMP_HEIGHT 3.25와 비교용)</summary>
        public float LastJumpRise => debugApexY - debugTakeoffY;

        public event Action Died;
        public event Action Respawned;
        /// <summary>점프 시작 (Game Juice / 사운드용)</summary>
        public event Action Jumped;
        /// <summary>착지. 인자 = 착지 직전 낙하 속도(음수)</summary>
        public event Action<float> Landed;
        /// <summary>탄성 패드 발사. 인자 = 발사 속도</summary>
        public event Action<Vector2> Launched;
        /// <summary>탄성 패드로 발사된 뒤 아직 착지 전인지</summary>
        public bool IsLaunched => isLaunched;
        /// <summary>전환 시 끼임 보정이 일어난 횟수/마지막 내용 (테스트 검증용)</summary>
        public int OverlapFixCount { get; private set; }
        public int LedgeAssistCount { get; private set; }
        float lastGroundFeet = float.NegativeInfinity;
        public string LastOverlapFix { get; private set; } = "";
        /// <summary>보간된 표시 위치(피벗 = 발바닥 중앙)와 스프라이트 — 잔상용</summary>
        public SpriteRenderer VisualRenderer => visualRenderer != null ? visualRenderer : (visual != null ? visualRenderer = visual.GetComponent<SpriteRenderer>() : null);
        SpriteRenderer visualRenderer;

        const float GroundProbeDistance = 0.06f;
        const float SkinWidth = 0.02f;
        const float PixelSize = 1f / GameConstants.PIXELS_PER_UNIT;
        const int MaxUpSnapPixels = 6; // OVERLAP_UP_SNAP_MAX (0.375 Unit)

        Rigidbody2D rb;
        BoxCollider2D box;
        Vector2 velocity;       // 플레이어 자체 속도 (발판 이동분 제외)
        Vector2 carryVelocity;  // 톱니 발판에서 전달받은 속도
        float inputX;
        float coyoteTimer;
        float jumpBufferTimer;
        float lastAirborneVy;
        float groundGap;
        float launchHoldTimer;
        bool jumpReleaseQueued;
        bool isJumping;
        bool isLaunched;
        Vector2 visualScale = Vector2.one;
        float airPeakY;                                  // 땅을 떠난 뒤 최고 y (착지 연출 세기용, 물리 무관)
        GameConstants.LandSquashKey[] landKeys;           // 재생 중인 착지 키프레임 (null = 없음)
        float landTime;
        int landOffsetPx;                                // 벽 쪽 파고듦 방지용 비주얼 x 오프셋 (px)
        bool landNoWiden;                                // 양쪽이 다 막혀 폭을 늘리지 않음
        /// <summary>마지막 착지 스쿼시 단계 (0 = 없음, 1~4 px) — 디버그/검증용</summary>
        public int LastLandTier { get; private set; }
        CrimsonGearPlatform groundPlatform;
        PaletteManager boundManager;
        Coroutine respawnRoutine;

        readonly RaycastHit2D[] castHits = new RaycastHit2D[4];
        readonly Collider2D[] overlapHits = new Collider2D[8];

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            box = GetComponent<BoxCollider2D>();

            gameObject.layer = GameConstants.LAYER_PLAYER;
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            rb.sharedMaterial = new PhysicsMaterial2D("Player_NoFriction") { friction = 0f, bounciness = 0f };

            box.size = GameConstants.PLAYER_HITBOX_SIZE;               // 12x16 px
            box.offset = new Vector2(0f, GameConstants.PLAYER_HITBOX_SIZE.y * 0.5f); // 피벗 = 발바닥 중앙
            box.edgeRadius = 0f;

            SpawnPoint = rb.position;
        }

        void OnEnable()
        {
            Active = this;
            BindManager();
        }

        void Start() => BindManager();

        void OnDisable()
        {
            if (Active == this) Active = null;
            if (boundManager != null) boundManager.PaletteChanging -= OnPaletteChanging;
            boundManager = null;
        }

        void BindManager()
        {
            if (boundManager != null) return;
            boundManager = PaletteManager.Instance;
            if (boundManager == null) return;
            boundManager.PaletteChanging += OnPaletteChanging;
            boundManager.SetWaveOrigin(transform);
        }

        // ───────────── 입력 (프레임 단위) ─────────────
        void Update()
        {
            if (IsDead) return;
            inputX = GameInput.Horizontal;
            if (inputX != 0f) Facing = inputX > 0f ? 1 : -1;
            if (GameInput.JumpPressed) jumpBufferTimer = GameConstants.JUMP_BUFFER_TIME;
            if (GameInput.JumpReleased) jumpReleaseQueued = true;
            UpdateVisualScale(Time.deltaTime);
        }

        // ───────────── 이동 (물리 스텝 단위) ─────────────
        void FixedUpdate()
        {
            if (IsDead) return;
            float dt = Time.fixedDeltaTime;

            // 1) 이전 스텝의 충돌 해석 결과 반영 (벽/천장에 막힌 성분은 0이 되어 돌아온다)
            velocity = rb.linearVelocity - carryVelocity;
            // 점프 실측 높이는 LastJumpRise / DebugState (DebugRoomLoader HUD)에서 확인

            // 2) 접지
            bool wasGrounded = IsGrounded;
            bool wasLaunched = isLaunched;
            UpdateGround();
            if (IsGrounded)
            {
                coyoteTimer = GameConstants.COYOTE_TIME;
                isJumping = false;
                isLaunched = false;
                if (!wasGrounded && lastAirborneVy < -GameConstants.LAND_MIN_FALL_SPEED)
                {
                    StartLandSquash(lastAirborneVy, airPeakY - rb.position.y, wasLaunched);
                    Landed?.Invoke(lastAirborneVy);
                }
            }
            else
            {
                coyoteTimer -= dt;
            }
            jumpBufferTimer -= dt;

            // 3) 좌우 가감속
            //    대각선 탄성 발사: 0.25초간 수평 속도 강제 유지 → 이후 RUN_SPEED를 넘는 동안은 AIR_DECEL * 0.4로 서서히 감속
            float target = inputX * GameConstants.RUN_SPEED;
            if (IsGrounded) launchHoldTimer = 0f;
            if (launchHoldTimer > 0f)
            {
                launchHoldTimer -= dt;
            }
            else if (isLaunched && !IsGrounded && Mathf.Abs(velocity.x) > GameConstants.RUN_SPEED)
            {
                velocity.x = Mathf.MoveTowards(velocity.x, target,
                    GameConstants.AIR_DECEL * GameConstants.SIDE_BOUNCE_DRAG_MULT * dt);
            }
            else
            {
                bool accelerating = Mathf.Abs(target) > 0.01f;
                float rate = IsGrounded
                    ? (accelerating ? GameConstants.GROUND_ACCEL : GameConstants.GROUND_DECEL)
                    : (accelerating ? GameConstants.AIR_ACCEL : GameConstants.AIR_DECEL);
                velocity.x = Mathf.MoveTowards(velocity.x, target, rate * dt);
            }

            // 4) 점프 (버퍼 + 코요테)
            if (jumpBufferTimer > 0f && coyoteTimer > 0f)
            {
                velocity.y = GameConstants.JUMP_VELOCITY;
                halfGravityNextStep = true;
                debugJumpCount++;
                debugTakeoffY = rb.position.y;
                debugApexY = debugTakeoffY;
                isJumping = true;
                isLaunched = false;
                IsGrounded = false;
                GroundCollider = null;
                groundPlatform = null;
                jumpBufferTimer = 0f;
                coyoteTimer = 0f;
                SetVisualScale(jumpStretch);
                Jumped?.Invoke();
            }

            // 5) 가변 점프: 상승 중 점프 키를 떼면 Y 속도 × 0.45 (탄성 발사에는 적용 안 함)
            if (jumpReleaseQueued)
            {
                if (isJumping && velocity.y > 0f && !GameInput.JumpHeld)
                    velocity.y *= GameConstants.VARIABLE_JUMP_MULT;
                isJumping = false;
                jumpReleaseQueued = false;
            }

            // 6) 중력 + 종단 속도 (앰버 상승 기류 안에서는 중력 대신 기류 가속)
            InUpdraft = Gimmicks.AmberUpdraft.Affects(box.bounds);
            if (InUpdraft)
            {
                halfGravityNextStep = false;
                velocity.y = Mathf.MoveTowards(velocity.y, GameConstants.UPDRAFT_MAX_SPEED, GameConstants.UPDRAFT_ACCEL * dt);
            }
            else if (IsGrounded && velocity.y <= 0f)
                velocity.y = -groundGap / dt; // 바닥까지 남은 틈을 이번 스텝에 메워 픽셀 단위로 붙인다
            else
            {
                // 점프/발사 직후 첫 스텝은 중력을 절반만 적용(중점 적분) → 실제 상승 높이가 공식값(3.25 / 6.5타일)과 일치
                float g = GameConstants.GRAVITY * dt * (halfGravityNextStep ? 0.5f : 1f);
                halfGravityNextStep = false;
                velocity.y = Mathf.Max(velocity.y + g, GameConstants.MAX_FALL_SPEED);
            }

            // 6.5) 코너 슬립 보정 (모서리에 3px 이하로 걸리면 비켜 지나감)
            ApplyCornerCorrection(dt);

            // 7) 톱니 발판 탑승 시 발판 속도 전달
            carryVelocity = IsGrounded && groundPlatform != null ? groundPlatform.CurrentVelocity : Vector2.zero;
            rb.linearVelocity = velocity + carryVelocity;
            if (!IsGrounded && rb.position.y > debugApexY) debugApexY = rb.position.y;
            if (!IsGrounded) lastAirborneVy = velocity.y;
            airPeakY = IsGrounded ? rb.position.y : Mathf.Max(airPeakY, rb.position.y);

            // 8) 하단 추락 사망
            if (rb.position.y < deathY) Kill();
        }

        void UpdateGround()
        {
            IsGrounded = false;
            GroundCollider = null;
            groundPlatform = null;
            if (velocity.y > 0.01f) return;

            int mask = 1 << GameConstants.LAYER_TERRAIN_BASE;
            if (PaletteManager.IsCrimsonNow) mask |= 1 << GameConstants.LAYER_CRIMSON_SOLID;
            if (PaletteManager.IsAmberNow) mask |= 1 << GameConstants.LAYER_AMBER_SOLID;
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = mask };

            Bounds b = box.bounds;
            var origin = new Vector2(b.center.x, b.min.y + SkinWidth);
            var size = new Vector2(b.size.x - SkinWidth * 2f, SkinWidth);
            int n = Physics2D.BoxCast(origin, size, 0f, Vector2.down, filter, castHits, GroundProbeDistance + SkinWidth);
            for (int i = 0; i < n; i++)
            {
                var hit = castHits[i];
                if (hit.collider == box || hit.normal.y < 0.7f) continue;
                IsGrounded = true;
                lastGroundFeet = rb.position.y;
                groundGap = Mathf.Max(0f, hit.distance - SkinWidth * 0.5f); // 캐스트 박스 밑면 = 발바닥 + SkinWidth/2
                GroundCollider = hit.collider;
                groundPlatform = hit.rigidbody != null ? hit.rigidbody.GetComponent<CrimsonGearPlatform>() : null;
                break;
            }
        }

        // ───────────── 외부 호출 ─────────────
        public void SetSpawnPoint(Vector2 point, bool teleport)
        {
            SpawnPoint = point;
            if (teleport) MoveBodyTo(point, resetVelocity: true);
        }

        /// <summary>탄성 블록 발사. 현재 속도를 덮어쓴다.</summary>
        public void Launch(Vector2 launchVelocity)
        {
            if (IsDead) return;
            velocity = launchVelocity;
            carryVelocity = Vector2.zero;
            rb.linearVelocity = launchVelocity;
            halfGravityNextStep = true;
            debugTakeoffY = rb.position.y;
            debugApexY = debugTakeoffY;
            isJumping = false;
            isLaunched = true;
            IsGrounded = false;
            GroundCollider = null;
            groundPlatform = null;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            launchHoldTimer = launchVelocity.x != 0f ? GameConstants.SIDE_BOUNCE_HOLD_TIME : 0f;
            if (launchVelocity.x != 0f) GrantHazardGrace(GameConstants.SIDE_BOUNCE_HAZARD_GRACE);

            if (launchVelocity.x != 0f) Facing = launchVelocity.x > 0f ? 1 : -1;
            SetVisualScale(bounceStretch);
            Launched?.Invoke(launchVelocity);
        }

        public void Kill()
        {
            if (IsDead) return;
            IsDead = true;
            rb.linearVelocity = Vector2.zero;
            // 사망 시 이동 상태 초기화 (발사·기류 상태가 남아 있으면 숨겨진 자리에서 잔상이 계속 생김)
            velocity = Vector2.zero;
            carryVelocity = Vector2.zero;
            isLaunched = false;
            InUpdraft = false;
            rb.simulated = false;
            if (visual != null) visual.gameObject.SetActive(false);
            Died?.Invoke();
            respawnRoutine = StartCoroutine(RespawnAfterDelay());
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(GameConstants.RESPAWN_DELAY);
            Respawn();
        }

        public void Respawn()
        {
            if (respawnRoutine != null) { StopCoroutine(respawnRoutine); respawnRoutine = null; }
            rb.simulated = true;
            MoveBodyTo(SpawnPoint, resetVelocity: true);
            coyoteTimer = jumpBufferTimer = 0f;
            jumpReleaseQueued = isJumping = isLaunched = false;
            launchHoldTimer = 0f;
            IsGrounded = false;
            visualScale = Vector2.one;
            airPeakY = SpawnPoint.y;
            StopLandSquash();
            if (visual != null)
            {
                visual.localScale = Vector3.one;
                visual.localPosition = Vector3.zero;
                visual.gameObject.SetActive(true);
            }
            IsDead = false;
            Respawned?.Invoke();
        }

        // ───────────── 코너 슬립 보정 (Corner Correction, GDD 4.2) ─────────────
        static readonly int[] NudgeDirs = { -1, 1 };

        int SolidMask()
        {
            int mask = 1 << GameConstants.LAYER_TERRAIN_BASE;
            if (PaletteManager.IsCrimsonNow) mask |= 1 << GameConstants.LAYER_CRIMSON_SOLID;
            if (PaletteManager.IsAmberNow) mask |= 1 << GameConstants.LAYER_AMBER_SOLID;
            return mask;
        }

        /// <summary>
        /// 1) 상승 중 머리가 천장 모서리에 CORNER_NUDGE_MAX 이하로 걸리면 좌우로 비켜 올라간다.
        /// 2) 수평 이동 중 발끝/머리가 블록 모서리에 CORNER_NUDGE_MAX 이하로 걸리면 위(발끝) 또는 아래(머리, 공중일 때만)로 비켜 지나간다.
        /// 이동 경로가 실제로 비어 있을 때만 1px 단위로 최소 거리만 옮긴다 (속도는 그대로).
        /// </summary>
        void ApplyCornerCorrection(float dt)
        {
            int maxPx = Mathf.RoundToInt(GameConstants.CORNER_NUDGE_MAX * GameConstants.PIXELS_PER_UNIT);
            var filter = MakeFilter(SolidMask());
            Vector2 pos = rb.position;
            Vector2 center = pos + box.offset;
            Vector2 size = box.size - new Vector2(0.02f, 0.02f);

            if (velocity.y > 0.01f)
            {
                float dy = velocity.y * dt + 0.01f;
                if (Physics2D.BoxCast(center, size, 0f, Vector2.up, filter, castHits, dy) > 0)
                {
                    for (int px = 1; px <= maxPx; px++)
                        foreach (int dir in NudgeDirs)
                        {
                            Vector2 shift = new Vector2(dir * px * PixelSize, 0f);
                            if (OverlapAt(pos + shift, filter) == 0 && OverlapAt(pos + shift + Vector2.up * dy, filter) == 0)
                            {
                                MoveBodyTo(pos + shift, resetVelocity: false);
                                return;
                            }
                        }
                }
            }

            if (Mathf.Abs(velocity.x) > 0.01f)
            {
                float dir = Mathf.Sign(velocity.x);
                float dx = Mathf.Abs(velocity.x) * dt + 0.01f;
                Vector2 ahead = new Vector2(dir * dx, 0f);
                if (Physics2D.BoxCast(center, size, 0f, new Vector2(dir, 0f), filter, castHits, dx) > 0)
                {
                    bool rising = !IsGrounded && velocity.y > 0.01f;
                    bool falling = !IsGrounded && velocity.y <= 0.01f;
                    int assistPx = Mathf.RoundToInt(GameConstants.LEDGE_ASSIST_MAX * GameConstants.PIXELS_PER_UNIT);
                    int upLimit = rising ? maxPx : falling ? assistPx : 0;
                    for (int px = 1; px <= Mathf.Max(maxPx, upLimit); px++)
                    {
                        Vector2 up = Vector2.up * (px * PixelSize);
                        // 낙하 중 턱 보조: 마지막 지면보다 충분히 높은 발판일 때만 (1칸 홈 건너뛰기 방지)
                        bool upAllowed = px <= upLimit &&
                            (rising || pos.y + px * PixelSize >= lastGroundFeet + GameConstants.LEDGE_ASSIST_MIN_RISE);
                        if (upAllowed && OverlapAt(pos + up, filter) == 0 && OverlapAt(pos + up + ahead, filter) == 0
                            && Physics2D.BoxCast(pos + up + ahead + box.offset, size, 0f, Vector2.down, filter, castHits, px * PixelSize + 0.02f) > 0)
                        {
                            MoveBodyTo(pos + up, resetVelocity: false);
                            if (velocity.y < 0f) velocity.y = 0f;
                            if (falling) LedgeAssistCount++;
                            return;
                        }
                        if (px > maxPx) continue;
                        if (!IsGrounded && velocity.y <= 0f &&
                            OverlapAt(pos - up, filter) == 0 && OverlapAt(pos - up + ahead, filter) == 0)
                        {
                            MoveBodyTo(pos - up, resetVelocity: false);
                            return;
                        }
                    }
                }
            }
        }

        // ───────────── 끼임 밀어내기 보정 (Overlap Push-out Grace) ─────────────
        void OnPaletteChanging(PaletteState next)
        {
            if (IsDead) return;
            if (next == PaletteState.CRIMSON) ResolveColorOverlap(GameConstants.LAYER_CRIMSON_SOLID);
            else if (next == PaletteState.AMBER) ResolveColorOverlap(GameConstants.LAYER_AMBER_SOLID);
        }

        /// <summary>
        /// 비활성 색 블록(크림슨/앰버)과 겹친 채 그 색으로 전환할 때 죽이지 않고 밖으로 밀어낸다.
        /// 1) 위로 6px(0.375 Unit) 이하로 빠져나갈 수 있으면 무조건 위로 올려 착지
        /// 2) 아니면 Physics2D.Distance로 구한 최소 침투 방향으로 즉시 Snap (최대 4회 반복)
        /// </summary>
        void ResolveColorOverlap(int colorLayer)
        {
            Physics2D.SyncTransforms();
            var crimsonOnly = MakeFilter(1 << colorLayer);
            var allSolids = MakeFilter((1 << GameConstants.LAYER_TERRAIN_BASE) | (1 << colorLayer));
            Vector2 pos = rb.position;
            if (OverlapAt(pos, crimsonOnly) == 0) return;
            OverlapFixCount++;
            LastOverlapFix = $"겹침 @({pos.x:0.00},{pos.y:0.00}) v({velocity.x:0.0},{velocity.y:0.0})";

            // 공중에서 마지막 지면보다 높은 발판을 켠 경우엔 턱 보조와 같은 한도(8px)까지 올려 세움
            int assistPx = Mathf.RoundToInt(GameConstants.LEDGE_ASSIST_MAX * GameConstants.PIXELS_PER_UNIT);
            int upMax = MaxUpSnapPixels;
            if (!IsGrounded) upMax = Mathf.Max(upMax, assistPx);
            for (int px = 1; px <= upMax; px++)
            {
                Vector2 candidate = pos + Vector2.up * (px * PixelSize);
                if (px > MaxUpSnapPixels && candidate.y < lastGroundFeet + GameConstants.LEDGE_ASSIST_MIN_RISE) break;
                if (OverlapAt(candidate, allSolids) != 0) continue;
                MoveBodyTo(candidate, resetVelocity: false);
                if (velocity.y < 0f) { velocity.y = 0f; rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f); }
                return;
            }

            for (int iteration = 0; iteration < 4; iteration++)
            {
                int n = OverlapAt(pos, allSolids);
                if (n == 0) break;
                Vector2 best = Vector2.zero;
                for (int i = 0; i < n; i++)
                {
                    var other = overlapHits[i];
                    if (other == null || other == box) continue;
                    ColliderDistance2D d = other.Distance(box);
                    if (!d.isOverlapped) continue;
                    Vector2 separation = d.pointA - d.pointB; // box를 other 밖으로 꺼내는 최소 이동
                    if (separation.sqrMagnitude > best.sqrMagnitude) best = separation;
                }
                if (best == Vector2.zero) break;
                pos += best + best.normalized * 0.001f;
                MoveBodyTo(pos, resetVelocity: false);
            }
        }

        static ContactFilter2D MakeFilter(int mask) =>
            new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = mask };

        int OverlapAt(Vector2 bodyPosition, ContactFilter2D filter)
        {
            Vector2 center = bodyPosition + box.offset;
            Vector2 size = box.size - new Vector2(0.01f, 0.01f);
            return Physics2D.OverlapBox(center, size, 0f, filter, overlapHits);
        }

        void MoveBodyTo(Vector2 position, bool resetVelocity)
        {
            rb.position = position;
            transform.position = position;
            if (resetVelocity)
            {
                rb.linearVelocity = Vector2.zero;
                velocity = Vector2.zero;
                carryVelocity = Vector2.zero;
            }
            Physics2D.SyncTransforms();
        }

        // ───────────── 스쿼시 & 스트레치 ─────────────
        /// <summary>점프/발사 스트레치 (기존 방식). 재생 중인 착지 스쿼시는 즉시 끊는다.</summary>
        void SetVisualScale(Vector2 scale)
        {
            StopLandSquash();
            visualScale = scale;
        }

        void StopLandSquash()
        {
            landKeys = null;
            landOffsetPx = 0;
            landNoWiden = false;
        }

        /// <summary>낙하 거리로 단계를 정하고, 착지 직전 낙하 속도로 상한을 건다 (GameConstants.LAND_*).</summary>
        public static int LandSquashTier(float landingVy, float drop, bool launched)
        {
            float v = -landingVy;
            if (v <= GameConstants.LAND_MIN_FALL_SPEED) return 0;
            int tier = launched || drop >= GameConstants.LAND_TIER4_MIN_DROP ? 4
                     : drop >= GameConstants.LAND_TIER3_MIN_DROP ? 3
                     : drop >= GameConstants.LAND_TIER2_MIN_DROP ? 2 : 1;
            if (v < GameConstants.LAND_CAP1_SPEED) tier = Mathf.Min(tier, 1);
            else if (v < GameConstants.LAND_CAP2_SPEED) tier = Mathf.Min(tier, 2);
            return tier;
        }

        void StartLandSquash(float landingVy, float drop, bool launched)
        {
            int tier = LandSquashTier(landingVy, drop, launched);
            LastLandTier = tier;
            if (tier == 0) { StopLandSquash(); return; }
            PlayLandSquash(tier);
        }

        /// <summary>단계(1~4) 착지 키프레임 재생. 벽에 붙어 있으면 열린 쪽으로만 넓힌다.</summary>
        public void PlayLandSquash(int tier)
        {
            tier = Mathf.Clamp(tier, 1, GameConstants.LAND_SQUASH_KEYS.Length);
            landKeys = GameConstants.LAND_SQUASH_KEYS[tier - 1];
            landTime = 0f;
            visualScale = Vector2.one;

            // 몸통 좌우 1px 안에 고체가 있는지 (시각 판정용 조회만, 물리 상태는 바꾸지 않음)
            bool wallLeft = WallWithinOnePixel(-1f), wallRight = WallWithinOnePixel(1f);
            landNoWiden = wallLeft && wallRight;
            landOffsetPx = landNoWiden ? 0 : wallLeft ? 1 : wallRight ? -1 : 0;
        }

        bool WallWithinOnePixel(float dir)
        {
            if (box == null) return false;
            Bounds b = box.bounds;
            // 바닥/천장에 걸리지 않도록 세로는 위아래 2px씩 줄이고, 가로는 0.01 줄인 뒤 1px + 0.01만큼 캐스트
            var size = new Vector2(b.size.x - 0.02f, b.size.y - 4f * PixelSize);
            int n = Physics2D.BoxCast(b.center, size, 0f, new Vector2(dir, 0f), MakeFilter(SolidMask()), castHits, PixelSize + 0.01f);
            for (int i = 0; i < n; i++)
                if (castHits[i].collider != box) return true;
            return false;
        }

        void UpdateVisualScale(float dt)
        {
            Vector2 offset = Vector2.zero;
            if (landKeys != null)
            {
                landTime += dt;
                float t = landTime;
                int i = 0;
                while (i < landKeys.Length && t >= landKeys[i].duration) { t -= landKeys[i].duration; i++; }
                if (i >= landKeys.Length)
                {
                    StopLandSquash();
                    visualScale = Vector2.one;
                }
                else
                {
                    int w = landKeys[i].widthPx;
                    int maxW = GameConstants.LAND_BODY_W_PX + (landNoWiden ? 0 : GameConstants.LAND_MAX_WIDEN_PX);
                    w = Mathf.Min(w, maxW);
                    visualScale = new Vector2((float)w / GameConstants.LAND_BODY_W_PX,
                                              (float)landKeys[i].heightPx / GameConstants.LAND_BODY_H_PX);
                    // 폭이 늘어날 때만 벽 반대쪽으로 1px 이동 → 벽 쪽 가장자리는 제자리, 열린 쪽으로 2px
                    if (w > GameConstants.LAND_BODY_W_PX) offset.x = landOffsetPx * PixelSize;
                }
            }
            else
            {
                visualScale = Vector2.Lerp(visualScale, Vector2.one, 1f - Mathf.Exp(-scaleRecoverSpeed * dt));
            }
            if (visual != null)
            {
                visual.localScale = new Vector3(visualScale.x, visualScale.y, 1f);
                visual.localPosition = new Vector3(offset.x, 0f, visual.localPosition.z);
            }
        }
    }
}
