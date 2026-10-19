using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;

namespace Puttshack.Tests
{
    /// <summary>
    /// TerrainModel against the shared golden (Fixtures/terrain_parity.golden.json) that
    /// ArtSource/Blender/scripts/test_terrain_parity.py checks pp_pebble_terrain.py against: the same features and points
    /// must give the same heights in both languages, and the golden must describe the current course.
    /// </summary>
    public sealed class TerrainParityTests
    {
        private const string Regenerate = "Regenerate with Puttshack > Course > Write Terrain Parity Golden, then rerun ArtSource/Blender/scripts/test_terrain_parity.py.";

        private static JObject Read(string text)
        {
            using (var reader = new JsonTextReader(new StringReader(text)) { FloatParseHandling = FloatParseHandling.Decimal })
                return (JObject)JToken.ReadFrom(reader);
        }

        [Test]
        public void Golden_DescribesTheCurrentCourse()
        {
            JObject golden = Read(File.ReadAllText(TerrainParity.GoldenPath));
            JObject current = Read(TerrainParity.Golden(GrayboxCourse.Create(), GrayboxCourse.CourseTerrain()));
            Assert.AreEqual((string)current["Course"], (string)golden["Course"], Regenerate);
            Assert.IsTrue(JToken.DeepEquals(current["Features"], golden["Features"]), "Terrain features changed. " + Regenerate);
            var a = (JArray)current["Probes"];
            var b = (JArray)golden["Probes"];
            Assert.AreEqual(a.Count, b.Count, "Probe points changed. " + Regenerate);
            for (int i = 0; i < a.Count; i++)
            {
                // Columns: x, z, height bits (the fourth, a rounded height for reading, prints per runtime).
                for (int c = 0; c < 3; c++)
                    Assert.IsTrue(JToken.DeepEquals(a[i][c], b[i][c]), $"Probe {i} differs ({b[i]} vs {a[i]}). " + Regenerate);
            }
        }

        [Test]
        public void GoldenFeatures_ReproduceTheGoldenHeights_BitForBit()
        {
            JObject golden = Read(File.ReadAllText(TerrainParity.GoldenPath));
            var terrain = TerrainParity.DecodeFeatures((JArray)golden["Features"]);
            int checkedProbes = 0;
            foreach (JArray probe in (JArray)golden["Probes"])
            {
                double x = (double)(decimal)probe[0], z = (double)(decimal)probe[1];
                string expected = (string)probe[2];
                Assert.AreEqual(expected, TerrainParity.Bits(TerrainModel.Height(terrain, x, z)), $"height at ({x}, {z})");
                checkedProbes++;
            }
            Assert.Greater(checkedProbes, 1000);
        }

        [Test]
        public void Height_MatchesTheUnprunedReference_BitForBit()
        {
            // TerrainModel skips bunkers and coast segments by their bounds; that must never change a single bit.
            List<TerrainFeature> terrain = GrayboxCourse.CourseTerrain();
            var rng = new System.Random(7);
            int differ = 0;
            for (int i = 0; i < 40000; i++)
            {
                double x = -170 + rng.NextDouble() * 470, z = -840 + rng.NextDouble() * 1120;
                if (BitConverter.DoubleToInt64Bits(TerrainModel.Height(terrain, x, z)) != BitConverter.DoubleToInt64Bits(Reference(terrain, x, z))) differ++;
            }
            foreach (TerrainFeature f in terrain)
            {
                if (f.Kind != TerrainFeatureKind.Shelf) continue;
                for (int i = 0; i + 1 < f.Path.Count; i++)
                {
                    // On, beside and exactly at the path's vertices, where segment choice and side are decided.
                    Vec2d a = f.Path[i], b = f.Path[i + 1];
                    foreach (double o in new[] { -3.0, 0.0, 0.5, 4.0 })
                    {
                        double x = a.x + (b.z - a.z) * o * 0.1, z = a.z - (b.x - a.x) * o * 0.1;
                        if (BitConverter.DoubleToInt64Bits(TerrainModel.Height(terrain, x, z)) != BitConverter.DoubleToInt64Bits(Reference(terrain, x, z))) differ++;
                    }
                }
            }
            Assert.AreEqual(0, differ);
        }

        private static double Reference(List<TerrainFeature> features, double x, double z)
        {
            double h = 0;
            foreach (TerrainFeature f in features)
            {
                switch (f.Kind)
                {
                    case TerrainFeatureKind.Plateau:
                    case TerrainFeatureKind.Tilt:
                    {
                        double dx = x - f.Center.x, dz = z - f.Center.z, r = Math.Sqrt(dx * dx + dz * dz);
                        double w = r <= f.InnerRadius ? 1 : r >= f.OuterRadius ? 0 : Smooth(1 - (r - f.InnerRadius) / (f.OuterRadius - f.InnerRadius));
                        if (w <= 0) continue;
                        if (f.Kind == TerrainFeatureKind.Plateau) h += f.Height * w;
                        else h += (f.Slope.x * dx + f.Slope.z * dz) * w;
                        break;
                    }
                    case TerrainFeatureKind.Shelf:
                    {
                        double w = Ramp(TerrainModel.SignedDistanceLeft(f.Path, x, z), f.InnerRadius, f.OuterRadius);
                        if (w > 0) h += (f.Height - h) * w;
                        break;
                    }
                    case TerrainFeatureKind.Bowl:
                        if (!Shapes.Contains(f.Path, x, z)) continue;
                        h += f.Height * Ramp(TerrainModel.DistanceToEdge(f.Path, x, z), 0, f.InnerRadius);
                        break;
                }
            }
            return h;
        }

        private static double Smooth(double s) => s * s * (3 - 2 * s);

        private static double Ramp(double d, double start, double end)
        {
            if (d <= start) return 0;
            if (d >= end) return 1;
            double t = (d - start) / (end - start);
            return t * t * (3 - 2 * t);
        }
    }
}
