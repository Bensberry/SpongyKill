using System.Collections.Generic;
using UnityEngine;

namespace Spongykill.Audio
{
    /// <summary>
    /// Placeholder sound effects synthesised at runtime so combat has audio feedback before real
    /// sound assets exist. Clips are generated once per play session and cached.
    /// </summary>
    public static class ProceduralSfx
    {
        const int SampleRate = 44100;
        const int ShotVariants = 3;

        static readonly Dictionary<(float pitch, float length, float noise, int variant), AudioClip> ShotCache =
            new Dictionary<(float, float, float, int), AudioClip>();
        static AudioClip hit;
        static AudioClip headshot;
        static AudioClip kill;
        static AudioClip dryFire;
        static AudioClip reloadStart;
        static AudioClip reloadEnd;
        static AudioClip hurt;

        // Enter Play Mode runs without a domain reload in this project, so statics must be cleared by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ShotCache.Clear();
            hit = headshot = kill = dryFire = reloadStart = reloadEnd = hurt = null;
        }

        public static AudioClip Hit => hit != null ? hit : hit = Chirp("sfx_hit", 0.05f, 1900f, 1700f, 0.35f);
        public static AudioClip Headshot => headshot != null ? headshot : headshot = Chirp("sfx_headshot", 0.16f, 1400f, 2800f, 0.45f);
        public static AudioClip Kill => kill != null ? kill : kill = Chirp("sfx_kill", 0.2f, 950f, 260f, 0.5f);
        public static AudioClip DryFire => dryFire != null ? dryFire : dryFire = Click("sfx_dry", 0.03f, 0.35f, 11);
        public static AudioClip ReloadStart => reloadStart != null ? reloadStart : reloadStart = Click("sfx_reload_start", 0.05f, 0.4f, 23);
        public static AudioClip ReloadEnd => reloadEnd != null ? reloadEnd : reloadEnd = Click("sfx_reload_end", 0.07f, 0.5f, 37);
        public static AudioClip Hurt => hurt != null ? hurt : hurt = Shot("sfx_hurt", 110f, 0.14f, 0.2f, 5);

        /// <summary>A gunshot-like "pop": a falling tone for body plus filtered noise for crack.</summary>
        public static AudioClip Shot(float pitch, float length, float noise)
        {
            int variant = Random.Range(0, ShotVariants);
            var key = (pitch, length, noise, variant);
            if (ShotCache.TryGetValue(key, out AudioClip clip) && clip != null) return clip;
            clip = Shot($"sfx_shot_{pitch:0}_{variant}", pitch, length, noise, 101 + variant * 17);
            ShotCache[key] = clip;
            return clip;
        }

        static AudioClip Shot(string name, float pitch, float length, float noise, int seed)
        {
            var random = new System.Random(seed);
            int count = Mathf.CeilToInt(length * SampleRate);
            var data = new float[count];
            float phase = 0f;
            float filtered = 0f;
            float decay = 6f / length;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float frequency = pitch * (1f + 3f * Mathf.Exp(-t * 35f));
                phase += 2f * Mathf.PI * frequency / SampleRate;
                float white = (float)random.NextDouble() * 2f - 1f;
                filtered += (white - filtered) * 0.35f;
                float body = pitch > 0f ? Mathf.Sin(phase) : 0f;
                float attack = Mathf.Clamp01(t * 800f);
                data[i] = (body * (1f - noise) + filtered * noise * 1.6f) * Mathf.Exp(-t * decay) * attack * 0.7f;
            }
            return Create(name, data);
        }

        static AudioClip Chirp(string name, float length, float startHz, float endHz, float volume)
        {
            int count = Mathf.CeilToInt(length * SampleRate);
            var data = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                phase += 2f * Mathf.PI * Mathf.Lerp(startHz, endHz, t) / SampleRate;
                float envelope = Mathf.Clamp01(t * 40f) * (1f - t) * (1f - t);
                data[i] = Mathf.Sin(phase) * envelope * volume;
            }
            return Create(name, data);
        }

        static AudioClip Click(string name, float length, float volume, int seed)
        {
            var random = new System.Random(seed);
            int count = Mathf.CeilToInt(length * SampleRate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                float white = (float)random.NextDouble() * 2f - 1f;
                data[i] = white * Mathf.Exp(-t * 12f) * volume;
            }
            return Create(name, data);
        }

        static AudioClip Create(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
