using System.Collections.Generic;
using PaletteShaper.Core;
using PaletteShaper.Player;
using UnityEngine;

namespace PaletteShaper.Gimmicks
{
    /// <summary>
    /// 탄성 블록. B = 수직(0, BOUNCE_PAD_VELOCITY), R = 우상단 대각선 SIDE_BOUNCE_VEC.
    /// - CRIMSON 상태에서 닿으면 발사
    /// - MONOCHROME에서 겹친 채로 CRIMSON을 켜는 순간에도 즉시 발사
    /// - 우선순위: R(대각선)이 발동되는 프레임에 겹친 가시(CrimsonHazard)는 사망 판정을 하지 않는다 (SideLaunchAvailable)
    /// Trigger Collider, Layer 8(Crimson_Solid) → MONOCHROME에서는 레이어 충돌 토글로 트리거 자체가 꺼진다.
    /// </summary>
    public class CrimsonBouncePad : PaletteObject
    {
        static readonly List<CrimsonBouncePad> sidePads = new List<CrimsonBouncePad>();

        [SerializeField] Vector2 launchVelocity = new Vector2(0f, GameConstants.BOUNCE_PAD_VELOCITY);
        [SerializeField] float retriggerCooldown = 0.15f;

        Collider2D padTrigger;
        float lastLaunchTime = -10f;

        public Vector2 LaunchVelocity => launchVelocity;
        public bool IsSide => launchVelocity.x != 0f;

        public void ConfigurePad(SpriteRenderer target, Sprite active, Sprite inactive, Vector2 velocity)
        {
            launchVelocity = velocity;
            ConfigureSprite(target, active, inactive);
            RefreshRegistration();
        }

        /// <summary>CRIMSON인 R 패드와 플레이어가 겹쳐 있으면 true (이번 스텝에 R이 발사되므로 가시 판정보다 우선).</summary>
        public static bool SideLaunchAvailable(PlayerController2D player)
        {
            if (player == null) return false;
            var hb = player.HitboxBounds;
            for (int i = 0; i < sidePads.Count; i++)
            {
                var pad = sidePads[i];
                if (pad == null || !pad.IsCrimson || pad.PadTrigger == null) continue;
                if (pad.PadTrigger.bounds.Intersects(hb)) return true;
            }
            return false;
        }

        Collider2D PadTrigger => padTrigger != null ? padTrigger : (padTrigger = GetComponent<Collider2D>());

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshRegistration();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            sidePads.Remove(this);
        }

        void RefreshRegistration()
        {
            sidePads.Remove(this);
            if (IsSide && isActiveAndEnabled) sidePads.Add(this);
        }

        void OnTriggerEnter2D(Collider2D other) => TryLaunch(PlayerOf(other));
        void OnTriggerStay2D(Collider2D other) => TryLaunch(PlayerOf(other));

        protected override void OnPaletteApplied(PaletteState state, bool initial)
        {
            if (initial || state != PaletteState.CRIMSON) return;
            var player = PlayerController2D.Active;
            if (player != null && PadTrigger != null && PadTrigger.bounds.Intersects(player.HitboxBounds))
                TryLaunch(player);
        }

        void TryLaunch(PlayerController2D player)
        {
            if (player == null || player.IsDead || !IsCrimson) return;
            if (Time.time - lastLaunchTime < retriggerCooldown) return;
            lastLaunchTime = Time.time;
            player.Launch(launchVelocity); // R(대각선)이면 Launch 안에서 가시 유예(SIDE_BOUNCE_HAZARD_GRACE)가 부여됨
        }
    }
}
