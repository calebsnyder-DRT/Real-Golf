using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Puttshack.Content;
using Puttshack.Runtime.Course;
using Puttshack.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// File-driven course bake and review for agents working while the editor is open (no batch mode, no focus needed
    /// beyond the script reload). Write <c>Temp/course-review.request</c> with one command per line:
    /// <c>export</c> (the course layout for Blender, <see cref="CourseExport"/>), <c>bake</c> (every hole prefab; the open Main scene's instances follow), <c>bake h03-garden-green h04-…</c>,
    /// or <c>capture ArtSource/Review/Greenery/after</c> (tee, green and overview of every baked hole in the open scene,
    /// with render timings and scatter triangle counts in <c>benchmark.txt</c>). The outcome lands in
    /// <c>Temp/course-review.result</c>. Same poses every run so before/after numbers compare.
    /// </summary>
    [InitializeOnLoad]
    public static class CourseReviewRequest
    {
        private const string RequestFile = "Temp/course-review.request";
        private const string ResultFile = "Temp/course-review.result";
        private const int TimedRenders = 20;
        private const int ShotWidth = 1024, ShotHeight = 576;

        static CourseReviewRequest() => EditorApplication.update += Poll;

        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestFile)) return;
            string[] lines = File.ReadAllLines(RequestFile);
            File.Delete(RequestFile);
            var log = new StringBuilder();
            try
            {
                foreach (string raw in lines)
                {
                    string[] words = raw.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 0) continue;
                    if (words[0] == "bake") log.AppendLine(Bake(words));
                    else if (words[0] == "export") log.AppendLine("wrote " + CourseExport.Export());
                    else if (words[0] == "capture" && words.Length == 2) log.AppendLine(Capture(words[1]));
                    else throw new ArgumentException($"Unknown course review command '{raw}'.");
                }
                File.WriteAllText(ResultFile, "ok\n" + log);
            }
            catch (Exception e)
            {
                File.WriteAllText(ResultFile, "error " + e + "\n" + log);
                Debug.LogException(e);
            }
        }

        private static string Bake(string[] words)
        {
            HashSet<string> ids = null;
            if (words.Length > 1)
            {
                ids = new HashSet<string>(words);
                ids.Remove("bake");
            }
            List<GameObject> prefabs = CourseBaker.BakeHoles(ids);
            List<string> problems = CourseBaker.Verify();
            return $"baked {prefabs.Count} hole(s); verify: {(problems.Count == 0 ? "0 problems" : string.Join("; ", problems))}";
        }

        private static string Capture(string outDir)
        {
            Camera cam = Camera.main;
            if (cam == null) throw new InvalidOperationException("No main camera in the open scene.");
            Directory.CreateDirectory(outDir);
            Vector3 keepPos = cam.transform.position;
            Quaternion keepRot = cam.transform.rotation;
            var report = new StringBuilder();
            CourseDefinition course = GrayboxCourse.Create();
            double total = 0;
            int views = 0;
            try
            {
                foreach (BakedHole hole in Object.FindObjectsByType<BakedHole>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
                {
                    HoleDefinition definition = course.Holes.FirstOrDefault(h => h.Id == hole.HoleId);
                    ICollisionWorld world = definition != null ? new AnalyticWorld(definition) : null;
                    Vector3 tee = hole.Tee.position, cup = hole.Cup.position;
                    Vector3 dir = cup - tee;
                    dir.y = 0f;
                    float length = dir.magnitude;
                    dir /= length;
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    Vector3 mid = (tee + cup) * 0.5f;
                    var poses = new (string name, Vector3 from, Vector3 at)[]
                    {
                        ("tee", tee - dir * 7f + Vector3.up * 2.4f, tee + dir * 140f),
                        ("green", cup - dir * 4.2f + Vector3.up * 1.15f, cup + Vector3.up * 0.55f),
                        ("overview", mid - dir * length * 0.35f + side * 60f + Vector3.up * 110f, mid + dir * length * 0.1f),
                        ("rough", mid + side * 38f + Vector3.up * 6f, mid - side * 10f + dir * 30f),
                        // Player's-eye view of the first cut and rough beside the fairway, for the grass blades.
                        ("lie", GroundAt(world, mid + side * 30f) + Vector3.up * 1.6f, GroundAt(world, mid + side * 18f + dir * 12f)),
                    };
                    report.AppendLine($"{hole.HoleId} scatter: {ScatterStats(hole.transform)}");
                    foreach (var pose in poses)
                    {
                        cam.transform.SetPositionAndRotation(pose.from, Quaternion.LookRotation(pose.at - pose.from));
                        double ms = TimeRenders(cam);
                        total += ms;
                        views++;
                        report.AppendLine($"{hole.HoleId} {pose.name}: render {ms:F1} ms/frame at 1080p");
                        Shoot(cam, Path.Combine(outDir, $"{hole.HoleId}_{pose.name}.png"));
                    }
                }
            }
            finally
            {
                cam.transform.SetPositionAndRotation(keepPos, keepRot);
            }
            report.AppendLine($"mean {total / Mathf.Max(views, 1):F1} ms over {views} views");
            File.WriteAllText(Path.Combine(outDir, "benchmark.txt"), report.ToString());
            return report.ToString();
        }

        /// <summary>Renderer and triangle totals of the hole's merged ground cover and groves.</summary>
        private static string ScatterStats(Transform hole)
        {
            var parts = new List<string>();
            foreach (string name in new[] { "Ground Cover", "Groves" })
            {
                Transform node = hole.Find(name);
                if (node == null) continue;
                long tris = 0;
                MeshFilter[] filters = node.GetComponentsInChildren<MeshFilter>(true);
                foreach (MeshFilter filter in filters)
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null) continue;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++) tris += (long)mesh.GetIndexCount(sub) / 3;
                }
                parts.Add($"{name} {filters.Length} renderers {tris / 1000}k tris");
            }
            GrassField grass = hole.GetComponentInChildren<GrassField>();
            if (grass != null) parts.Add($"grass {grass.InstanceCount / 1000}k clumps");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        private static Vector3 GroundAt(ICollisionWorld world, Vector3 p) =>
            world == null ? p : new Vector3(p.x, (float)world.GroundHeight(p.x, p.z), p.z);

        private static double TimeRenders(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);
            RenderTexture previous = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                for (int i = 0; i < 3; i++) cam.Render();
                RenderTexture.active = rt;
                probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                double t0 = EditorApplication.timeSinceStartup;
                for (int i = 0; i < TimedRenders; i++) cam.Render();
                probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                return (EditorApplication.timeSinceStartup - t0) * 1000.0 / TimedRenders;
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(probe);
            }
        }

        private static void Shoot(Camera cam, string path)
        {
            var rt = RenderTexture.GetTemporary(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            RenderTexture previous = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(tex);
            }
        }
    }
}
