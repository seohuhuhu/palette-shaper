using PaletteShaper.Core;
using UnityEngine;

namespace PaletteShaper.Player
{
    /// <summary>
    /// 스프라이트 프레임 애니메이터: Idle(4F) / Run(6F) / Jump(2F) / Fall(2F), 좌우는 flipX.
    /// PlayerController2D의 자식 "Visual"(SpriteRenderer)에 붙인다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] PlayerController2D controller;
        [SerializeField] PaletteShaperAssets assets;
        [SerializeField] float idleFps = 6f;
        [SerializeField] float runFps = 12f;
        [SerializeField] float airFps = 8f;

        SpriteRenderer spriteRenderer;
        Sprite[] current;
        float timer;
        int frame;

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sortingLayerName = GameConstants.SORT_PLAYER;
            if (controller == null) controller = GetComponentInParent<PlayerController2D>();
        }

        void LateUpdate()
        {
            if (controller == null || assets == null) return;

            Sprite[] next;
            float fps;
            if (!controller.IsGrounded)
            {
                next = controller.Velocity.y > 0.01f ? assets.playerJump : assets.playerFall;
                fps = airFps;
            }
            else if (Mathf.Abs(controller.Velocity.x) > 0.1f)
            {
                next = assets.playerRun;
                fps = runFps;
            }
            else
            {
                next = assets.playerIdle;
                fps = idleFps;
            }
            if (next == null || next.Length == 0) return;

            if (next != current)
            {
                current = next;
                frame = 0;
                timer = 0f;
            }
            else
            {
                timer += Time.deltaTime;
                float step = 1f / Mathf.Max(1f, fps);
                while (timer >= step)
                {
                    timer -= step;
                    frame = (frame + 1) % current.Length;
                }
            }

            spriteRenderer.sprite = current[frame];
            spriteRenderer.flipX = controller.Facing < 0;
        }
    }
}
