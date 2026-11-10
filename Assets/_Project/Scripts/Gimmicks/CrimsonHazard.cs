using UnityEngine;

namespace PaletteShaper.Gimmicks
{
    /// <summary>
    /// 크림슨 가시 덩굴(X). CRIMSON일 때 닿으면 사망(0.5초 후 리스폰), MONOCHROME일 때는 무해.
    /// 겹친 채로 CRIMSON을 켜면 다음 물리 스텝의 OnTriggerStay2D에서 사망 처리된다.
    /// 예외(우선순위 BouncePad > Hazard): 대각선 패드(R)와 함께 겹쳐 있거나 R 발사 직후 유예 시간에는 죽지 않는다.
    /// Trigger Collider, Layer 9(Crimson_Hazard).
    /// </summary>
    public class CrimsonHazard : PaletteObject
    {
        void OnTriggerEnter2D(Collider2D other) => TryKill(other);
        void OnTriggerStay2D(Collider2D other) => TryKill(other);

        void TryKill(Collider2D other)
        {
            if (!IsCrimson) return;
            var player = PlayerOf(other);
            if (player == null || player.IsDead) return;
            if (player.HazardImmune || CrimsonBouncePad.SideLaunchAvailable(player)) return;
            player.Kill();
        }
    }
}
