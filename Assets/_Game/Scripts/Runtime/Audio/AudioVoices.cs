using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Puttshack.Runtime.Audio
{
    /// <summary>Applies a cue's routing and 3D settings to a source.</summary>
    internal static class AudioSourceSetup
    {
        public static void Apply(AudioSource src, SoundCue cue, AudioMixerGroup group)
        {
            src.outputAudioMixerGroup = group;
            src.spatialBlend = cue.SpatialBlend;
            src.minDistance = cue.MinDistance;
            src.maxDistance = Mathf.Max(cue.MaxDistance, cue.MinDistance + 0.1f);
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = 0f;
            src.spread = cue.SpatialBlend > 0.5f ? 30f : 0f;
            src.priority = cue.Priority;
            src.loop = cue.Loop;
        }

        public static AudioSource Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            return src;
        }
    }

    /// <summary>Pooled one-shot voices. Each voice's level follows its bus gain every frame so ducks reach sounds in flight.</summary>
    internal sealed class VoicePool
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public AudioBus Bus;
            public float Level;
            public Transform Follow;
            public float StartedAt;
        }

        private readonly List<Voice> _voices = new List<Voice>();
        private readonly Func<AudioBus, AudioMixerGroup> _groups;

        public VoicePool(Transform root, int size, Func<AudioBus, AudioMixerGroup> groups)
        {
            _groups = groups;
            for (int i = 0; i < size; i++) _voices.Add(new Voice { Source = AudioSourceSetup.Create(root, $"Voice {i + 1:00}") });
        }

        public AudioSource Play(SoundCue cue, AudioClip clip, Vector3 position, float level, float pitch, Transform follow, float[] busGain)
        {
            Voice v = Acquire(cue.Priority);
            if (v == null) return null;
            AudioSource src = v.Source;
            AudioSourceSetup.Apply(src, cue, _groups(cue.Bus));
            src.loop = false;
            src.transform.position = follow != null ? follow.position : position;
            src.clip = clip;
            src.pitch = pitch;
            v.Bus = cue.Bus;
            v.Level = level;
            v.Follow = follow;
            v.StartedAt = UnityEngine.Time.unscaledTime;
            src.volume = level * busGain[(int)cue.Bus];
            src.Play();
            return src;
        }

        public void Tick(float[] busGain)
        {
            foreach (Voice v in _voices)
            {
                if (!v.Source.isPlaying)
                {
                    v.Follow = null;
                    continue;
                }
                v.Source.volume = v.Level * busGain[(int)v.Bus];
                if (v.Follow != null) v.Source.transform.position = v.Follow.position;
            }
        }

        public void StopAll()
        {
            foreach (Voice v in _voices)
            {
                v.Source.Stop();
                v.Follow = null;
            }
        }

        /// <summary>A free voice, else the oldest voice no more important than <paramref name="priority"/>.</summary>
        private Voice Acquire(int priority)
        {
            Voice steal = null;
            foreach (Voice v in _voices)
            {
                if (!v.Source.isPlaying) return v;
                if (v.Source.priority < priority) continue;
                if (steal == null || v.StartedAt < steal.StartedAt) steal = v;
            }
            steal?.Source.Stop();
            return steal;
        }
    }

    /// <summary>A looping source whose level eases toward a target; optional low-pass for indoor / pause muffling.</summary>
    internal sealed class LoopVoice
    {
        public readonly AudioSource Source;
        public readonly AudioLowPassFilter LowPass;
        public SoundCue Cue { get; private set; }
        /// <summary>0-1 level the voice eases toward, before the cue volume and bus gain.</summary>
        public float Target;
        public float Pitch = 1f;
        public float FadeSeconds = 0.6f;
        /// <summary>Music: each time it fades in from silence, pick a track again and play it from the top.</summary>
        public bool FromStart;
        private float _level;

        public float Level => _level;

        public LoopVoice(Transform parent, string name, bool lowPass)
        {
            Source = AudioSourceSetup.Create(parent, name);
            if (lowPass)
            {
                LowPass = Source.gameObject.AddComponent<AudioLowPassFilter>();
                LowPass.cutoffFrequency = 22000f;
            }
        }

        public void Bind(SoundCue cue, AudioMixerGroup group)
        {
            Cue = cue;
            AudioClip clip = cue != null ? cue.Pick() : null;
            if (clip == null) return;
            AudioSourceSetup.Apply(Source, cue, group);
            Source.loop = true;
            Source.clip = clip;
            Source.volume = 0f;
        }

        public void Tick(float dt, float busGain, float cutoffHz)
        {
            if (Source.clip == null) return;
            float k = FadeSeconds <= 0f ? 1f : 1f - Mathf.Exp(-dt / FadeSeconds);
            _level += (Target - _level) * k;
            if (_level < 0.0005f && Target <= 0f) _level = 0f;
            Source.volume = Cue.Volume * _level * busGain;
            Source.pitch = Pitch;
            if (LowPass != null) LowPass.cutoffFrequency = cutoffHz;
            bool audible = _level > 0f;
            if (audible && !Source.isPlaying)
            {
                if (FromStart)
                {
                    AudioClip next = Cue.Pick();
                    if (next != null) Source.clip = next;
                    Source.time = 0f;
                }
                else
                {
                    // Start each bed somewhere different so two loops of one recording never phase.
                    Source.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, Source.clip.length - 0.1f));
                }
                Source.Play();
            }
            else if (!audible && Source.isPlaying)
            {
                Source.Stop();
            }
        }
    }
}
