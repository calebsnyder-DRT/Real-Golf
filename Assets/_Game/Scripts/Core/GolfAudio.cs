using UnityEngine;

namespace RealGolf.Core
{
    /// <summary>
    /// Every sound in the game is synthesised on first use, so the project keeps
    /// its no-binary-assets rule and still gives the swing some weight.
    /// </summary>
    public sealed class GolfAudio : MonoBehaviour
    {
        private const int SampleRate = 44100;

        private AudioSource _source;
        private AudioClip _drive;
        private AudioClip _putt;
        private AudioClip _bounce;
        private AudioClip _splash;
        private AudioClip _holed;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = 0.75f;

            BuildClips();
        }

        private void BuildClips()
        {
            // One generator drives every clip so a given build always sounds the same.
            var rng = new System.Random(20240517);

            _drive = Build("Drive", 0.3f, t =>
            {
                float env = Mathf.Exp(-t * 36f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                // Falling pitch gives the strike its crack rather than a beep.
                float tone = Mathf.Sin(2f * Mathf.PI * (1600f - 1000f * t) * t);
                return env * (noise * 0.55f + tone * 0.45f);
            });

            _putt = Build("Putt", 0.18f, t =>
            {
                float env = Mathf.Exp(-t * 68f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float tone = Mathf.Sin(2f * Mathf.PI * 520f * t);
                return env * (tone * 0.7f + noise * 0.3f);
            });

            _bounce = Build("Bounce", 0.22f, t =>
            {
                float env = Mathf.Exp(-t * 26f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float tone = Mathf.Sin(2f * Mathf.PI * 190f * t);
                return env * (tone * 0.55f + noise * 0.45f);
            });

            float lowpass = 0f;
            _splash = Build("Splash", 0.75f, t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                // A one-pole filter turns white noise into something watery.
                lowpass += (noise - lowpass) * 0.22f;
                float swell = Mathf.Min(1f, t * 55f);
                return lowpass * Mathf.Exp(-t * 5.2f) * swell;
            });

            _holed = Build("Holed", 0.6f, t =>
                (Tap(t, 0f, 900f) + Tap(t, 0.08f, 760f) + Tap(t, 0.16f, 620f)) * 0.8f);
        }

        /// <summary>A single decaying click, used to build the cup rattle.</summary>
        private static float Tap(float t, float start, float frequency)
        {
            float dt = t - start;
            return dt < 0f ? 0f : Mathf.Exp(-dt * 52f) * Mathf.Sin(2f * Mathf.PI * frequency * dt);
        }

        private static AudioClip Build(string name, float seconds, System.Func<float, float> generator)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(SampleRate * seconds));
            var data = new float[count];

            for (int i = 0; i < count; i++)
            {
                data[i] = Mathf.Clamp(generator(i / (float)SampleRate), -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void Play(AudioClip clip, float volume, float pitch)
        {
            if (_source == null || clip == null)
            {
                return;
            }

            _source.pitch = Mathf.Clamp(pitch, 0.4f, 2.2f);
            _source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// <summary>Full swing. Harder strikes are louder and a touch brighter.</summary>
        public void PlayStrike(float power, bool putter)
        {
            if (putter)
            {
                Play(_putt, 0.35f + power * 0.4f, Random.Range(0.95f, 1.05f));
                return;
            }

            Play(_drive, 0.45f + power * 0.5f, Random.Range(0.94f, 1.06f) + power * 0.08f);
        }

        public void PlayBounce(float strength) =>
            Play(_bounce, Mathf.Clamp01(strength) * 0.5f, Random.Range(0.85f, 1.15f));

        public void PlaySplash() => Play(_splash, 0.7f, Random.Range(0.95f, 1.05f));

        public void PlayHoled() => Play(_holed, 0.8f, Random.Range(0.98f, 1.02f));
    }
}
