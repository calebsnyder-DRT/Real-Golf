using Puttshack.Content;
using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Players have no project folder, so course content (<see cref="CourseData"/>) is read from the same files as
    /// TextAssets under Resources/Course. The editor keeps reading the files directly (bakes and tests run outside play mode).
    /// </summary>
    public static class CourseDataResources
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            if (Application.isEditor) return;
            CourseData.ReadText = Read;
            CourseData.Invalidate();
        }

        private static string Read(string relativePath)
        {
            string resource = "Course/" + (relativePath.EndsWith(".json") ? relativePath.Substring(0, relativePath.Length - 5) : relativePath);
            TextAsset asset = Resources.Load<TextAsset>(resource);
            if (asset == null) throw new System.IO.FileNotFoundException($"Course content '{resource}' is missing from Resources.");
            string text = asset.text;
            Resources.UnloadAsset(asset);
            return text;
        }
    }
}
