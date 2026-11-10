using System;
using PaletteShaper.Player;
using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>출구 석문(E) 앞 트리거. 플레이어가 들어오면 Reached를 한 번 발행한다.</summary>
    public class RoomExit : MonoBehaviour
    {
        public event Action Reached;
        bool fired;

        public void ResetExit() => fired = false;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (fired) return;
            var body = other.attachedRigidbody;
            if (body == null) return;
            var player = body.GetComponent<PlayerController2D>();
            if (player == null || player.IsDead) return;
            fired = true;
            Reached?.Invoke();
        }
    }
}
