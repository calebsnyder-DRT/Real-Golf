using System.Collections.Generic;
using System.IO;
using Puttshack.Content;
using Puttshack.Runtime.Course;
using UnityEditor;
using UnityEngine;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Writes the course layout that ArtSource/Blender/scripts/pp_pebble.py builds the coastal scenery around: every hole
    /// definition, the shared ground grid the scenery's inner ring must match, the cliff/shore control points (and the
    /// exact sampled lines the terrain uses) and the cart path network.
    /// </summary>
    public static class CourseExport
    {
        public const string JsonPath = "ArtSource/Blender/pebble_course.json";

        [MenuItem("Puttshack/Export Course Layout For Blender", priority = 30)]
        public static void ExportMenu() => Debug.Log($"[Puttshack] Wrote {Export()}.");

        [MenuItem("Puttshack/Course/Write Terrain Parity Golden", priority = 31)]
        public static void ParityMenu() => Debug.Log($"[Puttshack] Wrote {WriteParityGolden()}.");

        public static string Export()
        {
            CourseData.Invalidate();
            CourseDefinition course = GrayboxCourse.Create();
            CourseTiling tiling = CourseVisualBuilder.TilingFor(course, course.Holes[0], out _);
            var cartPaths = new List<Dictionary<string, object>>();
            foreach (CartPathLine line in CourseLayout.Lines)
            {
                cartPaths.Add(new Dictionary<string, object>
                {
                    ["Id"] = line.Id,
                    ["Closed"] = line.Closed,
                    ["Samples"] = line.Samples,
                });
            }
            var payload = new Dictionary<string, object>
            {
                ["CourseId"] = course.Id,
                ["CourseVersion"] = course.Version,
                ["ContentHash"] = course.ContentHash,
                ["SeaLevelM"] = GrayboxCourse.SeaLevelM,
                ["Grid"] = new Dictionary<string, object>
                {
                    ["MinX"] = tiling.MinX,
                    ["MinZ"] = tiling.MinZ,
                    ["MaxX"] = tiling.MaxX,
                    ["MaxZ"] = tiling.MaxZ,
                    ["Nx"] = tiling.Nx,
                    ["Nz"] = tiling.Nz,
                    ["Spacing"] = tiling.Spacing,
                },
                ["CliffEdgeControl"] = GrayboxCourse.CliffEdgeControl,
                ["ShoreControl"] = GrayboxCourse.ShoreControl,
                ["CliffEdge"] = GrayboxCourse.CliffEdge,
                ["Shore"] = GrayboxCourse.Shore,
                ["CartPath"] = CourseLayout.CartPath,
                ["CartPaths"] = cartPaths,
                ["Holes"] = course.Holes,
            };
            string path = Path.Combine(Directory.GetCurrentDirectory(), JsonPath);
            File.WriteAllText(path, ContentHasher.Canonical(payload));
            return path;
        }

        public static string WriteParityGolden()
        {
            CourseData.Invalidate();
            string path = Path.Combine(Directory.GetCurrentDirectory(), TerrainParity.GoldenPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, TerrainParity.Golden(GrayboxCourse.Create(), GrayboxCourse.CourseTerrain()));
            AssetDatabase.ImportAsset(TerrainParity.GoldenPath);
            return path;
        }
    }

    /// <summary>Re-reads the course content (<see cref="CourseData"/>) whenever a file under its folder is imported.</summary>
    public sealed class CourseDataPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string[] paths in new[] { imported, deleted, moved, movedFrom })
            {
                foreach (string p in paths)
                {
                    if (!p.StartsWith(CourseData.ProjectFolder + "/")) continue;
                    CourseData.Invalidate();
                    return;
                }
            }
        }
    }
}
