using System;
using PaletteShaper.Gimmicks;
using PaletteShaper.Player;
using UnityEngine;

namespace PaletteShaper.Core
{
    /// <summary>
    /// GDD 7.7 절차적 효과음: 외부 음원 없이 AudioClip.Create로 8종을 Awake에서 합성한다.
    /// 점프/착지/크림슨·앰버·모노 전환/바운스/코어·룸 클리어/사망.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProceduralSoundManager : MonoBehaviour
    {
        const int Rate = 44100;
        [SerializeField] PlayerController2D player;
        [SerializeField, Range(0f, 1f)] float volume = 0.5f;

        AudioSource source;
        public AudioClip Jump, Land, SwitchCrimson, SwitchAmber, SwitchMono, Bounce, Clear, Death;
        public int PlayCount { get; private set; }
        public string LastPlayed { get; private set; }

        enum Wave { Square, Triangle, Sine }

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            if (player == null) player = FindAnyObjectByType<PlayerController2D>();

            Jump          = Sweep("Sfx_Jump", 200f, 440f, 0.08f, Wave.Square, 0.16f);
            Land          = Sweep("Sfx_Land", 120f, 60f, 0.04f, Wave.Triangle, 0.35f);
            SwitchCrimson = Chord("Sfx_SwitchCrimson", new[] { 220f, 330f }, 0.10f, Wave.Square, 0.12f);
            SwitchAmber   = Chord("Sfx_SwitchAmber", new[] { 440f, 660f }, 0.10f, Wave.Triangle, 0.22f);
            SwitchMono    = Sweep("Sfx_SwitchMono", 300f, 140f, 0.08f, Wave.Triangle, 0.28f);
            Bounce        = Sweep("Sfx_Bounce", 150f, 680f, 0.16f, Wave.Square, 0.14f);
            Clear         = Arpeggio("Sfx_Clear", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.28f, 0.14f);
            Death         = DeathClip("Sfx_Death", 0.20f);
        }

        void OnEnable()
        {
            if (player != null)
            {
                player.Jumped += OnJump;
                player.Landed += OnLand;
                player.Launched += OnLaunch;
                player.Died += OnDeath;
            }
            CrimsonCore.Collected += OnClear;
        }

        void Start()
        {
            if (PaletteManager.Instance != null) { PaletteManager.Instance.PaletteSwitched -= OnSwitch; PaletteManager.Instance.PaletteSwitched += OnSwitch; }
            if (RoomManager.Instance != null) { RoomManager.Instance.RoomCleared -= OnClear; RoomManager.Instance.RoomCleared += OnClear; }
        }

        void OnDisable()
        {
            if (player != null)
            {
                player.Jumped -= OnJump;
                player.Landed -= OnLand;
                player.Launched -= OnLaunch;
                player.Died -= OnDeath;
            }
            CrimsonCore.Collected -= OnClear;
            if (PaletteManager.Instance != null) PaletteManager.Instance.PaletteSwitched -= OnSwitch;
            if (RoomManager.Instance != null) RoomManager.Instance.RoomCleared -= OnClear;
        }

        void OnJump() => Play(Jump);
        void OnLand(float vy) => Play(Land);
        void OnLaunch(Vector2 v) => Play(Bounce);
        void OnDeath() => Play(Death);
        void OnClear() => Play(Clear);
        void OnSwitch(PaletteState s) =>
            Play(s == PaletteState.CRIMSON ? SwitchCrimson : s == PaletteState.AMBER ? SwitchAmber : SwitchMono);

        public void Play(AudioClip clip)
        {
            if (clip == null || source == null) return;
            source.PlayOneShot(clip, volume);
            PlayCount++;
            LastPlayed = clip.name;
        }

        // ───────── 합성 ─────────
        static float Osc(Wave w, double phase)
        {
            double f = phase - Math.Floor(phase);
            switch (w)
            {
                case Wave.Square: return f < 0.5 ? 1f : -1f;
                case Wave.Triangle: return (float)(f < 0.5 ? 4 * f - 1 : 3 - 4 * f);
                default: return (float)Math.Sin(f * 2 * Math.PI);
            }
        }

        // 짧은 어택(2ms) + 선형 릴리스 엔벨로프
        static float Env(int i, int n)
        {
            int attack = Mathf.Min(88, n / 4);
            if (i < attack) return i / (float)attack;
            return 1f - (i - attack) / (float)Mathf.Max(1, n - attack);
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Sweep(string name, float f0, float f1, float dur, Wave w, float amp)
        {
            int n = Mathf.RoundToInt(dur * Rate);
            var d = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)n;
                phase += Mathf.Lerp(f0, f1, k) / Rate;
                d[i] = Osc(w, phase) * amp * Env(i, n);
            }
            return Make(name, d);
        }

        static AudioClip Chord(string name, float[] freqs, float dur, Wave w, float amp)
        {
            int n = Mathf.RoundToInt(dur * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = 0f;
                foreach (var f in freqs) s += Osc(w, f * i / (double)Rate);
                d[i] = s / freqs.Length * amp * Env(i, n);
            }
            return Make(name, d);
        }

        static AudioClip Arpeggio(string name, float[] notes, float dur, float amp)
        {
            int n = Mathf.RoundToInt(dur * Rate);
            int per = n / notes.Length;
            var d = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                int ni = Mathf.Min(notes.Length - 1, i / per);
                phase += notes[ni] / Rate;
                int local = i - ni * per;
                int localN = ni == notes.Length - 1 ? n - ni * per : per;
                float env = Env(local, localN) * 0.6f + 0.4f * (1f - i / (float)n);
                d[i] = Osc(Wave.Square, phase) * amp * env;
            }
            return Make(name, d);
        }

        static AudioClip DeathClip(string name, float dur)
        {
            int n = Mathf.RoundToInt(dur * Rate);
            var d = new float[n];
            var rng = new System.Random(7);
            double phase = 0;
            float held = 0f;
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)n;
                if (i % 4 == 0) held = (float)(rng.NextDouble() * 2 - 1); // 약간 거친 노이즈
                float noise = held * Mathf.Clamp01(1f - k * 2.5f);         // 앞부분 노이즈 버스트
                phase += Mathf.Lerp(380f, 70f, k) / Rate;                   // 떨어지는 톤
                float tone = Osc(Wave.Square, phase) * 0.6f;
                d[i] = (noise * 0.5f + tone * 0.5f) * 0.22f * Env(i, n);
            }
            return Make(name, d);
        }
    }
}
