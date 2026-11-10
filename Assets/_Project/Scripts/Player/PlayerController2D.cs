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
        [SerializeField] Vector2 landSquash = new Vector2(1.2f, 0.8f);
        [SerializeField] Vector2 bounceStretch = new Vector2(0.75f, 1.3f);
        [SerializeField] float scaleRecoverSpeed = 14f;

        public bool IsGrounded { get; private set; }
        public bool IsDead { get; private set; }
        public int Facing { get; private set; } = 1;
        public Vector2 Velocity => velocity;
        public Vector2 SpawnPoint { get; private set; }
        public Collider2D GroundCollider { get; private set; }
        public Bounds HitboxBounds => box.bounds;
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
            UpdateGround();
            if (IsGrounded)
            {
                coyoteTimer = GameConstants.COYOTE_TIME;
                isJumping = false;
                isLaunched = false;
                if (!wasGrounded && lastAirborneVy < -2f) SetVisualScale(landSquash);
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
            }

            // 5) 가변 점프: 상승 중 점프 키를 떼면 Y 속도 × 0.45 (탄성 발사에는 적용 안 함)
            if (jumpReleaseQueued)
            {
                if (isJumping && velocity.y > 0f && !GameInput.JumpHeld)
                    velocity.y *= GameConstants.VARIABLE_JUMP_MULT;
                isJumping = false;
                jumpReleaseQueued = false;
            }

            // 6) 중력 + 종단 속도
            if (IsGrounded && velocity.y <= 0f)
                velocity.y = -groundGap / dt; // 바닥까지 남은 틈을 이번 스텝에 메워 픽셀 단위로 붙인다
            else
            {
                // 점프/발사 직후 첫 스텝은 중력을 절반만 적용(중점 적분) → 실제 상승 높이가 공식값(3.25 / 6.5타일)과 일치
                float g = GameConstants.GRAVITY * dt * (halfGravityNextStep ? 0.5f : 1f);
                halfGravityNextStep = false;
                velocity.y = Mathf.Max(velocity.y + g, GameConstants.MAX_FALL_SPEED);
            }

            // 7) 톱니 발판 탑승 시 발판 속도 전달
            carryVelocity = IsGrounded && groundPlatform != null ? groundPlatform.CurrentVelocity : Vector2.zero;
            rb.linearVelocity = velocity + carryVelocity;
            if (!IsGrounded && rb.position.y > debugApexY) debugApexY = rb.position.y;
            if (!IsGrounded) lastAirborneVy = velocity.y;

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
        }

        public void Kill()
        {
            if (IsDead) return;
            IsDead = true;
            rb.linearVelocity = Vector2.zero;
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
            if (visual != null)
            {
                visual.localScale = Vector3.one;
                visual.gameObject.SetActive(true);
            }
            IsDead = false;
            Respawned?.Invoke();
        }

        // ───────────── 끼임 밀어내기 보정 (Overlap Push-out Grace) ─────────────
        void OnPaletteChanging(PaletteState next)
        {
            if (next == PaletteState.CRIMSON && !IsDead) ResolveCrimsonOverlap();
        }

        /// <summary>
        /// 비활성 크림슨 블록과 겹친 채 CRIMSON으로 전환할 때 죽이지 않고 밖으로 밀어낸다.
        /// 1) 위로 6px(0.375 Unit) 이하로 빠져나갈 수 있으면 무조건 위로 올려 착지
        /// 2) 아니면 Physics2D.Distance로 구한 최소 침투 방향으로 즉시 Snap (최대 4회 반복)
        /// </summary>
        void ResolveCrimsonOverlap()
        {
            Physics2D.SyncTransforms();
            var crimsonOnly = MakeFilter(1 << GameConstants.LAYER_CRIMSON_SOLID);
            var allSolids = MakeFilter((1 << GameConstants.LAYER_TERRAIN_BASE) | (1 << GameConstants.LAYER_CRIMSON_SOLID));
            Vector2 pos = rb.position;
            if (OverlapAt(pos, crimsonOnly) == 0) return;

            for (int px = 1; px <= MaxUpSnapPixels; px++)
            {
                Vector2 candidate = pos + Vector2.up * (px * PixelSize);
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
        void SetVisualScale(Vector2 scale) => visualScale = scale;

        void UpdateVisualScale(float dt)
        {
            visualScale = Vector2.Lerp(visualScale, Vector2.one, 1f - Mathf.Exp(-scaleRecoverSpeed * dt));
            if (visual != null) visual.localScale = new Vector3(visualScale.x, visualScale.y, 1f);
        }
    }
}
