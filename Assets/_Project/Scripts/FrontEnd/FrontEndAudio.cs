using UnityEngine;

namespace MurderMystery.FrontEnd
{
    /// <summary>
    /// Small synthesised sounds for the menus (no audio assets yet): a paper tick on hover, a wooden block on
    /// clicks, a seal stamp for choices, a temple bell for opening a room, and a low wind bed on the title.
    /// </summary>
    public class FrontEndAudio : MonoBehaviour
    {
        private const int Rate = 44100;

        private AudioSource sfx;
        private AudioSource ambience;
        private AudioClip tick, block, stamp, bell, wind;

        public static FrontEndAudio Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.loop = true;
            ambience.volume = 0.12f;

            tick = Make("tick", 0.035f, (t, r) => Noise(r) * Decay(t, 140f) * 0.35f);
            block = Make("block", 0.16f, (t, r) =>
                (Mathf.Sin(2f * Mathf.PI * 860f * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * 1430f * t) * 0.3f) * Decay(t, 38f) * 0.6f);
            stamp = Make("stamp", 0.28f, (t, r) =>
                (Mathf.Sin(2f * Mathf.PI * 72f * t) * Decay(t, 16f) + Noise(r) * Decay(t, 60f) * 0.5f) * 0.8f);
            bell = Make("bell", 3.2f, (t, r) =>
            {
                var s = Mathf.Sin(2f * Mathf.PI * 196f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 392.7f * t) +
                        0.3f * Mathf.Sin(2f * Mathf.PI * 523f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * 811f * t);
                return s * Decay(t, 1.3f) * 0.28f * Mathf.Min(1f, t * 200f);
            });
            wind = MakeWind();
        }

        public void Hover() => Play(tick, 0.5f);
        public void Click() => Play(block, 0.7f);
        public void Stamp() => Play(stamp, 0.9f);
        public void Bell() => Play(bell, 0.8f);

        public void SetAmbience(bool on)
        {
            if (on && !ambience.isPlaying)
            {
                ambience.clip = wind;
                ambience.Play();
            }
            else if (!on && ambience.isPlaying)
            {
                ambience.Stop();
            }
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip != null) sfx.PlayOneShot(clip, volume);
        }

        private static float Decay(float t, float rate) => Mathf.Exp(-t * rate);
        private static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

        private static AudioClip Make(string name, float seconds, System.Func<float, System.Random, float> f)
        {
            var n = (int)(seconds * Rate);
            var data = new float[n];
            var random = new System.Random(name.GetHashCode());
            for (var i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate, random), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Brown noise, slowly swelling, crossfaded at the loop point.</summary>
        private static AudioClip MakeWind()
        {
            const float seconds = 8f;
            var n = (int)(seconds * Rate);
            var data = new float[n];
            var random = new System.Random(7);
            var level = 0f;
            for (var i = 0; i < n; i++)
            {
                level = Mathf.Clamp(level + Noise(random) * 0.02f, -1f, 1f) * 0.998f;
                var t = i / (float)Rate;
                var swell = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * t / seconds);
                data[i] = level * swell;
            }
            var fade = Rate / 2;
            for (var i = 0; i < fade; i++)
            {
                var w = i / (float)fade;
                data[n - fade + i] = data[n - fade + i] * (1f - w) + data[i] * w;
            }
            var clip = AudioClip.Create("wind", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
