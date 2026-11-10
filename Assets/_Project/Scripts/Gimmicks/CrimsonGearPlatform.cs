using PaletteShaper.Core;
using UnityEngine;

namespace PaletteShaper.Gimmicks
{
    /// <summary>
    /// 톱니 레일 발판(G + =). 32x16 px = 2x1 Units.
    /// - CRIMSON: 레일 양 끝(pointA ↔ pointB)을 3.0 Units/s로 왕복, 콜라이더 ON, 탑승한 플레이어에게 속도 전달
    /// - MONOCHROME: 그 자리에서 즉시 정지, 콜라이더 OFF, 점선 스프라이트
    /// 플레이어보다 먼저 이동하도록 실행 순서를 앞당긴다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(Rigidbody2D))]
    public class CrimsonGearPlatform : PaletteObject
    {
        [SerializeField] Vector2 pointA;
        [SerializeField] Vector2 pointB;
        [SerializeField] float speed = GameConstants.GEAR_PLATFORM_SPEED;

        Rigidbody2D body;
        int direction = 1;

        /// <summary>이번 물리 스텝의 이동 속도 (정지 시 0)</summary>
        public Vector2 CurrentVelocity { get; private set; }
        public Vector2 Position => Body.position;
        public Vector2 PointA => pointA;
        public Vector2 PointB => pointB;
        /// <summary>+1 = pointB 쪽으로 이동 중, -1 = pointA 쪽으로</summary>
        public int Direction => direction;

        public void ConfigurePlatform(SpriteRenderer target, Sprite active, Sprite inactive, Collider2D solid, Vector2 a, Vector2 b)
        {
            pointA = a;
            pointB = b;
            transform.position = a;
            if (Body != null) Body.position = a;
            ConfigureSprite(target, active, inactive);
            SetCrimsonOnlyColliders(solid);
        }

        Rigidbody2D Body => body != null ? body : (body = GetComponent<Rigidbody2D>());

        void Awake()
        {
            Body.bodyType = RigidbodyType2D.Kinematic;
            Body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        void FixedUpdate()
        {
            if (!IsCrimson || pointA == pointB)
            {
                CurrentVelocity = Vector2.zero;
                return;
            }

            float dt = Time.fixedDeltaTime;
            Vector2 target = direction > 0 ? pointB : pointA;
            Vector2 next = Vector2.MoveTowards(Body.position, target, speed * dt);
            CurrentVelocity = (next - Body.position) / dt;
            Body.MovePosition(next);
            if ((next - target).sqrMagnitude < 1e-6f) direction = -direction;
        }
    }
}
