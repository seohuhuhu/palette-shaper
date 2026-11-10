using System;
using System.Collections;
using PaletteShaper.Core;
using PaletteShaper.Player;
using UnityEngine;

namespace PaletteShaper.Gimmicks
{
    /// <summary>
    /// 크림슨 코어(*). Room 1 전용. 맥동 4프레임 루프.
    /// 플레이어가 닿으면 흡수(0.2초) 연출 후 사라지고 Q키 팔레트 전환 권한을 해금한다.
    /// </summary>
    public class CrimsonCore : MonoBehaviour
    {
        [SerializeField] SpriteRenderer spriteRenderer;
        [SerializeField] Sprite[] frames;
        [SerializeField] float fps = 8f;
        [SerializeField] float absorbDuration = 0.2f;

        public static event Action Collected;

        bool collected;
        float timer;

        public void Configure(SpriteRenderer target, Sprite[] pulseFrames)
        {
            spriteRenderer = target;
            frames = pulseFrames;
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingLayerName = GameConstants.SORT_GIMMICK_ACTIVE;
                if (frames != null && frames.Length > 0) spriteRenderer.sprite = frames[0];
            }
        }

        void Update()
        {
            if (spriteRenderer == null || frames == null || frames.Length == 0) return;
            timer += Time.deltaTime;
            spriteRenderer.sprite = frames[(int)(timer * fps) % frames.Length];
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (collected) return;
            var body = other.attachedRigidbody;
            if (body == null || body.GetComponent<PlayerController2D>() == null) return;

            collected = true;
            if (PaletteManager.Instance != null) PaletteManager.Instance.UnlockSwitch();
            Collected?.Invoke();
            StartCoroutine(Absorb(body.transform));
        }

        IEnumerator Absorb(Transform target)
        {
            Vector3 start = transform.position;
            float elapsed = 0f;
            while (elapsed < absorbDuration)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / absorbDuration);
                transform.position = Vector3.Lerp(start, target.position + Vector3.up * 0.5f, k);
                transform.localScale = Vector3.one * (1f - k);
                yield return null;
            }
            gameObject.SetActive(false);
        }
    }
}
