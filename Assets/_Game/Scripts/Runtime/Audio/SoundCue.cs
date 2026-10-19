using System;
using UnityEngine;

namespace Puttshack.Runtime.Audio
{
    /// <summary>One named sound: its clip variations and how they are played. Pure data; the director owns playback state.</summary>
    [Serializable]
    public sealed class SoundCue
    {
        public GolfSound Id;
        public AudioBus Bus = AudioBus.SFX;
        [Tooltip("Variations; one is picked at random, never the same one twice in a row.")]
        public AudioClip[] Clips = new AudioClip[0];
        [Range(0f, 1f)] public float Volume = 1f;
        [Tooltip("Random volume spread, as a fraction either way.")]
        [Range(0f, 0.5f)] public float VolumeJitter = 0.08f;
        [Range(0.1f, 3f)] public float Pitch = 1f;
        [Tooltip("Random pitch spread, as a fraction either way.")]
        [Range(0f, 0.5f)] public float PitchJitter = 0.04f;
        [Tooltip("0 = 2D, 1 = fully positional.")]
        [Range(0f, 1f)] public float SpatialBlend = 1f;
        public float MinDistance = 3f;
        public float MaxDistance = 150f;
        [Tooltip("Seconds before the cue may trigger again (stops bounce and roll events machine-gunning).")]
        public float Cooldown = 0.03f;
        public bool Loop;
        [Tooltip("Unity voice priority: 0 is most important, 256 least.")]
        [Range(0, 256)] public int Priority = 128;

        [NonSerialized] private int _last = -1;

        public bool HasClips
        {
            get
            {
                if (Clips == null) return false;
                foreach (AudioClip c in Clips)
                {
                    if (c != null) return true;
                }
                return false;
            }
        }

        public AudioClip Pick()
        {
            if (Clips == null || Clips.Length == 0) return null;
            int valid = 0;
            foreach (AudioClip c in Clips)
            {
                if (c != null) valid++;
            }
            if (valid == 0) return null;
            int skip = valid > 1 && _last >= 0 ? 1 : 0;
            int n = UnityEngine.Random.Range(0, valid - skip);
            for (int i = 0; i < Clips.Length; i++)
            {
                if (Clips[i] == null || (skip == 1 && i == _last)) continue;
                if (n-- == 0)
                {
                    _last = i;
                    return Clips[i];
                }
            }
            return null;
        }

        public float RandomVolume(float scale) => Mathf.Clamp01(Volume * scale * (1f + UnityEngine.Random.Range(-VolumeJitter, VolumeJitter)));

        public float RandomPitch(float scale) => Pitch * scale * (1f + UnityEngine.Random.Range(-PitchJitter, PitchJitter));
    }
}
