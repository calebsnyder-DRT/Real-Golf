using System;
using UnityEditor;
using UnityEngine;

namespace Puttshack.EditorTools.Audio
{
    /// <summary>
    /// Import rules for the game audio under Resources/Audio/ (ArtSource/Audio/tools/build_golf_audio.py):
    /// - Long beds (surf, wind, birds, murmur, ovation) and music stream from disk as Vorbis.
    /// - Short one-shots decompress on load so the first strike, bounce or cup drop has no decode latency.
    /// - Anything played positionally (ball, gulls, shore surf, the cart) is forced to mono; 3D voices pan it anyway.
    /// Clips load in the background; <see cref="Puttshack.Runtime.Audio.GolfAudioDirector"/> preloads the one-shots.
    /// </summary>
    public sealed class GolfAudioImportRules : AssetPostprocessor
    {
        public const string AudioRoot = "Assets/_Game/Resources/Audio/";

        public override uint GetVersion() => 1;

        private void OnPreprocessAudio()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(AudioRoot, StringComparison.Ordinal)) return;
            string folder = path.Substring(AudioRoot.Length);
            int slash = folder.IndexOf('/');
            folder = slash > 0 ? folder.Substring(0, slash) : "";
            string file = System.IO.Path.GetFileNameWithoutExtension(path);

            bool stream = folder == "Music" ||
                          file.StartsWith("surf_", StringComparison.Ordinal) ||
                          file.StartsWith("wind_", StringComparison.Ordinal) ||
                          file.StartsWith("birds_", StringComparison.Ordinal) ||
                          file.StartsWith("murmur_", StringComparison.Ordinal) ||
                          file.StartsWith("ovation_", StringComparison.Ordinal);
            bool mono = folder == "Ball" ||
                        file.StartsWith("gull_", StringComparison.Ordinal) ||
                        file.StartsWith("surf_shore_", StringComparison.Ordinal) ||
                        file.StartsWith("cart_", StringComparison.Ordinal);

            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = mono;
            importer.loadInBackground = true;
            importer.ambisonic = false;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = stream ? 0.6f : 0.8f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = !stream;
            importer.defaultSampleSettings = settings;
        }
    }
}
