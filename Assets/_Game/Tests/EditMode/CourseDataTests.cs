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
    /// The data-driven course (Assets/_Game/Resources/Course): the hand-written holes 3-5 survive the move byte for byte,
    /// the loader is strict and exact, and holes 6-8 keep their design intent.
    /// </summary>
    public sealed class CourseDataTests
    {
        private const string LegacyFrame = "Assets/_Game/Tests/EditMode/Fixtures/LegacyFrame/course.json";

        /// <summary>
        /// Hashes Unity baked from the hand-written GrayboxCourse.cs (graybox-0.3, see Assets/_Game/Course/Baked). Newtonsoft
        /// prints doubles through the runtime, so Mono and CoreCLR give different strings for the same values.
        /// </summary>
        private static readonly Dictionary<string, string> LegacyHashesMono = new Dictionary<string, string>
        {
            ["h03-garden-green"] = "68360e62edd12d50299315bd8c7df70d3f414ab5a26cabf45f081df5e95dd2ab",
            ["h04-cypress-ridge"] = "f75b77fc34f8b784725f0c8df1191660e125189f6d130a23fbf38740115013fc",
            ["h05-arrowhead-point"] = "c45caf20bb216c01e286c56107ad942027d1f87f079c93b9590b0c68fe1906a3",
        };

        private static readonly Dictionary<string, string> LegacyHashesCoreClr = new Dictionary<string, string>
        {
            ["h03-garden-green"] = "d77433cfbd5101918a35db9b2f23f57ea771da672bdb4afabf061146a6936902",
            ["h04-cypress-ridge"] = "b80c6a7395b25fdf3d7f51bf6b1ea7e1101ffeaa98f02e7e07e55a7fbbcceaa9",
            ["h05-arrowhead-point"] = "4f5b7ecaafb2379a89e084e4e8b58c1b604daf6e9be680f1ecfcee3fcc73660d",
        };

        private static CourseSpec Legacy() => CourseData.LoadFrom(CourseData.DefaultCourse, p => p == "Pebble/course.json" ? File.ReadAllText(LegacyFrame) : CourseData.ReadProjectFile(p));

        [Test]
        public void LegacyFrame_WithTheLiveHoleFiles_ReproducesTheBakedLegacyHashes()
        {
            Dictionary<string, string> expected = Type.GetType("Mono.Runtime") != null ? LegacyHashesMono : LegacyHashesCoreClr;
            CourseDefinition legacy = Legacy().Build();
            Assert.AreEqual(3, legacy.Holes.Count);
            foreach (HoleDefinition hole in legacy.Holes) Assert.AreEqual(expected[hole.Id], hole.ContentHash, hole.Id);
        }

        [Test]
        public void LegacyHoles_PlayIdentically_InTheGrownCourse()
        {
            CourseSpec legacySpec = Legacy();
            CourseDefinition legacy = legacySpec.Build();
            CourseDefinition current = GrayboxCourse.Create();
            List<TerrainFeature> legacyTerrain = legacySpec.Terrain(), terrain = GrayboxCourse.CourseTerrain();
            foreach (HoleDefinition old in legacy.Holes)
            {
                HoleDefinition now = current.Holes.Find(h => h.Id == old.Id);
                Assert.IsNotNull(now, old.Id);
                Assert.AreEqual(WithoutTerrain(old), WithoutTerrain(now), $"{old.Id}: everything but the course-wide terrain list (tee, cup and route heights included).");

                // Every point a ball can rest on, and the 30 m of ground drawn round it, keeps its exact height.
                Bounds(old.PlayableBoundary, out double x0, out double z0, out double x1, out double z1);
                int differ = 0;
                for (double x = x0 - 30; x <= x1 + 30; x += 0.8)
                {
                    for (double z = z0 - 30; z <= z1 + 30; z += 0.8)
                    {
                        if (BitConverter.DoubleToInt64Bits(TerrainModel.Height(legacyTerrain, x, z)) != BitConverter.DoubleToInt64Bits(TerrainModel.Height(terrain, x, z))) differ++;
                    }
                }
                Assert.AreEqual(0, differ, $"{old.Id}: heights changed inside the boundary or its ground margin.");
            }
        }

        [Test]
        public void LegacyCartDrives_AreUnchanged()
        {
            CourseSpec legacySpec = Legacy();
            CourseDefinition legacy = legacySpec.Build();
            CourseDefinition current = GrayboxCourse.Create();
            Assert.AreEqual(ContentHasher.Canonical(legacySpec.CartPaths.Loop.Samples), ContentHasher.Canonical(CourseLayout.CartPath));
            Assert.AreEqual(ContentHasher.Canonical(legacySpec.CartPaths.Loop.Chunks()), ContentHasher.Canonical(CourseLayout.CartPathChunks()));
            foreach (HoleDefinition a in legacy.Holes)
            {
                HoleDefinition nowA = current.Holes.Find(h => h.Id == a.Id);
                Assert.AreEqual(Canon(legacySpec.CartPaths.TeePark(a)), Canon(CourseLayout.TeePark(nowA)), a.Id);
                foreach (HoleDefinition b in legacy.Holes)
                {
                    HoleDefinition nowB = current.Holes.Find(h => h.Id == b.Id);
                    Assert.AreEqual(Canon(legacySpec.CartPaths.TransitionRoute(a, b)), Canon(CourseLayout.TransitionRoute(nowA, nowB)), $"{a.Id} -> {b.Id}");
                }
            }
        }

        [Test]
        public void Building_IsDeterministic_AndHolesAreFreshCopies()
        {
            CourseDefinition a = GrayboxCourse.Create(), b = GrayboxCourse.Create();
            Assert.AreEqual(a.ContentHash, b.ContentHash);
            a.Holes[0].Surfaces.Clear();
            Assert.IsNotEmpty(GrayboxCourse.Create().Holes[0].Surfaces);
            Assert.AreEqual(6, a.Holes.Count);
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7, 8 }, a.Holes.ConvertAll(h => h.Number));
        }

        [Test]
        public void Numbers_ConvertExactly_LikeCSharpLiterals()
        {
            CourseSpec spec = Edited("h03-garden-green", h => h["WindMps"] = JArray.Parse("[0.1, 1e-3, -0.7]"));
            Vec3d wind = spec.BuildHole("h03-garden-green").WindMps;
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(0.1), BitConverter.DoubleToInt64Bits(wind.x));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(1e-3), BitConverter.DoubleToInt64Bits(wind.y));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(-0.7), BitConverter.DoubleToInt64Bits(wind.z));
            Assert.Throws<FormatException>(() => Edited("h03-garden-green", h => h["WindMps"] = JArray.Parse("[0.12345678901234567, 0, 0]")).BuildHole("h03-garden-green"));
        }

        [Test]
        public void Loader_RejectsMistakes()
        {
            Assert.Throws<FormatException>(() => Edited("h03-garden-green", h => h["Parr"] = 3).BuildHole("h03-garden-green"), "unknown key");
            Assert.Throws<FormatException>(() => Edited("h03-garden-green", h => ((JObject)h["Surfaces"][0]["Shape"])["Kind"] = "Circle").BuildHole("h03-garden-green"), "unknown shape");
            Assert.Throws<FormatException>(() => Edited("h03-garden-green", h => h["DefaultSurface"] = "Rough").BuildHole("h03-garden-green"), "unknown surface");
            Assert.Throws<FormatException>(() => Edited("h03-garden-green", h => h["Id"] = "h03-other"), "id must match the file name");
            Assert.Throws<FormatException>(() => EditedCourse(c => ((JArray)c["Routing"])[0] = "h99-missing"), "routing names an unknown hole");
            Assert.Throws<FormatException>(() => EditedCourse(c => c["Paths"]["CliffEdge"]["Pieces"][1]["Control"][0] = JArray.Parse("[-62, -231]")), "pieces must join");
            Assert.DoesNotThrow(() => Edited("h03-garden-green", h => h["Notes"] = "Authors may leave notes anywhere.").BuildHole("h03-garden-green"));
        }

        [Test]
        public void CartPathEntry_AddsTheNearbyRibbons_InPlace()
        {
            CourseSpec spec = Edited("h03-garden-green", h => ((JArray)h["Surfaces"]).Insert(3, JObject.Parse("{\"CartPaths\": {\"Priority\": 30}}")));
            HoleDefinition hole = spec.BuildHole("h03-garden-green");
            List<SurfaceRegion> carts = hole.Surfaces.FindAll(r => r.Surface == SurfaceId.CartPath);
            Assert.IsNotEmpty(carts);
            Assert.AreEqual("fairway", hole.Surfaces[2].Id);
            Assert.AreEqual(SurfaceId.CartPath, hole.Surfaces[3].Surface);
            Assert.AreEqual("green", hole.Surfaces[3 + carts.Count].Id);
            foreach (SurfaceRegion r in carts)
            {
                Assert.AreEqual(30, r.Priority);
                StringAssert.StartsWith("cart-path-", r.Id);
            }
        }

        [Test]
        public void LegacyHoles_HaveNoCartPathSurfaces_AsBaked()
        {
            // GrayboxCourse.cs added the cart path regions before setting the playable boundary, so its bounds test kept
            // none; the data keeps that until the holes opt in with a "CartPaths" entry (and are rebaked).
            foreach (HoleDefinition hole in GrayboxCourse.Create().Holes)
                Assert.IsFalse(hole.Surfaces.Exists(r => r.Surface == SurfaceId.CartPath), hole.Id);
        }

        [Test]
        public void Hole6_ClimbsTheHeadland()
        {
            HoleDefinition hole = GrayboxCourse.Hole("h06-headland-climb");
            Assert.AreEqual(5, hole.Par);
            Assert.Greater(hole.CupM.y - hole.TeeM.y, 8.0, "The green sits well above the tee.");
            Assert.AreEqual(1, hole.Hazards.Count, "The Pacific runs down the right.");
        }

        [Test]
        public void Hole7_GreenIsOnThePoint_WithSeaOnThreeSides()
        {
            HoleDefinition hole = GrayboxCourse.Hole("h07-lands-end");
            Assert.AreEqual(3, hole.Par);
            Assert.Less(hole.YardageYd, 120);
            Assert.AreEqual(1, hole.Hazards.Count);
            List<Vec2d> sea = hole.Hazards[0].Polygon;
            Assert.IsFalse(Shapes.Contains(sea, hole.CupM.x, hole.CupM.z));
            foreach (double bearing in new[] { 180.0, 225.0, 270.0 })
            {
                // Over the cliff edge (the hazard line) within 40 m, and the ground below sea level not far beyond it.
                double a = bearing * Math.PI / 180, dx = Math.Sin(a), dz = Math.Cos(a);
                double hazardAt = double.NaN, waterAt = double.NaN;
                for (double r = 1; r <= 80 && double.IsNaN(waterAt); r += 1)
                {
                    double x = hole.CupM.x + dx * r, z = hole.CupM.z + dz * r;
                    if (double.IsNaN(hazardAt) && Shapes.Contains(sea, x, z)) hazardAt = r;
                    if (TerrainModel.Height(hole.Terrain, x, z) < GrayboxCourse.SeaLevelM) waterAt = r;
                }
                Assert.LessOrEqual(hazardAt, 40, $"Bearing {bearing}: the cliff edge should be within 40 m of the cup.");
                Assert.LessOrEqual(waterAt, 65, $"Bearing {bearing}: open water should lie just beyond the cliff.");
            }
        }

        [Test]
        public void Hole8_ApproachCarriesTheChasm()
        {
            HoleDefinition hole = GrayboxCourse.Hole("h08-the-chasm");
            Assert.AreEqual(4, hole.Par);
            Vec3d landing = hole.PlayRouteM[hole.PlayRouteM.Count - 2];
            bool water = false, dryBothEnds = TerrainModel.Height(hole.Terrain, landing.x, landing.z) > 0 && hole.CupM.y > 0;
            for (double t = 0; t <= 1; t += 0.005)
            {
                double x = landing.x + (hole.CupM.x - landing.x) * t, z = landing.z + (hole.CupM.z - landing.z) * t;
                if (TerrainModel.Height(hole.Terrain, x, z) < GrayboxCourse.SeaLevelM) water = true;
            }
            Assert.IsTrue(dryBothEnds && water, "The second shot plays from dry land over open water to the green.");
        }

        /// <summary>Canonical JSON of a freshly built hole minus the shared terrain list (clears those fields on it).</summary>
        private static string WithoutTerrain(HoleDefinition hole)
        {
            hole.Terrain = null;
            hole.ContentHash = null;
            return ContentHasher.Canonical(hole);
        }

        private static string Canon(object value) => ContentHasher.Canonical(value);

        private static void Bounds(List<Vec2d> poly, out double x0, out double z0, out double x1, out double z1)
        {
            x0 = z0 = double.PositiveInfinity;
            x1 = z1 = double.NegativeInfinity;
            foreach (Vec2d p in poly)
            {
                x0 = Math.Min(x0, p.x);
                x1 = Math.Max(x1, p.x);
                z0 = Math.Min(z0, p.z);
                z1 = Math.Max(z1, p.z);
            }
        }

        private static CourseSpec Edited(string holeId, Action<JObject> edit) => EditedFile($"Pebble/Holes/{holeId}.json", edit);

        private static CourseSpec EditedCourse(Action<JObject> edit) => EditedFile("Pebble/course.json", edit);

        /// <summary>The live course with one file edited (numbers kept as decimals so the rest stays exact).</summary>
        private static CourseSpec EditedFile(string file, Action<JObject> edit) => CourseData.LoadFrom(CourseData.DefaultCourse, p =>
        {
            string text = CourseData.ReadProjectFile(p);
            if (p != file) return text;
            JObject o;
            using (var reader = new JsonTextReader(new StringReader(text)) { FloatParseHandling = FloatParseHandling.Decimal })
                o = (JObject)JToken.ReadFrom(reader);
            edit(o);
            return o.ToString();
        });
    }
}
