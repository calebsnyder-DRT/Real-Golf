using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Puttshack.Content;
using Puttshack.Runtime.Clubs;
using Puttshack.Runtime.Shop;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Brings the club models from ArtSource/Clubs (built by ArtSource/Blender/scripts/pp_clubs.py) into the game:
    /// textures, URP colourway materials, one prefab per model with its reference points, the <see cref="ClubLibrary"/>
    /// of sets, the Pro Shop club icons and review shots. Each step is its own menu item and stays short, so the editor
    /// never blocks for long. Club conventions are documented in ArtSource/Clubs/README.md.
    /// </summary>
    public static class ClubArtBaker
    {
        public const string ArtRoot = "Assets/_Game/Art/Clubs";
        public const string ModelDir = ArtRoot + "/Models";
        public const string TextureDir = ArtRoot + "/Textures";
        public const string MaterialDir = ArtRoot + "/Materials";
        public const string PaletteDir = ArtRoot + "/Palettes";
        public const string PrefabDir = ArtRoot + "/Prefabs";
        public const string LibraryPath = "Assets/_Game/Resources/" + ClubLibrary.ResourcePath + ".asset";
        public const string SourceDir = "ArtSource/Clubs/Exports";
        public const string SourceTextureDir = "ArtSource/Clubs/Textures";
        public const string ManifestPath = SourceDir + "/clubs_manifest.json";
        public const string ReviewDir = "ArtSource/Review/Clubs";

        [Serializable]
        private sealed class Manifest
        {
            public List<Entry> clubs = new List<Entry>();
        }

        [Serializable]
        private sealed class Entry
        {
            public string id;
            public string kind;
            public string label;
            public int tris;
            public float length_m;
            public float lie_deg;
            public float loft_deg;
            public string shaft;
            public float[] grip;
            public float[] grip_trail;
            public float[] butt;
            public float[] hosel;
            public float[] face;
            public float[] face_normal;
            public float[] sole;
        }

        private struct Paint
        {
            public Color Color;
            public float Metallic;
            public float Smoothness;
            public string Texture;
            public float NormalScale;

            public Paint(string hex, float metallic, float smoothness, string texture = null, float normalScale = 1f)
            {
                ColorUtility.TryParseHtmlString(hex, out Color);
                Metallic = metallic;
                Smoothness = smoothness;
                Texture = texture;
                NormalScale = normalScale;
            }
        }

        private sealed class Colorway
        {
            public string Id;
            public string Name;
            public Paint Head, FaceIron, FaceWood, FacePutter, Accent, Crown, ShaftSteel, ShaftGraphite, Grip, Trim, Decal;
        }

        /// <summary>Colourways; brand cyan #00CBE7, ink #192020, cream #F2EDDF.</summary>
        private static readonly Colorway[] Colorways =
        {
            new Colorway
            {
                Id = "cyan", Name = "Puttshack Cyan",
                Head = new Paint("#C4C9CE", 1f, 0.72f), FaceIron = new Paint("#959A9F", 1f, 0.42f, "Club_IronFace"),
                FaceWood = new Paint("#D9DCDF", 1f, 0.6f, "Club_WoodFace"), FacePutter = new Paint("#B8BDC2", 1f, 0.5f, "Club_PutterFace"),
                Accent = new Paint("#00CBE7", 0f, 0.62f), Crown = new Paint("#192020", 0.2f, 0.88f, "Club_Carbon", 0.6f),
                ShaftSteel = new Paint("#D8DBDE", 1f, 0.9f), ShaftGraphite = new Paint("#192020", 0.1f, 0.78f, "Club_Carbon", 0.4f),
                Grip = new Paint("#CFCBC2", 0f, 0.28f, "Club_Grip"), Trim = new Paint("#00CBE7", 0f, 0.6f), Decal = new Paint("#192020", 0f, 0.5f),
            },
            new Colorway
            {
                Id = "midnight", Name = "Midnight",
                Head = new Paint("#2A2B2E", 1f, 0.62f), FaceIron = new Paint("#55575B", 1f, 0.48f, "Club_IronFace"),
                FaceWood = new Paint("#6A6C70", 1f, 0.55f, "Club_WoodFace"), FacePutter = new Paint("#4A4C50", 1f, 0.45f, "Club_PutterFace"),
                Accent = new Paint("#D9B021", 1f, 0.72f), Crown = new Paint("#0B0C0D", 0.2f, 0.5f, "Club_Carbon", 0.8f),
                ShaftSteel = new Paint("#3A3C40", 1f, 0.85f), ShaftGraphite = new Paint("#0E0F10", 0.1f, 0.62f, "Club_Carbon", 0.5f),
                Grip = new Paint("#141516", 0f, 0.25f, "Club_Grip"), Trim = new Paint("#D9B021", 1f, 0.7f), Decal = new Paint("#D9B021", 1f, 0.68f),
            },
            new Colorway
            {
                Id = "tour", Name = "Tour Chrome",
                Head = new Paint("#D2D4D8", 1f, 0.9f), FaceIron = new Paint("#8F9397", 1f, 0.45f, "Club_IronFace"),
                FaceWood = new Paint("#C7CACE", 1f, 0.62f, "Club_WoodFace"), FacePutter = new Paint("#B0B3B7", 1f, 0.55f, "Club_PutterFace"),
                Accent = new Paint("#08090A", 0f, 0.6f), Crown = new Paint("#17191B", 0.2f, 0.85f, "Club_Carbon", 0.7f),
                ShaftSteel = new Paint("#DADDE0", 1f, 0.88f), ShaftGraphite = new Paint("#101113", 0.1f, 0.75f, "Club_Carbon", 0.5f),
                Grip = new Paint("#121314", 0f, 0.25f, "Club_Grip"), Trim = new Paint("#08090A", 0f, 0.5f), Decal = new Paint("#EDEDED", 0f, 0.5f),
            },
        };

        /// <summary>Sets by roster colour, as (club id, model id) per gameplay club. Index = colour (wraps).</summary>
        private static readonly (string Id, string Name, string Palette, string[] Models)[] SetDefs =
        {
            ("cyan", "Puttshack Cyan", "cyan", new[] { "driver", "wood3", "hybrid", "iron7_cavity", "iron9_cavity", "wedge_pw", "wedge_sw", "wedge_lw", "putter_fang" }),
            ("midnight", "Midnight", "midnight", new[] { "driver", "wood3", "iron5_cavity", "iron7_cavity", "iron9_cavity", "wedge_pw", "wedge_sw", "wedge_lw", "putter_mallet" }),
            ("tour", "Tour Chrome", "tour", new[] { "driver", "wood3", "iron5_blade", "iron7_blade", "iron9_blade", "wedge_pw", "wedge_sw", "wedge_lw", "putter_blade" }),
        };

        private static readonly string[] ClubIds = { "driver", "wood3", "iron5", "iron7", "iron9", "pw", "sw", "lw", ClubCatalog.PutterId };

        // ---------------------------------------------------------------- Steps

        [MenuItem("Puttshack/Clubs/Bake Clubs (all steps)")]
        public static void BakeAll()
        {
            CopyTextures();
            CopyModels(null);
            BuildPalettes();
            BuildPrefabs(null);
            BuildLibrary();
            BakeIcons();
        }

        [MenuItem("Puttshack/Clubs/1 Copy Textures")]
        public static void CopyTextures()
        {
            EnsureFolder(TextureDir);
            foreach (string src in Directory.GetFiles(SourceTextureDir, "Club_*.png")) CopyIn(src, TextureDir);
        }

        /// <summary>Copies and imports club FBXs; <paramref name="ids"/> limits the batch (null: all).</summary>
        public static void CopyModels(string[] ids)
        {
            EnsureFolder(ModelDir);
            foreach (Entry e in Load().clubs)
            {
                if (ids == null || ids.Contains(e.id)) CopyIn($"{SourceDir}/Club_{e.id}.fbx", ModelDir);
            }
        }

        [MenuItem("Puttshack/Clubs/2 Copy Models")]
        private static void CopyAllModels() => CopyModels(null);

        [MenuItem("Puttshack/Clubs/3 Build Palettes")]
        public static void BuildPalettes()
        {
            EnsureFolder(PaletteDir);
            foreach (Colorway c in Colorways)
            {
                string dir = $"{MaterialDir}/{c.Id}";
                EnsureFolder(dir);
                string path = $"{PaletteDir}/ClubPalette_{c.Id}.asset";
                var palette = AssetDatabase.LoadAssetAtPath<ClubPalette>(path);
                if (palette == null)
                {
                    palette = ScriptableObject.CreateInstance<ClubPalette>();
                    AssetDatabase.CreateAsset(palette, path);
                }
                palette.Id = c.Id;
                palette.DisplayName = c.Name;
                palette.Head = Mat(dir, c.Id, "Head", c.Head);
                palette.FaceIron = Mat(dir, c.Id, "FaceIron", c.FaceIron);
                palette.FaceWood = Mat(dir, c.Id, "FaceWood", c.FaceWood);
                palette.FacePutter = Mat(dir, c.Id, "FacePutter", c.FacePutter);
                palette.Accent = Mat(dir, c.Id, "Accent", c.Accent);
                palette.Crown = Mat(dir, c.Id, "Crown", c.Crown);
                palette.ShaftSteel = Mat(dir, c.Id, "ShaftSteel", c.ShaftSteel);
                palette.ShaftGraphite = Mat(dir, c.Id, "ShaftGraphite", c.ShaftGraphite);
                palette.Grip = Mat(dir, c.Id, "Grip", c.Grip);
                palette.Trim = Mat(dir, c.Id, "Trim", c.Trim);
                palette.Decal = Mat(dir, c.Id, "Decal", c.Decal);
                EditorUtility.SetDirty(palette);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>One prefab per model: the FBX under "Model", painted Tour Chrome, plus the reference points.</summary>
        public static List<string> BuildPrefabs(string[] ids)
        {
            EnsureFolder(PrefabDir);
            var preview = AssetDatabase.LoadAssetAtPath<ClubPalette>($"{PaletteDir}/ClubPalette_tour.asset");
            var report = new List<string>();
            foreach (Entry e in Load().clubs)
            {
                if (ids != null && !ids.Contains(e.id)) continue;
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/Club_{e.id}.fbx");
                if (fbx == null)
                {
                    report.Add($"{e.id}: model not imported");
                    continue;
                }
                var root = new GameObject("Club_" + e.id);
                try
                {
                    var club = root.AddComponent<ClubModel>();
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                    model.name = "Model";
                    model.transform.SetParent(root.transform, false);
                    Renderer body = model.GetComponentInChildren<MeshRenderer>();
                    club.ModelId = e.id;
                    club.Kind = e.kind;
                    club.Label = e.label;
                    club.FaceStyle = e.kind == "Putter" ? ClubFaceStyle.Putter : e.kind == "Driver" || e.id == "hybrid" ? ClubFaceStyle.Wood : ClubFaceStyle.Iron;
                    club.Graphite = e.shaft == "graphite";
                    club.LengthMeters = e.length_m;
                    club.LieDegrees = e.lie_deg;
                    club.LoftDegrees = e.loft_deg;
                    club.Body = body;
                    club.Roles = body != null ? body.sharedMaterials.Select(RoleOf).ToArray() : new string[0];
                    club.Grip = Point(root, ClubModel.GripName, e.grip);
                    club.TrailGrip = Point(root, ClubModel.TrailGripName, e.grip_trail);
                    club.Butt = Point(root, ClubModel.ButtName, e.butt);
                    club.Hosel = Point(root, ClubModel.HoselName, e.hosel);
                    club.Face = Point(root, ClubModel.FaceName, e.face);
                    Vector3 n = V(e.face_normal);
                    if (n.sqrMagnitude > 0.5f) club.Face.localRotation = Quaternion.LookRotation(n, Vector3.down);
                    club.Sole = Point(root, ClubModel.SoleName, e.sole);
                    club.Paint(preview);
                    if (body != null) body.shadowCastingMode = ShadowCastingMode.On;
                    PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Club_{e.id}.prefab");
                    Bounds b = body != null ? SeatBounds(root.transform, body) : default;
                    report.Add($"{e.id}: roles [{string.Join(",", club.Roles)}] seat bounds min {b.min:F3} max {b.max:F3}");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            return report;
        }

        [MenuItem("Puttshack/Clubs/4 Build Prefabs")]
        private static void BuildAllPrefabs()
        {
            foreach (string line in BuildPrefabs(null)) Debug.Log("[Puttshack] Club " + line);
        }

        [MenuItem("Puttshack/Clubs/5 Build Library")]
        public static void BuildLibrary()
        {
            EnsureFolder(Path.GetDirectoryName(LibraryPath).Replace('\\', '/'));
            var library = AssetDatabase.LoadAssetAtPath<ClubLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ClubLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.Models = Load().clubs.Select(e => AssetDatabase.LoadAssetAtPath<ClubModel>($"{PrefabDir}/Club_{e.id}.prefab")).Where(m => m != null).ToList();
            library.Palettes = Colorways.Select(c => AssetDatabase.LoadAssetAtPath<ClubPalette>($"{PaletteDir}/ClubPalette_{c.Id}.asset")).Where(p => p != null).ToList();
            library.Sets = new List<ClubLibrary.ClubSet>();
            foreach (var def in SetDefs)
            {
                var set = new ClubLibrary.ClubSet { Id = def.Id, DisplayName = def.Name, Palette = library.Palettes.FirstOrDefault(p => p.Id == def.Palette) };
                for (int i = 0; i < ClubIds.Length; i++)
                {
                    ClubModel model = library.Models.FirstOrDefault(m => m.ModelId == def.Models[i]);
                    if (model != null) set.Clubs.Add(new ClubLibrary.Slot { ClubId = ClubIds[i], Model = model });
                }
                library.Sets.Add(set);
            }
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Re-renders the Pro Shop club.&lt;id&gt; icons from colour 0's set (same files, so sprite references hold).</summary>
        [MenuItem("Puttshack/Clubs/6 Bake Icons")]
        public static void BakeIcons()
        {
            var paths = new List<string>();
            foreach (string id in ClubIds)
            {
                GameObject subject = IconSubject(id);
                if (subject == null) continue;
                var shot = new ProShopIconRenderer.Shot { ViewDirection = new Vector3(0.15f, -0.2f, -1f), Margin = 0.06f, FocusHeight = 0.17f };
                Texture2D tex = ProShopIconRenderer.Render(subject, 256, shot);
                string path = $"{ProShopBaker.IconDir}/club.{id}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                paths.Add(path);
            }
            foreach (string path in paths) AssetDatabase.ImportAsset(path);
        }

        /// <summary>The club as the shop icons show it: head down, face to camera, shaft leaning; null without a library model.</summary>
        public static GameObject IconSubject(string clubId)
        {
            var library = AssetDatabase.LoadAssetAtPath<ClubLibrary>(LibraryPath);
            if (library == null) return null;
            var holder = new GameObject("Club " + clubId);
            ClubModel club = library.Spawn(clubId, library.SetFor(0), holder.transform);
            if (club == null)
            {
                Object.DestroyImmediate(holder);
                return null;
            }
            club.transform.localRotation = Quaternion.Euler(0f, 0f, -30f) * Quaternion.Euler(0f, 0f, 180f);
            return holder;
        }

        // ---------------------------------------------------------------- Review shots

        /// <summary>Every model side by side, standing on its sole, faces to camera, in each set's colourway row.</summary>
        [MenuItem("Puttshack/Clubs/Review/Lineup")]
        public static void ShootLineup()
        {
            Directory.CreateDirectory(ReviewDir);
            var library = AssetDatabase.LoadAssetAtPath<ClubLibrary>(LibraryPath);
            var models = library.Models;
            using (var studio = new Studio())
            {
                for (int i = 0; i < models.Count; i++)
                {
                    ClubModel club = studio.Add(models[i], library.Palettes.First(p => p.Id == "tour"));
                    club.transform.position = new Vector3(-(i - (models.Count - 1) * 0.5f) * 0.2f, 0f, 0f);
                    club.transform.rotation = club.AddressRotation;
                    Ground(club);
                }
                studio.Shoot(new Vector3(0.28f, 0.8f, 5.4f), new Vector3(0.28f, 0.42f, 0f), 25f, 2400, 1000, $"{ReviewDir}/unity_lineup.png");
            }
            using (var studio = new Studio())
            {
                for (int s = 0; s < library.Sets.Count; s++)
                {
                    ClubLibrary.ClubSet set = library.Sets[s];
                    for (int i = 0; i < set.Clubs.Count; i++)
                    {
                        ClubModel club = studio.Add(set.Clubs[i].Model, set.Palette);
                        club.transform.position = new Vector3(-(i - (set.Clubs.Count - 1) * 0.5f) * 0.24f, 0f, -s * 0.7f);
                        club.transform.rotation = club.AddressRotation;
                        Ground(club);
                    }
                }
                studio.Shoot(new Vector3(0.32f, 1.75f, 4.3f), new Vector3(0.32f, 0.32f, -0.7f), 32f, 2000, 1200, $"{ReviewDir}/unity_sets.png");
            }
        }

        /// <summary>Head close-ups: each model's head from the face, the back and the golfer's eye at address; three models per row.</summary>
        [MenuItem("Puttshack/Clubs/Review/Heads")]
        public static void ShootHeads()
        {
            Directory.CreateDirectory(ReviewDir);
            var library = AssetDatabase.LoadAssetAtPath<ClubLibrary>(LibraryPath);
            ClubPalette[] palettes = library.Palettes.ToArray();
            for (int p = 0; p < palettes.Length; p++)
            {
                string path = $"{ReviewDir}/unity_heads_{palettes[p].Id}.png";
                using (var studio = new Studio())
                {
                    var tiles = new List<Texture2D>();
                    foreach (ClubModel model in library.Models)
                    {
                        ClubModel club = studio.Add(model, palettes[p]);
                        club.transform.rotation = club.AddressRotation;
                        Ground(club);
                        Vector3 head = club.Sole != null ? club.Sole.position : Vector3.zero;
                        Vector3 at = head + Vector3.up * 0.03f;
                        float d = model.Kind == "Driver" ? 0.62f : 0.45f;
                        tiles.Add(studio.Render(at + new Vector3(0.12f, 0.08f, 1f).normalized * d, at, 22f, 360, 300));
                        tiles.Add(studio.Render(at + new Vector3(0.25f, 0.2f, -1f).normalized * d, at, 22f, 360, 300));
                        tiles.Add(studio.Render(at + new Vector3(-0.35f, 1f, 0.1f).normalized * d, at, 22f, 360, 300));
                        club.gameObject.SetActive(false);
                    }
                    WriteSheet(tiles, 3 * 3, path);
                }
            }
        }

        /// <summary>
        /// The course golfer in the open Main scene at a ball on <paramref name="ball"/>, aimed along <paramref name="aim"/>,
        /// holding <paramref name="clubId"/> from <paramref name="colorIndex"/>'s club set, dressed in that colour's defaults:
        /// at address, or at the measured impact frame with <paramref name="impact"/>. Every change is undone afterwards
        /// (the scene is never saved).
        /// </summary>
        public static string ShootInHands(string clubId, int colorIndex, Vector3 ball, Vector3 aim, bool impact, string path)
        {
            var golfer = Object.FindObjectsByType<CourseGolfer>(FindObjectsInactive.Include).FirstOrDefault();
            if (golfer == null || golfer.Appearance == null) return "No course golfer in the open scenes.";
            var library = AssetDatabase.LoadAssetAtPath<ClubLibrary>(LibraryPath);
            GolferAppearance appearance = golfer.Appearance;
            GolferMotion motion = appearance.GetComponent<GolferMotion>();
            string model = GolferMotion.ClubModel(clubId);
            GolferMotion.ClubMotion clubMotion = motion != null ? motion.Clubs.FirstOrDefault(m => m.Model == model) : default;
            if (clubMotion.Address == null) return "No address state for " + model;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RegisterFullObjectHierarchyUndo(golfer.gameObject, "Club review shot");
            if (golfer.Bag != null) Undo.RegisterFullObjectHierarchyUndo(golfer.Bag.gameObject, "Club review shot");
            ClubModel club = null;
            GameObject ballGo = null;
            try
            {
                if (!golfer.gameObject.activeSelf) golfer.gameObject.SetActive(true);
                if (golfer.Bag != null) golfer.Bag.gameObject.SetActive(false);
                appearance.Apply(CosmeticCatalog.DefaultFor(colorIndex), colorIndex);
                appearance.Hold(model);
                Vector3 fwd = new Vector3(aim.x, 0f, aim.z).normalized;
                Vector3 left = Vector3.Cross(fwd, Vector3.up);
                Quaternion facing = Quaternion.LookRotation(-left, Vector3.up);
                Vector3 offset = motion.BallOffset(clubId, out Vector3 measured) ? new Vector3(measured.x, 0f, measured.z) : left * -golfer.AddressOffset;
                golfer.transform.SetPositionAndRotation(ball - facing * offset, facing);
                Animator anim = appearance.ShownAnimator;
                if (impact) GolferCharacterBaker.PoseHeld(anim, clubMotion.Swing, clubMotion.ContactOf(colorIndex), model);
                else GolferCharacterBaker.PoseHeld(anim, clubMotion.Address, 0f, model);
                var skin = appearance.ShownPart(GolferAppearance.ClubPrefix + model) as SkinnedMeshRenderer;
                Transform seat = skin != null ? skin.bones.FirstOrDefault(b => b != null && b.name.StartsWith("ClubGrip_", StringComparison.Ordinal)) : null;
                if (seat == null) return "No club seat for " + model;
                club = library.Spawn(clubId, library.SetFor(colorIndex), seat);
                skin.enabled = false;

                ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ballGo.name = "Review Ball";
                Object.DestroyImmediate(ballGo.GetComponent<Collider>());
                ballGo.transform.localScale = Vector3.one * 0.0427f;
                ballGo.transform.position = ball + Vector3.up * 0.0214f;
                var ballMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                ballMat.SetFloat("_Smoothness", 0.7f);
                ballGo.GetComponent<MeshRenderer>().sharedMaterial = ballMat;

                Transform root = appearance.transform;
                Vector3 mid = root.position + root.up * 0.82f;
                Vector3 head = club.Face != null ? club.Face.position : ball;
                using (new Frozen(anim.gameObject))
                {
                    Vector3 body = mid - root.up * 0.1f;
                    GolferReviewShots.Shoot(body + (root.forward + root.up * 0.12f).normalized * 4.1f, body, 34f, 900, 1100, path);
                    Vector3 low = Vector3.Lerp(head, mid, 0.18f);
                    Vector3 dir = (root.forward * 0.85f + fwd * 0.45f + Vector3.up * 0.3f).normalized;
                    GolferReviewShots.Shoot(low + dir * 1.15f, low, 34f, 900, 900, path.Replace(".png", "_close.png"));
                }
                Object.DestroyImmediate(ballMat);
                return $"Shot {path}; seat {seat.name}, face {head - ball:F3} from the ball";
            }
            finally
            {
                if (ballGo != null) Object.DestroyImmediate(ballGo);
                if (club != null) Object.DestroyImmediate(club.gameObject);
                Undo.RevertAllDownToGroup(group);
            }
        }

        // ---------------------------------------------------------------- Helpers

        private static Manifest Load() => JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));

        private static void CopyIn(string src, string dir)
        {
            string dst = $"{dir}/{Path.GetFileName(src)}";
            File.Copy(src, dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
        }

        private static string RoleOf(Material m)
        {
            if (m == null) return "Head";
            string name = m.name;
            int dot = name.IndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        private static Transform Point(GameObject root, string name, float[] p)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root.transform, false);
            t.localPosition = V(p);
            return t;
        }

        private static Vector3 V(float[] p) => p != null && p.Length >= 3 ? new Vector3(p[0], p[1], p[2]) : Vector3.zero;

        private static Bounds SeatBounds(Transform root, Renderer body)
        {
            Mesh mesh = body.GetComponent<MeshFilter>().sharedMesh;
            var b = new Bounds(root.InverseTransformPoint(body.transform.TransformPoint(mesh.vertices[0])), Vector3.zero);
            foreach (Vector3 v in mesh.vertices) b.Encapsulate(root.InverseTransformPoint(body.transform.TransformPoint(v)));
            return b;
        }

        private static Material Mat(string dir, string palette, string role, Paint paint)
        {
            string path = $"{dir}/Club_{palette}_{role}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", paint.Color);
            mat.SetFloat("_Metallic", paint.Metallic);
            mat.SetFloat("_Smoothness", paint.Smoothness);
            Texture2D albedo = paint.Texture != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{paint.Texture}_Albedo.png") : null;
            Texture2D normal = paint.Texture != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{paint.Texture}_Normal.png") : null;
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", paint.NormalScale);
            if (normal != null) mat.EnableKeyword("_NORMALMAP");
            else mat.DisableKeyword("_NORMALMAP");
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void Ground(ClubModel club)
        {
            if (club.Body == null) return;
            club.transform.position += Vector3.up * (0.002f - club.Body.bounds.min.y);
        }

        private static void WriteSheet(List<Texture2D> tiles, int columns, string path)
        {
            int w = tiles[0].width, h = tiles[0].height;
            int rows = (tiles.Count + columns - 1) / columns;
            var sheet = new Texture2D(w * columns, h * rows, TextureFormat.RGBA32, false);
            for (int i = 0; i < tiles.Count; i++)
            {
                int x = i % columns, y = rows - 1 - i / columns;
                sheet.SetPixels(x * w, y * h, w, h, tiles[i].GetPixels());
                Object.DestroyImmediate(tiles[i]);
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>An isolated preview scene with a ground disc, three lights and a reflection probe, for review shots.</summary>
        private sealed class Studio : IDisposable
        {
            private readonly Scene _scene;
            private readonly List<Object> _made = new List<Object>();

            public Studio()
            {
                _scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                AddLight("Key", Quaternion.Euler(38f, 150f, 0f), new Color(1f, 0.96f, 0.9f), 1.25f, true);
                AddLight("Fill", Quaternion.Euler(14f, 230f, 0f), new Color(0.82f, 0.9f, 1f), 0.6f, false);
                AddLight("Rim", Quaternion.Euler(24f, -24f, 0f), Color.white, 0.9f, false);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                ground.transform.localScale = new Vector3(8f, 0.001f, 8f);
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                ColorUtility.TryParseHtmlString("#E6E1D4", out Color cream);
                mat.SetColor("_BaseColor", cream * 0.88f);
                mat.SetFloat("_Smoothness", 0.2f);
                ground.GetComponent<MeshRenderer>().sharedMaterial = mat;
                _made.Add(mat);
                Move(ground);
                var probeGo = new GameObject("Studio Reflections");
                var probe = probeGo.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = Environment();
                probe.size = new Vector3(40f, 40f, 40f);
                probe.importance = 100;
                probe.intensity = 1f;
                _made.Add(probe.customBakedTexture);
                Move(probeGo);
            }

            /// <summary>Soft studio sky: bright overhead softbox, cream walls, a darker floor, so metals read as metal.</summary>
            private static Cubemap Environment()
            {
                const int size = 64;
                var cube = new Cubemap(size, TextureFormat.RGBAHalf, true);
                ColorUtility.TryParseHtmlString("#F2EDDF", out Color cream);
                ColorUtility.TryParseHtmlString("#192020", out Color ink);
                var faces = new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
                foreach (CubemapFace face in faces)
                {
                    var px = new Color[size * size];
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                        Vector3 d = Direction(face, u, -v).normalized;
                        Color c = d.y > 0f ? Color.Lerp(cream * 0.95f, Color.white * 1.5f, Mathf.Pow(d.y, 3f)) : Color.Lerp(cream * 0.75f, ink * 1.5f, Mathf.Sqrt(-d.y));
                        float strip = Mathf.Exp(-Mathf.Pow((Mathf.Atan2(d.x, d.z) - 0.9f) * 3f, 2f)) * Mathf.Clamp01(1f - Mathf.Abs(d.y - 0.25f) * 2.5f);
                        c += Color.white * 1.3f * strip;
                        c.a = 1f;
                        px[y * size + x] = c;
                    }
                    cube.SetPixels(px, face);
                }
                cube.Apply(true);
                return cube;
            }

            private static Vector3 Direction(CubemapFace face, float u, float v)
            {
                switch (face)
                {
                    case CubemapFace.PositiveX: return new Vector3(1f, v, -u);
                    case CubemapFace.NegativeX: return new Vector3(-1f, v, u);
                    case CubemapFace.PositiveY: return new Vector3(u, 1f, -v);
                    case CubemapFace.NegativeY: return new Vector3(u, -1f, v);
                    case CubemapFace.PositiveZ: return new Vector3(u, v, 1f);
                    default: return new Vector3(-u, v, -1f);
                }
            }

            public ClubModel Add(ClubModel prefab, ClubPalette palette)
            {
                ClubModel club = Object.Instantiate(prefab);
                club.Paint(palette);
                Move(club.gameObject);
                return club;
            }

            public Texture2D Render(Vector3 from, Vector3 at, float fov, int width, int height)
            {
                const int ss = 2;
                var go = new GameObject("Studio Camera");
                Move(go);
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.scene = _scene;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.02f;
                cam.farClipPlane = 60f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                ColorUtility.TryParseHtmlString("#F2EDDF", out Color bg);
                cam.backgroundColor = bg;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.renderShadows = true;
                var rt = new RenderTexture(width * ss, height * ss, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                try
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.Render();
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    var big = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                    big.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    RenderTexture.active = previous;
                    Color[] src = big.GetPixels();
                    var dst = new Color[width * height];
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            Color c = Color.clear;
                            for (int dy = 0; dy < ss; dy++)
                            for (int dx = 0; dx < ss; dx++)
                                c += src[(y * ss + dy) * rt.width + x * ss + dx];
                            c /= ss * ss;
                            c.a = 1f;
                            dst[y * width + x] = c;
                        }
                    }
                    Object.DestroyImmediate(big);
                    var small = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    small.SetPixels(dst);
                    small.Apply();
                    return small;
                }
                finally
                {
                    cam.targetTexture = null;
                    rt.Release();
                    Object.DestroyImmediate(rt);
                    Object.DestroyImmediate(go);
                }
            }

            public void Shoot(Vector3 from, Vector3 at, float fov, int width, int height, string path)
            {
                Texture2D tex = Render(from, at, fov, width, height);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }

            public void Dispose()
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(_scene);
                foreach (Object o in _made) Object.DestroyImmediate(o);
            }

            private void AddLight(string name, Quaternion rotation, Color color, float intensity, bool shadows)
            {
                var go = new GameObject(name);
                var light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = color;
                light.intensity = intensity;
                light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                go.transform.rotation = rotation;
                Move(go);
            }

            private void Move(GameObject go) => SceneManager.MoveGameObjectToScene(go, _scene);
        }

        /// <summary>Static copies of the skinned meshes in their sampled pose (skins only re-skin once per editor tick).</summary>
        private sealed class Frozen : IDisposable
        {
            private readonly List<SkinnedMeshRenderer> _hidden = new List<SkinnedMeshRenderer>();
            private readonly List<Object> _made = new List<Object>();

            public Frozen(GameObject body)
            {
                foreach (SkinnedMeshRenderer smr in body.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!smr.enabled) continue;
                    var mesh = new Mesh();
                    smr.BakeMesh(mesh, false);
                    var copy = new GameObject(smr.name + " (frozen)");
                    copy.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    copy.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                    smr.enabled = false;
                    _hidden.Add(smr);
                    _made.Add(mesh);
                    _made.Add(copy);
                }
            }

            public void Dispose()
            {
                foreach (SkinnedMeshRenderer smr in _hidden) smr.enabled = true;
                foreach (Object o in _made) Object.DestroyImmediate(o);
            }
        }
    }

    /// <summary>Club art imports: models without animation, cameras or lights, normal maps flagged, textures capped.</summary>
    public sealed class ClubImportRules : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ClubArtBaker.ModelDir + "/", StringComparison.Ordinal)) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.addCollider = false;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ClubArtBaker.TextureDir + "/", StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_Normal.png", StringComparison.Ordinal);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
        }
    }
}
