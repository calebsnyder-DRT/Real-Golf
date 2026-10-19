using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Puttshack.Runtime.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Puttshack.EditorTools.Audio
{
    /// <summary>
    /// Authoring for the audio layer. The game runs without any of this (the director falls back to presets and clips
    /// found by filename), so the bank asset is only needed to tune levels, swap clips or route through the mixer.
    /// </summary>
    public static class GolfAudioMenu
    {
        public const string BankPath = "Assets/_Game/Resources/Audio/GolfSoundBank.asset";
        public const string MixerPath = "Assets/_Game/Resources/Audio/GolfMixer.mixer";

        /// <summary>Creates the bank (or adds any cue a newer GolfSound introduced), fills clip-less cues by filename and assigns the mixer.</summary>
        [MenuItem("Puttshack/Audio/Create or Update Sound Bank", priority = 90)]
        public static void CreateOrUpdateBank()
        {
            var bank = AssetDatabase.LoadAssetAtPath<GolfSoundBank>(BankPath);
            bool created = bank == null;
            if (created) bank = ScriptableObject.CreateInstance<GolfSoundBank>();
            List<GolfSound> silent = bank.EnsureCues(fillEmptyFromResources: true);
            bank.Cues.Sort((a, b) => ((int)a.Id).CompareTo((int)b.Id));
            if (bank.Mixer == null) bank.Mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (created) AssetDatabase.CreateAsset(bank, BankPath);
            else EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GolfAudio] {(created ? "Created" : "Updated")} {BankPath}: {bank.Cues.Count} cues" +
                      (silent.Count > 0 ? $", silent: {string.Join(", ", silent)}" : "") +
                      (bank.Mixer == null ? " (no mixer)" : ""));
            Selection.activeObject = bank;
        }

        /// <summary>Reports silent cues, missing mixer groups and exposed parameters; returns the problem count.</summary>
        [MenuItem("Puttshack/Audio/Validate Audio Setup", priority = 91)]
        public static int Validate()
        {
            var report = new StringBuilder();
            int problems = 0;
            var asset = AssetDatabase.LoadAssetAtPath<GolfSoundBank>(BankPath);
            GolfSoundBank bank = asset != null ? UnityEngine.Object.Instantiate(asset) : ScriptableObject.CreateInstance<GolfSoundBank>();
            report.AppendLine(asset != null ? $"Bank: {BankPath}" : "Bank: none (runtime uses presets + filename clips)");

            foreach (GolfSound id in bank.EnsureCues(fillEmptyFromResources: true))
            {
                // Music is an optional hook; everything else should make a sound.
                if (id == GolfSound.MusicMenu || id == GolfSound.MusicRound)
                {
                    report.AppendLine($"  info: {id} has no clips (optional)");
                    continue;
                }
                report.AppendLine($"  silent: {id} (Resources/{GolfSoundBank.ClipRoot}{GolfSoundBank.ResourceKey(id)}_*)");
                problems++;
            }
            foreach (SoundCue cue in bank.Cues)
            {
                if (cue?.Clips == null) continue;
                foreach (AudioClip clip in cue.Clips)
                {
                    if (clip == null)
                    {
                        report.AppendLine($"  missing clip reference in {cue.Id}");
                        problems++;
                    }
                }
            }

            AudioMixer mixer = bank.Mixer != null ? bank.Mixer : AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null)
            {
                report.AppendLine($"Mixer: none at {MixerPath} (ducking still runs in code)");
            }
            else
            {
                report.AppendLine($"Mixer: {AssetDatabase.GetAssetPath(mixer)}");
                foreach (string group in new[] { "Master" }.Concat(Enum.GetNames(typeof(AudioBus))))
                {
                    bool found = Array.Exists(mixer.FindMatchingGroups(group), g => g.name == group);
                    if (found) continue;
                    report.AppendLine($"  missing group: {group}");
                    problems++;
                }
                foreach (string bus in new[] { "Master" }.Concat(Enum.GetNames(typeof(AudioBus))))
                {
                    if (mixer.GetFloat(bus + "Volume", out _)) continue;
                    report.AppendLine($"  missing exposed parameter: {bus}Volume (user volume falls back to code)");
                }
            }
            UnityEngine.Object.DestroyImmediate(bank);

            string text = $"[GolfAudio] Validate: {problems} problem(s)\n{report}";
            if (problems > 0) Debug.LogWarning(text);
            else Debug.Log(text);
            return problems;
        }
    }
}
