using System;
using NUnit.Framework;
using Puttshack.Runtime.Audio;
using UnityEngine;
using UnityEngine.Audio;

namespace Puttshack.Tests
{
    /// <summary>The audio layer's data: every sound has a preset and a clip convention, and the shipped clips cover them.</summary>
    public sealed class AudioBankTests
    {
        private static bool Optional(GolfSound id) => id == GolfSound.MusicMenu || id == GolfSound.MusicRound;

        [Test]
        public void EverySoundHasAPresetAndResourceKey()
        {
            foreach (GolfSound id in (GolfSound[])Enum.GetValues(typeof(GolfSound)))
            {
                SoundCue cue = GolfSoundBank.Preset(id);
                Assert.AreEqual(id, cue.Id, $"{id} preset id");
                Assert.Greater(cue.Volume, 0f, $"{id} preset volume");
                Assert.IsNotNull(GolfSoundBank.ResourceKey(id), $"{id} resource key");
            }
        }

        [Test]
        public void ShippedClipsCoverEveryRequiredSound()
        {
            GolfSoundBank bank = GolfSoundBank.LoadForRuntime();
            try
            {
                foreach (GolfSound id in (GolfSound[])Enum.GetValues(typeof(GolfSound)))
                {
                    if (Optional(id)) continue;
                    SoundCue cue = bank.Get(id);
                    Assert.IsNotNull(cue, $"{id} cue");
                    Assert.IsTrue(cue.HasClips, $"{id} has no clips under Resources/{GolfSoundBank.ClipRoot}{GolfSoundBank.ResourceKey(id)}_*");
                    foreach (AudioClip clip in cue.Clips) Assert.IsNotNull(clip, $"{id} clip reference");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bank);
            }
        }

        [Test]
        public void MixerHasAGroupPerBus()
        {
            var mixer = Resources.Load<AudioMixer>(GolfSoundBank.MixerResourcePath);
            if (mixer == null) Assert.Ignore("No GolfMixer; ducking runs in code.");
            foreach (string bus in Enum.GetNames(typeof(AudioBus)))
                Assert.IsTrue(Array.Exists(mixer.FindMatchingGroups(bus), g => g.name == bus), $"mixer group {bus}");
        }
    }
}
