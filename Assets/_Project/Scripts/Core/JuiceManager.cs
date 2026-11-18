using System.Collections.Generic;
using PaletteShaper.Gimmicks;
using PaletteShaper.Player;
using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// GDD 7.6 Game Juice. 실체화 팝업은 PaletteObject가 직접 처리하고, 여기서는:
    /// 먼지(점프/착지) · 잔상(패드 발사 중 속도≥10 또는 기류 안) · 앰비언트 먼지 16개 ·
    /// 히트스톱(패드) · 화면 흔들림(패드/전환/사망) · 사망 파편.
    /// 모든 색은 GameConstants의 9색 팔레트만 사용한다. 흔들림은 정수 픽셀 단위.
    /// </summary>
    public class JuiceManager : MonoBehaviour
    {
        [SerializeField] PaletteShaperAssets assets;
        [SerializeField] PlayerController2D player;
        [SerializeField] Camera targetCamera;

        const float PX = 1f / GameConstants.PIXELS_PER_UNIT;

        class Particle { public SpriteRenderer sr; public Vector2 vel; public float life, maxLife, gravity; public Color32 color; }
        class Ghost { public SpriteRenderer sr; public float life; }

        Sprite pixel;
        readonly List<Particle> particles = new List<Particle>();
        readonly List<Ghost> ghosts = new List<Ghost>();
        readonly List<SpriteRenderer> motes = new List<SpriteRenderer>();
        readonly List<Vector2> moteVel = new List<Vector2>();
        readonly Dictionary<Sprite, int> frameIndex = new Dictionary<Sprite, int>();
        Transform fxRoot;

        float ghostTimer;
        float hitstopLeft;
        float shakeLeft;
        int shakePx;
        Vector3 appliedShake;

        public int ActiveGhosts { get { int n = 0; foreach (var g in ghosts) if (g.sr.enabled) n++; return n; } }
        public float ShakeLeft => shakeLeft;

        void Awake()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "JuicePixel" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), GameConstants.PIXELS_PER_UNIT);
            fxRoot = new GameObject("Fx_Juice").transform;
            fxRoot.SetParent(transform, false);
            if (targetCamera == null) targetCamera = Camera.main;
            if (player == null) player = FindAnyObjectByType<PlayerController2D>();

            if (assets != null)
            {
                int i = 0;
                foreach (var arr in new[] { assets.playerIdle, assets.playerRun, assets.playerJump, assets.playerFall })
                    if (arr != null) foreach (var s in arr) { if (s != null && !frameIndex.ContainsKey(s)) frameIndex[s] = i; i++; }
            }
            CreateMotes();
        }

        void OnEnable()
        {
            if (player != null)
            {
                player.Jumped += OnJumped;
                player.Landed += OnLanded;
                player.Launched += OnLaunched;
                player.Died += OnDied;
            }
            if (PaletteManager.Instance != null) PaletteManager.Instance.PaletteSwitched += OnSwitched;
        }

        void Start()
        {
            // PaletteManager.Instance가 Awake 순서상 늦게 잡힐 수 있어 한 번 더 연결
            if (PaletteManager.Instance != null)
            {
                PaletteManager.Instance.PaletteSwitched -= OnSwitched;
                PaletteManager.Instance.PaletteSwitched += OnSwitched;
            }
        }

        void OnDisable()
        {
            if (player != null)
            {
                player.Jumped -= OnJumped;
                player.Landed -= OnLanded;
                player.Launched -= OnLaunched;
                player.Died -= OnDied;
            }
            if (PaletteManager.Instance != null) PaletteManager.Instance.PaletteSwitched -= OnSwitched;
            if (hitstopLeft > 0f) { Time.timeScale = 1f; hitstopLeft = 0f; }
            RemoveShake();
        }

        // ───────── 이벤트 ─────────
        void OnJumped() => SpawnDust(Random.Range(4, 6), false);
        void OnLanded(float vy) => SpawnDust(Random.Range(4, 6), true);
        void OnSwitched(PaletteState s) => Shake(GameConstants.SHAKE_SWITCH_PX, GameConstants.SHAKE_SWITCH_TIME);
        void OnLaunched(Vector2 v)
        {
            Shake(GameConstants.SHAKE_PAD_PX, GameConstants.SHAKE_PAD_TIME);
            hitstopLeft = GameConstants.HITSTOP_PAD;
            Time.timeScale = 0f;
        }
        void OnDied()
        {
            Shake(GameConstants.SHAKE_DEATH_PX, GameConstants.SHAKE_DEATH_TIME);
            var c = PlayerCenter();
            for (int i = 0; i < 8; i++)
            {
                float a = (i / 8f) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                var vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f + 0.6f) * Random.Range(4f, 7f);
                Color32 col = i % 2 == 0 ? GameConstants.CREAM_IVORY : GameConstants.WARM_GRAY;
                Spawn(c, vel, 0.4f, 30f, col, 2);
            }
        }

        // ───────── 외부 API ─────────
        public void Shake(int px, float time)
        {
            if (px >= shakePx || shakeLeft <= 0f) { shakePx = px; }
            shakeLeft = Mathf.Max(shakeLeft, time);
        }

        // ───────── 업데이트 ─────────
        void Update()
        {
            if (hitstopLeft > 0f)
            {
                hitstopLeft -= Time.unscaledDeltaTime;
                if (hitstopLeft <= 0f) { hitstopLeft = 0f; Time.timeScale = 1f; }
            }
            float dt = Time.deltaTime;
            UpdateParticles(dt);
            UpdateGhosts(dt);
            UpdateMotes(dt);
        }

        void LateUpdate()
        {
            if (targetCamera == null) return;
            RemoveShake();
            if (shakeLeft > 0f)
            {
                shakeLeft -= Time.unscaledDeltaTime;
                if (shakeLeft > 0f)
                {
                    int x = Random.Range(-shakePx, shakePx + 1);
                    int y = Random.Range(-shakePx, shakePx + 1);
                    if (x == 0 && y == 0) x = Random.value < 0.5f ? -shakePx : shakePx;
                    appliedShake = new Vector3(x * PX, y * PX, 0f);
                    targetCamera.transform.position += appliedShake;
                }
                else shakePx = 0;
            }
        }

        void RemoveShake()
        {
            if (targetCamera != null && appliedShake != Vector3.zero) targetCamera.transform.position -= appliedShake;
            appliedShake = Vector3.zero;
        }

        // ───────── 먼지 / 파편 ─────────
        void SpawnDust(int count, bool landing)
        {
            if (player == null) return;
            var feet = PlayerFeet();
            for (int i = 0; i < count; i++)
            {
                float side = (i % 2 == 0 ? -1f : 1f);
                var vel = new Vector2(side * Random.Range(1.5f, 4f), Random.Range(0.5f, landing ? 2f : 1.2f));
                Spawn(feet + new Vector2(side * Random.Range(0f, 3f) * PX, 0f), vel, Random.Range(0.14f, 0.2f), 0f,
                      GameConstants.CREAM_IVORY, Random.Range(1, 3));
            }
        }

        void Spawn(Vector2 pos, Vector2 vel, float life, float gravity, Color32 color, int sizePx)
        {
            Particle p = null;
            foreach (var q in particles) if (!q.sr.enabled) { p = q; break; }
            if (p == null)
            {
                var go = new GameObject("Fx_Particle");
                go.transform.SetParent(fxRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = pixel;
                sr.sortingLayerName = GameConstants.SORT_VFX_WAVE;
                p = new Particle { sr = sr };
                particles.Add(p);
            }
            p.sr.enabled = true;
            p.sr.transform.position = Snap(pos);
            p.sr.transform.localScale = new Vector3(sizePx, sizePx, 1f);
            p.vel = vel; p.life = life; p.maxLife = life; p.gravity = gravity; p.color = color;
            p.sr.color = (Color)color;
        }

        void UpdateParticles(float dt)
        {
            foreach (var p in particles)
            {
                if (!p.sr.enabled) continue;
                p.life -= dt;
                if (p.life <= 0f) { p.sr.enabled = false; continue; }
                p.vel.y -= p.gravity * dt;
                p.vel *= Mathf.Max(0f, 1f - 6f * dt);
                p.sr.transform.position += (Vector3)(p.vel * dt);
                Color c = p.color;
                c.a = Mathf.Clamp01(p.life / p.maxLife);
                p.sr.color = c;
            }
        }

        // ───────── 잔상 ─────────
        bool WantGhost()
        {
            if (player == null || !player.gameObject.activeInHierarchy || player.IsDead) return false;
            var vr = player.VisualRenderer;
            if (vr == null || !vr.gameObject.activeInHierarchy) return false;
            if (player.InUpdraft) return true;
            return player.IsLaunched && player.Velocity.magnitude >= GameConstants.GHOST_MIN_SPEED;
        }

        void UpdateGhosts(float dt)
        {
            foreach (var g in ghosts)
            {
                if (!g.sr.enabled) continue;
                g.life -= dt;
                if (g.life <= 0f) { g.sr.enabled = false; continue; }
                g.sr.color = new Color(1f, 1f, 1f, GameConstants.GHOST_ALPHA * (g.life / GameConstants.GHOST_LIFETIME));
            }
            if (!WantGhost()) { ghostTimer = 0f; return; }
            ghostTimer -= dt;
            if (ghostTimer > 0f) return;
            ghostTimer = GameConstants.GHOST_INTERVAL;
            SpawnGhost();
        }

        void SpawnGhost()
        {
            var src = player.VisualRenderer;
            if (src == null || src.sprite == null || assets == null) return;
            var set = GhostSet();
            if (set == null || !frameIndex.TryGetValue(src.sprite, out int idx) || idx >= set.Length) return;
            Ghost g = null;
            foreach (var q in ghosts) if (!q.sr.enabled) { g = q; break; }
            if (g == null)
            {
                var go = new GameObject("Fx_Ghost");
                go.transform.SetParent(fxRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingLayerName = GameConstants.SORT_PLAYER;
                sr.sortingOrder = -1;
                g = new Ghost { sr = sr };
                ghosts.Add(g);
            }
            g.sr.enabled = true;
            g.sr.sprite = set[idx];
            g.sr.flipX = src.flipX;
            g.sr.transform.position = src.transform.position;
            g.sr.transform.localScale = src.transform.lossyScale;
            g.life = GameConstants.GHOST_LIFETIME;
            g.sr.color = new Color(1f, 1f, 1f, GameConstants.GHOST_ALPHA);
        }

        Sprite[] GhostSet()
        {
            var s = PaletteManager.Instance != null ? PaletteManager.Instance.State : PaletteState.MONOCHROME;
            switch (s)
            {
                case PaletteState.CRIMSON: return assets.playerGhostCrimson;
                case PaletteState.AMBER: return assets.playerGhostAmber;
                default: return assets.playerGhostMono;
            }
        }

        // ───────── 앰비언트 먼지 ─────────
        void CreateMotes()
        {
            for (int i = 0; i < GameConstants.AMBIENT_MOTE_COUNT; i++)
            {
                var go = new GameObject("Fx_Mote");
                go.transform.SetParent(fxRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = pixel;
                sr.sortingLayerName = GameConstants.SORT_BG_PROPS;
                sr.sortingOrder = 50;
                motes.Add(sr);
                moteVel.Add(new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(0.15f, 0.5f)));
            }
            PlaceMotesRandomly();
        }

        void PlaceMotesRandomly()
        {
            if (targetCamera == null) return;
            var r = ViewRect();
            foreach (var m in motes)
                m.transform.position = new Vector3(Random.Range(r.xMin, r.xMax), Random.Range(r.yMin, r.yMax), 0f);
        }

        Rect ViewRect()
        {
            float h = targetCamera.orthographicSize, w = h * targetCamera.aspect;
            var c = targetCamera.transform.position - appliedShake;
            return new Rect(c.x - w, c.y - h, w * 2f, h * 2f);
        }

        Vector2[] moteExact;

        void UpdateMotes(float dt)
        {
            if (targetCamera == null) return;
            var r = ViewRect();
            if (moteExact == null || moteExact.Length != motes.Count)
            {
                moteExact = new Vector2[motes.Count];
                for (int i = 0; i < motes.Count; i++) moteExact[i] = motes[i].transform.position;
            }
            Color col = MoteColor();
            float t = Time.time;
            for (int i = 0; i < motes.Count; i++)
            {
                var p = moteExact[i] + (moteVel[i] + new Vector2(Mathf.Sin(t * 0.7f + i) * 0.15f, 0f)) * dt;
                if (p.y > r.yMax) { p.y = r.yMin; p.x = Random.Range(r.xMin, r.xMax); }
                if (p.x < r.xMin) p.x = r.xMax; else if (p.x > r.xMax) p.x = r.xMin;
                moteExact[i] = p;
                motes[i].transform.position = Snap(p);
                col.a = 0.35f + 0.35f * (0.5f + 0.5f * Mathf.Sin(t * 1.3f + i * 1.7f));
                motes[i].color = col;
            }
        }

        static Color MoteColor()
        {
            var s = PaletteManager.Instance != null ? PaletteManager.Instance.State : PaletteState.MONOCHROME;
            switch (s)
            {
                case PaletteState.CRIMSON: return GameConstants.LIGHT_CORAL;
                case PaletteState.AMBER: return GameConstants.AMBER_HIGHLIGHT;
                default: return GameConstants.WARM_GRAY;
            }
        }

        // ───────── 유틸 ─────────
        Vector2 PlayerFeet()
        {
            var col = player.GetComponent<Collider2D>();
            if (col != null) return new Vector2(col.bounds.center.x, col.bounds.min.y + PX);
            return player.transform.position;
        }

        Vector2 PlayerCenter()
        {
            var col = player.GetComponent<Collider2D>();
            return col != null ? (Vector2)col.bounds.center : (Vector2)player.transform.position;
        }

        static Vector3 Snap(Vector2 p) =>
            new Vector3(Mathf.Round(p.x * GameConstants.PIXELS_PER_UNIT) * PX, Mathf.Round(p.y * GameConstants.PIXELS_PER_UNIT) * PX, 0f);
    }
}
