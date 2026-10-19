using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Puttshack.Content;
using Puttshack.Runtime.Shop;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Bakes the piece-built golfers (ArtSource/Blender/scripts/pp_golfer_build.py, pp_golfer_bake.py: face, eyes, brows,
    /// lashes, hair, skin and every garment as its own mesh, all skinned to one shared rig) and their Mixamo clips: copies the
    /// staged exports from ArtSource/Exports/Characters, and the clips the controller uses plus the Y Bot reference skeleton
    /// from ArtSource/Mixamo/YBot_TPose, into Art/Characters; builds the skin / eye / hair / cloth materials on the
    /// Puttshack/Character shaders, remaps the FBX materials onto them, bakes a tinted variant of every garment base for each
    /// catalogue colour, runs the Pro Shop bake (which rebuilds Caleb_Golfer.prefab), points the shared controller's states
    /// at the Humanoid clips, then adds Maya's body, the per-club seats in the lead hand, the right-hand grip IK and the
    /// <see cref="GolferMotion"/> table to that prefab. Each club is seated at address (sole on the ground) and rides the lead
    /// hand; impact is where the clubhead, at speed, passes the ball it addressed, the lead hand is drawn back onto its
    /// address pose there (<see cref="GolferHandIK"/>) and the ball is placed where the face then meets it.
    /// </summary>
    public static class GolferCharacterBaker
    {
        public const string CharacterDir = "Assets/_Game/Art/Characters";
        public const string MaterialDir = CharacterDir + "/Materials";
        public const string VariantDir = MaterialDir + "/Variants";
        public const string StagingDir = "ArtSource/Exports/Characters";
        /// <summary>Mixamo Y Bot clips re-exported with a T-pose default pose (ArtSource/Blender/scripts/pp_mixamo_tpose.py).</summary>
        public const string ClipStagingDir = "ArtSource/Mixamo/YBot_TPose";
        public const string MayaRootName = "Maya_Body";
        public const string GripBone = "ClubGrip.L";
        public const string SeatPrefix = "ClubGrip_";
        /// <summary>Visual ball radius on course (BallView draws the ball larger than regulation so it reads on screen).</summary>
        public const float BallRadius = 0.032f;
        /// <summary>How far down the shaft the right palm closes, below the centre of the left hand's grip (metres).</summary>
        public const float TrailGripGap = 0.085f;

        public static readonly string[] Names = { "Caleb", "Maya" };
        public static readonly string[] SlotParts =
        {
            "Body_Arms", CosmeticCatalog.PartPolo, CosmeticCatalog.PartTee, CosmeticCatalog.PartChinos, CosmeticCatalog.PartShorts,
            CosmeticCatalog.PartSocksAnkle, CosmeticCatalog.PartSocksCrew, CosmeticCatalog.PartShoes, CosmeticCatalog.PartBelt,
            "Club_Driver", "Club_Iron", "Club_Wedge", "Club_Putter",
        };

        private sealed class Look
        {
            public Color Hair;
            public Color HairSpec;
            public Color Lash;
            public float HairSmoothness;
        }

        // Tints match the Blender preview materials (pp_golfer.py CHARS hair_tint), converted from linear.
        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            ["Caleb"] = new Look { Hair = new Color(0.23f, 0.16f, 0.11f).gamma, HairSpec = new Color(0.62f, 0.48f, 0.36f), Lash = new Color(0.05f, 0.04f, 0.035f).gamma, HairSmoothness = 0.3f },
            ["Maya"] = new Look { Hair = new Color(0.06f, 0.045f, 0.04f).gamma, HairSpec = new Color(0.42f, 0.34f, 0.3f), Lash = new Color(0.03f, 0.025f, 0.022f).gamma, HairSmoothness = 0.38f },
        };

        /// <summary>
        /// Controller state to Mixamo clip (ArtSource/Mixamo/YBot_TPose file name). The swings are the takes whose lead hand
        /// comes back nearest its address pose at impact (Golf_Drive_Swing's hands pass 11 cm high and Golf_Drive_Medium's
        /// club 70 cm over the ball), so the strike only has to correct a few centimetres.
        /// </summary>
        public static readonly (string State, string Clip)[] StateClips =
        {
            ("Idle", "Idle_Standard"),
            ("Swing_Driver", "Golf_Drive_Standard"),
            ("Swing_Iron", "Golf_Drive_Standard"),
            ("Swing_Wedge", "Golf_Chip_Swing2"),
            ("Putt", "Golf_Putt_Medium"),
            ("React_Good", "React_FistPump"),
            ("React_Miss", "React_Disappointed"),
            ("Celebrate", "React_Victory"),
        };

        /// <summary>Address states hold the first frame of their swing, so the swing always starts from the stance.</summary>
        public static readonly (string State, string Swing)[] AddressStates =
        {
            ("Address_Driver", "Swing_Driver"), ("Address_Iron", "Swing_Iron"), ("Address_Wedge", "Swing_Wedge"), ("Putt_Address", "Putt"),
        };

        private static readonly (string Model, string Address, string Swing)[] ClubStates =
        {
            ("Driver", "Address_Driver", "Swing_Driver"),
            ("Iron", "Address_Iron", "Swing_Iron"),
            ("Wedge", "Address_Wedge", "Swing_Wedge"),
            ("Putter", "Putt_Address", "Putt"),
        };

        public static readonly string[] RequiredStates = StateClips.Select(s => s.State).Concat(AddressStates.Select(a => a.State)).ToArray();

        [MenuItem("Puttshack/Characters/Bake Golfers", priority = 45)]
        public static void BakeMenu()
        {
            List<string> notes = BakeAll();
            foreach (string n in notes) Debug.LogWarning("[Puttshack] Golfer bake: " + n);
            List<string> problems = Verify();
            if (problems.Count > 0) Debug.LogError("[Puttshack] Golfer verification failed: " + string.Join("; ", problems));
            else Debug.Log("[Puttshack] Golfers baked.");
        }

        [MenuItem("Puttshack/Characters/Verify Golfers", priority = 46)]
        public static void VerifyMenu()
        {
            List<string> problems = Verify();
            if (problems.Count > 0) Debug.LogError("[Puttshack] Golfer verification failed: " + string.Join("; ", problems));
            else Debug.Log("[Puttshack] Golfers verified.");
        }

        /// <summary>Batch-mode entry point: bake, then fail the run if verification finds problems.</summary>
        public static void BatchBake()
        {
            List<string> notes = BakeAll();
            foreach (string n in notes) Debug.LogWarning("[Puttshack] Golfer bake: " + n);
            List<string> problems = Verify();
            if (problems.Count > 0) throw new Exception("Golfer verification failed: " + string.Join("; ", problems));
            Debug.Log("[Puttshack] Golfer batch bake complete.");
        }

        public static List<string> BakeAll()
        {
            var notes = new List<string>();
            ProShopBaker.EnsureFolder(MaterialDir);
            ProShopBaker.EnsureFolder(VariantDir);
            int copied = SyncStaged();
            if (copied > 0) notes.Add($"Copied {copied} staged character files.");

            Dictionary<string, Material> materials = BakeMaterials();
            foreach (string name in Names) Remap(ModelPath(name), name, materials);

            var art = AssetDatabase.LoadAssetAtPath<CosmeticArt>(ProShopBaker.ArtAssetPath);
            if (art == null)
            {
                ProShopBaker.BakeAll();
                art = AssetDatabase.LoadAssetAtPath<CosmeticArt>(ProShopBaker.ArtAssetPath);
            }
            BakeVariants(art, materials);
            AssetDatabase.SaveAssets();

            notes.AddRange(ProShopBaker.BakeAll());
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ProShopBaker.ControllerPath);
            Dictionary<string, AnimationClip> clips = BakeStates(controller, notes);
            AssetDatabase.SaveAssets();
            BakeGolferPrefab(controller, clips, notes);
            AssetDatabase.SaveAssets();
            return notes;
        }

        public static string ModelPath(string name) => name == "Maya" ? ProShopImportRules.MayaModel : ProShopImportRules.CalebModel;

        // ---------------------------------------------------------------- Sync

        /// <summary>Per-character exports of pp_golfer_build.py / pp_golfer_bake.py; every Shared/Textures PNG is staged too.</summary>
        private static readonly string[] StagedSuffixes =
        {
            "_Modular.fbx", "_Face_Albedo.png", "_Face_Mask.png", "_Face_Normal.png", "_Skin_Albedo.png", "_Skin_Mask.png",
            "_Skin_Normal.png", "_Eye_Albedo.png", "_Eye_Mask.png", "_Hair.png", "_Hair_Normal.png", "_Brow.png", "_Lash.png",
        };

        /// <summary>Whether a path relative to the staging (or character) folder is one of the golfer exports.</summary>
        private static bool IsStagedCharacterFile(string rel)
        {
            string[] parts = rel.Replace('\\', '/').Split('/');
            string file = parts[parts.Length - 1];
            if (parts.Length == 3 && parts[0] == "Shared" && parts[1] == "Textures") return file.EndsWith(".png", StringComparison.Ordinal);
            return parts.Length >= 2 && Names.Contains(parts[0]) && StagedSuffixes.Any(s => file == parts[0] + s);
        }

        /// <summary>
        /// Copies changed character exports (FBX, textures) and the Mixamo clips the controller uses (after the reference
        /// skeleton they copy their avatar from) into the project, drops textures and clips nothing uses any more, and
        /// reimports clips imported with older settings; returns the number of files changed.
        /// </summary>
        public static int SyncStaged()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            int copied = 0;
            string source = Path.Combine(project, StagingDir);
            if (Directory.Exists(source))
            {
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(source.Length + 1).Replace('\\', '/');
                    if (!IsStagedCharacterFile(rel)) continue;
                    if (Copy(file, Path.Combine(project, CharacterDir, rel))) copied++;
                }
            }
            foreach (string folder in Names.Select(n => $"{CharacterDir}/{n}/Textures").Append($"{CharacterDir}/Shared/Textures"))
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (string guid in AssetDatabase.FindAssets("", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path) || IsStagedCharacterFile(path.Substring(CharacterDir.Length + 1))) continue;
                    AssetDatabase.DeleteAsset(path);
                    copied++;
                }
            }

            string clipSource = Path.Combine(project, ClipStagingDir);
            string clipTarget = Path.Combine(project, ProShopImportRules.AnimationRoot);
            string reference = Path.Combine(clipSource, ProShopImportRules.MixamoReferenceFile);
            if (File.Exists(reference) && Copy(reference, Path.Combine(clipTarget, ProShopImportRules.MixamoReferenceFile)))
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                copied++;
            }
            var used = new HashSet<string> { Path.GetFileNameWithoutExtension(ProShopImportRules.MixamoReferenceFile) };
            foreach (var (_, clip) in StateClips)
            {
                used.Add(clip);
                string staged = Path.Combine(clipSource, clip + ".fbx");
                if (File.Exists(staged) && Copy(staged, Path.Combine(clipTarget, clip + ".fbx"))) copied++;
            }
            if (Directory.Exists(clipTarget))
            {
                foreach (string file in Directory.GetFiles(clipTarget, "*.fbx"))
                {
                    if (used.Contains(Path.GetFileNameWithoutExtension(file))) continue;
                    AssetDatabase.DeleteAsset(ProShopImportRules.AnimationRoot + Path.GetFileName(file));
                    copied++;
                }
            }
            if (copied > 0) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string clip in used)
            {
                string path = $"{ProShopImportRules.AnimationRoot}{clip}.fbx";
                AnimationClip imported = LoadClip(clip);
                if (imported != null && !ProShopImportRules.MixamoClipCurrent(imported))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    copied++;
                }
            }
            return copied;
        }

        private static bool Copy(string from, string to)
        {
            if (File.Exists(to) && SameBytes(from, to)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
            return true;
        }

        private static bool SameBytes(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            byte[] x = File.ReadAllBytes(a);
            byte[] y = File.ReadAllBytes(b);
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i]) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- Materials

        private static Texture2D Tex(string rel) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{CharacterDir}/{rel}.png");

        private static Material Mat(string name, Shader shader)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.shaderKeywords = new string[0];
            m.enableInstancing = true;
            // Texture slots saved under an earlier shader would keep retired textures referenced.
            var so = new SerializedObject(m);
            SerializedProperty textures = so.FindProperty("m_SavedProperties.m_TexEnvs");
            for (int i = textures.arraySize - 1; i >= 0; i--)
            {
                if (shader.FindPropertyIndex(textures.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue) < 0) textures.DeleteArrayElementAtIndex(i);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private static Material Lit(string name, string mode)
        {
            Material m = Mat(name, Shader.Find("Puttshack/Character/Lit"));
            string[] modes = { "Standard", "Skin", "Cloth", "Eye" };
            m.SetFloat("_Mode", Array.IndexOf(modes, mode));
            m.EnableKeyword("_MODE_" + mode.ToUpperInvariant());
            m.SetFloat("_UseDetail", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_BumpScale", 1f);
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetFloat("_CavityStrength", 1f);
            m.SetTexture("_BaseMap", null);
            m.SetTexture("_BumpMap", null);
            m.SetTexture("_MaskMap", null);
            m.SetTexture("_DetailNormalMap", null);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.renderQueue = -1;
            return m;
        }

        private static void Detail(Material m, float strength, float tiling)
        {
            m.SetFloat("_UseDetail", 1f);
            m.EnableKeyword("_DETAIL_NORMAL");
            m.SetTexture("_DetailNormalMap", Tex("Shared/Textures/Skin_Pores_Normal"));
            m.SetFloat("_DetailNormalScale", strength);
            m.SetFloat("_DetailTiling", tiling);
        }

        /// <summary>Textured garment: white albedo with its own weave, tinted per catalogue colour by BakeVariants.</summary>
        private static void Cloth(Material m, string tex, float smoothness, float sheen, float sheenRoughness)
        {
            m.SetTexture("_BaseMap", Tex($"Shared/Textures/{tex}_Albedo"));
            m.SetTexture("_BumpMap", Tex($"Shared/Textures/{tex}_Normal"));
            m.SetFloat("_Smoothness", smoothness);
            m.SetColor("_SheenColor", new Color(sheen, sheen, sheen, 1f));
            m.SetFloat("_SheenRoughness", sheenRoughness);
            m.SetFloat("_Wrap", 0.35f);
        }

        private static Dictionary<string, Material> BakeMaterials()
        {
            var all = new Dictionary<string, Material>();
            void Keep(Material m)
            {
                EditorUtility.SetDirty(m);
                all[m.name] = m;
            }

            foreach (string name in Names)
            {
                Look look = Looks[name];
                // Face and body skin: painted albedo, baked AO / smoothness / thickness / cavity mask, crease normals, pores.
                foreach (string part in new[] { "Skin", "Face" })
                {
                    Material skin = Lit($"Mat_{part}_{name}", "Skin");
                    skin.SetTexture("_BaseMap", Tex($"{name}/Textures/{name}_{part}_Albedo"));
                    skin.SetTexture("_BumpMap", Tex($"{name}/Textures/{name}_{part}_Normal"));
                    skin.SetTexture("_MaskMap", Tex($"{name}/Textures/{name}_{part}_Mask"));
                    skin.SetFloat("_Smoothness", part == "Face" ? 0.72f : 0.8f);
                    skin.SetFloat("_BumpScale", part == "Face" ? 0.8f : 1f);
                    skin.SetColor("_ScatterColor", new Color(1f, 0.45f, 0.3f));
                    skin.SetFloat("_Wrap", 0.5f);
                    skin.SetFloat("_Transmission", 0.8f);
                    skin.SetColor("_TransmissionColor", new Color(1f, 0.34f, 0.2f));
                    skin.SetFloat("_SecondLobeSmoothness", 0.4f);
                    skin.SetFloat("_SecondLobeMix", 0.1f);
                    skin.SetFloat("_CavityStrength", 0.8f);
                    Detail(skin, part == "Face" ? 0.3f : 0.4f, part == "Face" ? 40f : 28f);
                    Keep(skin);
                }

                Material eye = Lit($"Mat_Eye_{name}", "Eye");
                eye.SetTexture("_BaseMap", Tex($"{name}/Textures/{name}_Eye_Albedo"));
                eye.SetTexture("_MaskMap", Tex($"{name}/Textures/{name}_Eye_Mask"));
                eye.SetFloat("_Smoothness", 0.4f);
                eye.SetColor("_BaseColor", new Color(0.9f, 0.88f, 0.86f));
                Keep(eye);

                Shader hair = Shader.Find("Puttshack/Character/Hair");
                foreach ((string part, Color tint, float cutoff) in new[] { ("Hair", look.Hair, 0.5f), ("Brow", look.Hair, 0.4f), ("Lash", look.Lash, 0.5f) })
                {
                    Material m = Mat($"Mat_{part}_{name}", hair);
                    m.SetTexture("_BaseMap", Tex($"{name}/Textures/{name}_{part}"));
                    m.SetTexture("_BumpMap", part == "Hair" ? Tex($"{name}/Textures/{name}_Hair_Normal") : null);
                    m.SetColor("_BaseColor", tint);
                    m.SetFloat("_Cutoff", cutoff);
                    m.SetFloat("_FringeCutoff", part == "Hair" ? 0.06f : 0.04f);
                    m.SetFloat("_Smoothness", look.HairSmoothness);
                    m.SetColor("_SpecColorA", part == "Hair" ? new Color(0.2f, 0.19f, 0.18f) : new Color(0.06f, 0.06f, 0.06f));
                    m.SetColor("_SpecColorB", part == "Hair" ? look.HairSpec * 0.6f : new Color(0.04f, 0.04f, 0.04f));
                    m.SetFloat("_ExponentA", 280f);
                    m.SetFloat("_ExponentB", 60f);
                    m.SetFloat("_Cull", (float)CullMode.Off);
                    Keep(m);
                }
            }

            Material cornea = Mat("Mat_Cornea", Shader.Find("Puttshack/Character/Cornea"));
            cornea.SetFloat("_Smoothness", 0.96f);
            cornea.SetFloat("_Catchlight", 1.2f);
            Keep(cornea);

            // Garments are shared by both bodies (MakeHuman clothes keep their UVs when fitted).
            foreach ((string garment, string tex, float smooth, float sheen, float sheenRough) in new[]
            {
                ("Mat_Top_Polo", "Polo", 0.3f, 0.22f, 0.4f), ("Mat_Collar_Polo", "Polo", 0.3f, 0.22f, 0.4f), ("Mat_Top_Tee", "Tee", 0.2f, 0.14f, 0.65f),
                ("Mat_Bottom_Chinos", "Pants", 0.22f, 0.12f, 0.7f), ("Mat_Bottom_Shorts", "Pants", 0.22f, 0.12f, 0.7f),
                ("Mat_Socks_Knit", "Socks", 0.1f, 0.25f, 0.8f), ("Mat_Socks_Band", "Socks", 0.1f, 0.25f, 0.8f),
            })
            {
                Material m = Lit(garment, "Cloth");
                Cloth(m, tex, smooth, sheen, sheenRough);
                Keep(m);
            }
            foreach ((string shoe, float smooth) in new[] { ("Mat_Shoes_Golf", 0.58f), ("Mat_ShoeAccent_Golf", 0.46f), ("Mat_Sole_Golf", 0.28f) })
            {
                Material m = Lit(shoe, "Standard");
                m.SetTexture("_BaseMap", Tex("Shared/Textures/Shoes_Albedo"));
                m.SetTexture("_BumpMap", Tex("Shared/Textures/Shoes_Normal"));
                m.SetFloat("_Smoothness", smooth);
                Keep(m);
            }
            Material belt = Lit("Mat_Belt_Leather", "Standard");
            belt.SetTexture("_BaseMap", Tex("Shared/Textures/Belt_Albedo"));
            belt.SetTexture("_BumpMap", Tex("Shared/Textures/Belt_Normal"));
            belt.SetFloat("_Smoothness", 0.5f);
            Keep(belt);
            Material buckle = Lit("Mat_Buckle_Metal", "Standard");
            buckle.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.82f));
            buckle.SetFloat("_Metallic", 0.9f);
            buckle.SetFloat("_Smoothness", 0.78f);
            Keep(buckle);
            foreach (string logo in new[] { "Mat_Logo_Polo", "Mat_Logo_Tee" })
            {
                Material m = Lit(logo, "Standard");
                m.SetTexture("_BaseMap", Tex("Shared/Textures/Logo_Mask"));
                m.SetFloat("_AlphaClip", 1f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cutoff", 0.5f);
                m.SetFloat("_Smoothness", 0.35f);
                m.renderQueue = (int)RenderQueue.AlphaTest;
                Keep(m);
            }

            // Clubs are the same for everyone: rubber grip, chrome shaft, satin steel head, gloss crown.
            foreach ((string club, Color color, float metal, float smooth) in new[]
            {
                ("Mat_ClubGrip", new Color(0.07f, 0.07f, 0.075f), 0f, 0.32f),
                ("Mat_ClubShaft", new Color(0.8f, 0.8f, 0.82f), 1f, 0.86f),
                ("Mat_ClubHead", new Color(0.72f, 0.72f, 0.74f), 1f, 0.68f),
                ("Mat_ClubCrown", new Color(0.05f, 0.05f, 0.06f), 0f, 0.82f),
                ("Mat_ClubAccent", new Color(0.86f, 0.2f, 0.12f), 0f, 0.5f),
            })
            {
                Material m = Lit(club, "Standard");
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Metallic", metal);
                m.SetFloat("_Smoothness", smooth);
                Keep(m);
            }

            // Materials of the retired stylized golfers (per-body tops and bottoms, caps) go with them.
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != MaterialDir) continue;
                if (!all.ContainsKey(Path.GetFileNameWithoutExtension(path))) AssetDatabase.DeleteAsset(path);
            }
            return all;
        }

        /// <summary>Points the FBX's materials at the baked ones of the same name.</summary>
        private static void Remap(string modelPath, string name, Dictionary<string, Material> materials)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (importer == null || model == null) return;
            var used = new HashSet<string>(model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name));
            bool changed = false;
            Dictionary<AssetImporter.SourceAssetIdentifier, Object> existing = importer.GetExternalObjectMap();
            foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, Object> kv in existing)
            {
                if (kv.Key.type == typeof(Material) && !materials.ContainsKey(kv.Key.name))
                {
                    importer.RemoveRemap(kv.Key);
                    changed = true;
                }
            }
            foreach (string mat in used)
            {
                if (!materials.TryGetValue(mat, out Material baked)) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), mat);
                if (existing.TryGetValue(id, out Object current) && current == baked) continue;
                importer.AddRemap(id, baked);
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        /// <summary>Garment roles: one tinted variant per (catalogue colour, base).</summary>
        private static void BakeVariants(CosmeticArt art, Dictionary<string, Material> materials)
        {
            var bases = materials.Values.Where(m => IsGarment(m.name)).OrderBy(m => m.name).ToList();
            art.Bases = bases;
            var pairs = new SortedDictionary<string, (string Role, string Hex)>();
            foreach (CosmeticItem item in CosmeticCatalog.All)
            {
                foreach (KeyValuePair<string, string> kv in item.Colors) pairs[CosmeticArt.MaterialKey(kv.Key, kv.Value)] = (kv.Key, kv.Value);
            }

            var keep = new HashSet<string>();
            art.Variants.Clear();
            foreach ((string role, string hex) in pairs.Values)
            {
                foreach (Material baseMat in bases)
                {
                    if (CosmeticPaint.RoleOf(baseMat) != role) continue;
                    string key = CosmeticArt.VariantKey(role, hex, baseMat.name);
                    string path = $"{VariantDir}/{key}.mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null)
                    {
                        m = new Material(baseMat) { name = key };
                        AssetDatabase.CreateAsset(m, path);
                    }
                    m.shader = baseMat.shader;
                    m.CopyPropertiesFromMaterial(baseMat);
                    m.shaderKeywords = baseMat.shaderKeywords;
                    m.renderQueue = baseMat.renderQueue;
                    m.enableInstancing = true;
                    m.SetColor("_BaseColor", CosmeticArt.ParseHex(hex));
                    EditorUtility.SetDirty(m);
                    art.Variants.Add(new CosmeticArt.MaterialEntry { Key = key, Material = m });
                    keep.Add(path);
                }
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { VariantDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!keep.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
            art.Invalidate();
            EditorUtility.SetDirty(art);
        }

        private static bool IsGarment(string materialName)
        {
            string[] roles = { "Top", "Collar", "Logo", "Bottom", "Socks", "Shoes", "ShoeAccent", "Sole", "Belt", "Buckle" };
            return roles.Any(r => materialName.StartsWith("Mat_" + r + "_", StringComparison.Ordinal));
        }

        // ---------------------------------------------------------------- Animator

        public static AnimationClip LoadClip(string clipName)
        {
            int split = clipName.IndexOf(':');
            string path = split < 0 ? $"{ProShopImportRules.AnimationRoot}{clipName}.fbx" : ModelPath(clipName.Substring(0, split));
            string take = split < 0 ? null : clipName.Substring(split + 1);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal) && (take == null || c.name == take));
        }

        /// <summary>
        /// One state per <see cref="StateClips"/> entry on its Mixamo clip (the shop Idle included, replacing the static pose
        /// ProShopBaker starts it on) plus the held address states; returns state name to clip.
        /// </summary>
        private static Dictionary<string, AnimationClip> BakeStates(AnimatorController controller, List<string> notes)
        {
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            var clips = new Dictionary<string, AnimationClip>();
            foreach ((string state, string clipName) in StateClips)
            {
                AnimationClip clip = LoadClip(clipName);
                if (clip == null) notes.Add($"No {clipName} clip for {state}.");
                else if (!clip.humanMotion) notes.Add($"{clip.name} is not a Humanoid clip.");
                else clips[state] = clip;
            }

            var wanted = new HashSet<string>();
            int row = 0;
            AnimatorState Ensure(string stateName, AnimationClip clip, float speed)
            {
                wanted.Add(stateName);
                AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
                if (state == null) state = sm.AddState(stateName, new Vector3(320f + 240f * (row % 3), 60f * (row / 3), 0f));
                row++;
                state.motion = clip;
                state.speed = speed;
                state.iKOnFeet = true;
                state.writeDefaultValues = true;
                return state;
            }
            foreach (KeyValuePair<string, AnimationClip> kv in clips.ToList())
            {
                AnimatorState state = Ensure(kv.Key, kv.Value, 1f);
                if (kv.Key == "Idle") sm.defaultState = state;
            }
            foreach ((string state, string swing) in AddressStates)
            {
                if (!clips.TryGetValue(swing, out AnimationClip clip)) continue;
                Ensure(state, clip, 0f);
                clips[state] = clip;
            }
            foreach (ChildAnimatorState child in sm.states)
            {
                if (!wanted.Contains(child.state.name)) sm.RemoveState(child.state);
            }
            EditorUtility.SetDirty(controller);
            return clips;
        }

        // ---------------------------------------------------------------- Prefab

        private static void BakeGolferPrefab(AnimatorController controller, Dictionary<string, AnimationClip> clips, List<string> notes)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ProShopBaker.CalebPrefabPath);
            try
            {
                var look = root.GetComponent<GolferAppearance>();
                look.Extra.Clear();
                Transform old = root.transform.Find(MayaRootName);
                if (old != null) Object.DestroyImmediate(old.gameObject);

                var mayaModel = AssetDatabase.LoadAssetAtPath<GameObject>(ProShopImportRules.MayaModel);
                if (mayaModel != null)
                {
                    var maya = (GameObject)PrefabUtility.InstantiatePrefab(mayaModel, root.transform);
                    maya.name = MayaRootName;
                    maya.transform.localPosition = Vector3.zero;
                    maya.transform.localRotation = Quaternion.identity;
                    Animator anim = maya.GetComponent<Animator>() ?? maya.AddComponent<Animator>();
                    anim.runtimeAnimatorController = controller;
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    if (anim.avatar == null || !anim.avatar.isHuman || !anim.avatar.isValid) notes.Add("Maya avatar is not a valid Humanoid.");
                    foreach (SkinnedMeshRenderer smr in maya.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        smr.shadowCastingMode = ShadowCastingMode.On;
                        smr.updateWhenOffscreen = true;
                    }
                    look.AddBody("Maya", maya);
                }
                else notes.Add($"Missing {ProShopImportRules.MayaModel}; Maya is shown as a stand-in.");

                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                for (int body = 0; body < look.BodyCount; body++)
                {
                    Animator anim = look.AnimatorOf(body);
                    if (anim != null && anim.GetComponent<GolferHandIK>() == null) anim.gameObject.AddComponent<GolferHandIK>();
                }
                GolferMotion motion = root.GetComponent<GolferMotion>() ?? root.AddComponent<GolferMotion>();
                motion.Appearance = look;
                motion.Clubs = MeasureClubs(look, clips, notes);
                notes.AddRange(GolferLookBaker.Bake(look));
                look.Apply(CosmeticCatalog.DefaultFor(0), 0);
                PrefabUtility.SaveAsPrefabAsset(root, ProShopBaker.CalebPrefabPath, out bool ok);
                if (!ok) throw new IOException($"Could not save {ProShopBaker.CalebPrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject BodyRoot(GolferAppearance look, int body) => body == 0 ? look.gameObject : look.Extra[body - 1].Root;

        private static string BodyName(int body) => Names[Mathf.Clamp(body, 0, Names.Length - 1)];

        /// <summary>
        /// Per club and body: seats the club at address, measures the right-hand grip and the lead hand's address pose for
        /// <see cref="GolferHandIK"/>, finds impact on the clubhead's path through the swing and places the ball in front
        /// of the face at impact with both hands on. Contact times are per body: the same clip on each skeleton meets the
        /// ball a few milliseconds apart, which at clubhead speed is centimetres.
        /// </summary>
        private static List<GolferMotion.ClubMotion> MeasureClubs(GolferAppearance look, Dictionary<string, AnimationClip> clips, List<string> notes)
        {
            var result = new List<GolferMotion.ClubMotion>();
            for (int body = 0; body < look.BodyCount; body++)
            {
                Animator anim = look.AnimatorOf(body);
                GolferHandIK ik = anim != null ? anim.GetComponent<GolferHandIK>() : null;
                if (ik != null) ik.Grips.Clear();
            }
            foreach (var spec in ClubStates)
            {
                if (!clips.ContainsKey(spec.Address) || !clips.TryGetValue(spec.Swing, out AnimationClip swing))
                {
                    notes.Add($"No address/swing clips for the {spec.Model}.");
                    continue;
                }
                var m = new GolferMotion.ClubMotion
                {
                    Model = spec.Model,
                    Address = spec.Address,
                    Swing = spec.Swing,
                    Contact = new float[look.BodyCount],
                    Ball = new Vector3[look.BodyCount],
                };
                for (int body = 0; body < look.BodyCount; body++)
                {
                    GameObject bodyRoot = BodyRoot(look, body);
                    bool wasActive = bodyRoot.activeSelf;
                    bodyRoot.SetActive(true);
                    try
                    {
                        Animator anim = look.AnimatorOf(body);
                        PoseState(anim, spec.Address, 0f);
                        if (!SeatClub(anim, bodyRoot.transform, spec.Model, look.transform, notes)) continue;
                        if (!ClubFace(bodyRoot.transform, spec.Model, look.transform, out Vector3 addressFace, out float addressSole)) continue;
                        var ik = anim.GetComponent<GolferHandIK>();
                        Transform seat = ProShopBaker.FindDeep(bodyRoot.transform, SeatPrefix + spec.Model);
                        Vector3 faceOnClub = seat.InverseTransformPoint(look.transform.TransformPoint(addressFace));
                        TrailGrip(anim, seat, out Vector3 offset, out Quaternion rotation, out float turn);
                        var grip = new GolferHandIK.Grip
                        {
                            Model = spec.Model,
                            Seat = seat,
                            Offset = offset,
                            Rotation = rotation,
                            Swing = spec.Swing,
                        };

                        float contact = Impact(anim, spec.Swing, swing.length, seat, faceOnClub, look.transform, addressFace, out float miss);
                        if (contact > 0f)
                        {
                            PoseState(anim, spec.Swing, contact);
                            Vector3 clipFace = FacePoint(seat, faceOnClub, look.transform);
                            grip.Contact = contact;
                            grip.LeadShift = anim.transform.InverseTransformVector(look.transform.TransformVector(addressFace - clipFace));
                        }
                        ik.Grips.Add(grip);
                        if (contact <= 0f)
                        {
                            notes.Add($"{BodyName(body)}'s {spec.Model} never gets up to speed in {swing.name}.");
                            continue;
                        }
                        PoseHeld(anim, spec.Swing, contact, spec.Model);
                        Vector3 face = FacePoint(seat, faceOnClub, look.transform);
                        ClubFace(bodyRoot.transform, spec.Model, look.transform, out _, out float sole);
                        m.Ball[body] = new Vector3(face.x - BallRadius, 0f, face.z);
                        m.Contact[body] = contact;
                        float gap = GripGap(anim, ik, spec.Model, spec.Swing, swing.length);
                        notes.Add($"{BodyName(body)} {spec.Model}: impact {contact:0.000} s, face passes {miss:0.000} m from address on the clip alone and {(face - addressFace).magnitude:0.000} m with the strike, sole {addressSole:0.000} m at address / {sole:0.000} m at impact, right hand turned {turn:0} deg onto the grip, worst grip gap {gap:0.000} m.");
                    }
                    finally
                    {
                        bodyRoot.SetActive(wasActive);
                    }
                }
                result.Add(m);
            }
            if (clips.ContainsKey("Idle"))
            {
                for (int body = 0; body < look.BodyCount; body++)
                {
                    GameObject bodyRoot = BodyRoot(look, body);
                    bool wasActive = bodyRoot.activeSelf;
                    bodyRoot.SetActive(true);
                    Animator anim = look.AnimatorOf(body);
                    if (anim != null) PoseState(anim, "Idle", 0f);
                    bodyRoot.SetActive(wasActive);
                }
            }
            return result;
        }

        /// <summary>
        /// Poses the body as the controller plays <paramref name="state"/> at <paramref name="seconds"/> into its clip, root
        /// settings and foot IK included (AnimationClip.SampleAnimation skips the clip's root transform settings).
        /// </summary>
        public static void PoseState(Animator anim, string state, float seconds, bool rebind = true)
        {
            if (rebind) anim.Rebind();
            AnimationClip clip = null;
            if (anim.runtimeAnimatorController is AnimatorController controller)
                clip = controller.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == state)?.motion as AnimationClip;
            float length = clip != null && clip.length > 0f ? clip.length : 1f;
            anim.Play(state, 0, Mathf.Clamp01(seconds / length));
            anim.Update(0f);
        }

        /// <summary>Where a hand's palm closes around the grip: three quarters of the way to the knuckles, in front of the palm.</summary>
        public static Vector3 GripPoint(Animator anim, bool lead)
        {
            Transform wrist = anim.GetBoneTransform(lead ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform middle = anim.GetBoneTransform(lead ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            Transform index = anim.GetBoneTransform(lead ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform little = anim.GetBoneTransform(lead ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            Vector3 along = middle.position - wrist.position;
            Vector3 palm = Vector3.Cross(index.position - little.position, along).normalized * (lead ? 1f : -1f);
            return wrist.position + along * 0.75f + palm * (along.magnitude * 0.25f);
        }

        /// <summary>
        /// <see cref="PoseState"/> with the club in hand: the right hand closes on the grip of <paramref name="model"/> and the
        /// lead hand eases onto the ball around impact (or the right hand lets go when null), as <see cref="GolferMotion"/>
        /// holds it in play.
        /// </summary>
        public static void PoseHeld(Animator anim, string state, float seconds, string model)
        {
            PoseState(anim, state, seconds);
            var ik = anim.GetComponent<GolferHandIK>();
            if (ik == null) return;
            if (model != null) ik.Hold(model);
            else ik.Release();
            ik.Snap();
            ik.Apply();
        }

        /// <summary>
        /// Right wrist pose in the seat's space that closes the right palm on the grip <see cref="TrailGripGap"/> below the
        /// left hand: the address pose's hand, turned so its knuckle line (little to index finger) runs down the shaft.
        /// </summary>
        private static void TrailGrip(Animator anim, Transform seat, out Vector3 offset, out Quaternion rotation, out float turnDegrees)
        {
            Transform wrist = anim.GetBoneTransform(HumanBodyBones.RightHand);
            Transform index = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            Transform little = anim.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            Vector3 palm = GripPoint(anim, false);
            Quaternion turn = Quaternion.FromToRotation(index.position - little.position, seat.up);
            turnDegrees = Quaternion.Angle(Quaternion.identity, turn);
            Vector3 wristPos = seat.position + seat.up * TrailGripGap - turn * (palm - wrist.position);
            offset = seat.InverseTransformPoint(wristPos);
            rotation = Quaternion.Inverse(seat.rotation) * (turn * wrist.rotation);
        }

        /// <summary>
        /// Seconds into the swing at impact, on the clip alone: where the clubface passes closest to where it addressed the
        /// ball while moving at least half its top speed (the strike, not the address hover, the takeaway or the finish),
        /// interpolated between samples. 0 if the clubhead never moves.
        /// </summary>
        private static float Impact(Animator anim, string swingState, float length, Transform seat, Vector3 faceOnClub, Transform root, Vector3 addressFace, out float miss)
        {
            const float step = 1f / 120f;
            int n = Mathf.FloorToInt(length / step) + 1;
            var face = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                PoseState(anim, swingState, i * step);
                face[i] = FacePoint(seat, faceOnClub, root);
            }
            var speed = new float[n];
            float top = 0f;
            for (int i = 1; i < n - 1; i++)
            {
                speed[i] = (face[i + 1] - face[i - 1]).magnitude / (2f * step);
                top = Mathf.Max(top, speed[i]);
            }
            miss = float.MaxValue;
            float contact = 0f;
            for (int i = 1; i < n - 1 && top > 0f; i++)
            {
                if (speed[i] < 0.5f * top) continue;
                Vector3 path = face[i + 1] - face[i];
                float s = path.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(addressFace - face[i], path) / path.sqrMagnitude) : 0f;
                float d = (face[i] + path * s - addressFace).magnitude;
                if (d >= miss) continue;
                miss = d;
                contact = (i + s) * step;
            }
            return contact;
        }

        /// <summary>Largest distance over the swing between the right palm and its spot on the grip, with the hand IK on.</summary>
        private static float GripGap(Animator anim, GolferHandIK ik, string model, string swingState, float length)
        {
            GolferHandIK.Grip grip = ik.Grips.Find(g => g.Model == model);
            float worst = 0f;
            for (float t = 0f; t <= length; t += 1f / 30f)
            {
                PoseHeld(anim, swingState, t, model);
                Vector3 spot = grip.Seat.position + grip.Seat.up * TrailGripGap;
                worst = Mathf.Max(worst, (GripPoint(anim, false) - spot).magnitude);
            }
            ik.Release();
            ik.Snap();
            return worst;
        }

        /// <summary>
        /// Seats the club in the body's current (address) pose on its own child of ClubGrip.L, which the club's skin is
        /// re-pointed to: shaft from the lead-hand grip out toward the ball (root +Z), tilted until the sole meets the
        /// ground, face square to the target (root -X).
        /// </summary>
        private static bool SeatClub(Animator anim, Transform body, string model, Transform root, List<string> notes)
        {
            Transform club = ProShopBaker.FindDeep(body, GolferAppearance.ClubPrefix + model);
            Transform grip = ProShopBaker.FindDeep(body, GripBone);
            var smr = club != null ? club.GetComponent<SkinnedMeshRenderer>() : null;
            if (smr == null || grip == null || !ClubPoints(smr, out int boneIndex, out List<Vector3> head))
            {
                notes.Add($"{body.name} has no skinned {GolferAppearance.ClubPrefix}{model} on {GripBone}.");
                return false;
            }
            Transform seat = grip.Find(SeatPrefix + model);
            if (seat == null)
            {
                seat = new GameObject(SeatPrefix + model).transform;
                seat.SetParent(grip, false);
            }

            Vector3 lead = root.InverseTransformPoint(GripPoint(anim, true));
            Vector3 flat = Vector3.forward;
            Quaternion Frame(float down)
            {
                Vector3 shaft = flat * Mathf.Cos(down) + Vector3.down * Mathf.Sin(down);
                return Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.left, shaft).normalized, shaft);
            }
            float Sole(float down)
            {
                Quaternion q = Frame(down);
                return head.Min(p => (lead + q * p).y);
            }
            float lo = 5f * Mathf.Deg2Rad, hi = 89f * Mathf.Deg2Rad;
            for (int i = 0; i < 40; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Sole(mid) > 0f) lo = mid;
                else hi = mid;
            }
            Quaternion frame = Frame(0.5f * (lo + hi));
            seat.SetPositionAndRotation(root.TransformPoint(lead), root.rotation * frame);
            seat.localScale = Vector3.one;
            Transform[] bones = smr.bones;
            bones[boneIndex] = seat;
            smr.bones = bones;
            return true;
        }

        /// <summary>Club vertices in the club's grip-bone space (the club is skinned to that bone alone).</summary>
        private static bool ClubPoints(SkinnedMeshRenderer smr, out int bone, out List<Vector3> head)
        {
            head = null;
            bone = smr != null && smr.sharedMesh != null ? Array.FindIndex(smr.bones, b => b != null && b.name.StartsWith("ClubGrip", StringComparison.Ordinal)) : -1;
            if (bone < 0) return false;
            Matrix4x4 bind = smr.sharedMesh.bindposes[bone];
            Vector3[] verts = smr.sharedMesh.vertices;
            var local = new List<Vector3>(verts.Length);
            float far = float.MinValue;
            foreach (Vector3 v in verts)
            {
                Vector3 p = bind.MultiplyPoint3x4(v);
                local.Add(p);
                far = Mathf.Max(far, p.y);
            }
            head = local.Where(p => p.y > far - 0.07f).ToList();
            return head.Count > 0;
        }

        private static bool ClubHead(Transform body, string model, out Transform grip, out List<Vector3> head)
        {
            grip = null;
            head = null;
            Transform club = ProShopBaker.FindDeep(body, GolferAppearance.ClubPrefix + model);
            var smr = club != null ? club.GetComponent<SkinnedMeshRenderer>() : null;
            if (!ClubPoints(smr, out int bone, out head)) return false;
            grip = smr.bones[bone];
            return true;
        }

        /// <summary>
        /// Where the spot on the club that faced the ball at address is now, in golfer-root space. Tracked on the club
        /// rather than re-measured, since the clip turns the face and its foremost point moves to the toe or heel.
        /// </summary>
        private static Vector3 FacePoint(Transform seat, Vector3 faceOnClub, Transform root) => root.InverseTransformPoint(seat.TransformPoint(faceOnClub));

        /// <summary>Front-centre of the club face and the sole height, in golfer-root space (target is root -X).</summary>
        private static bool ClubFace(Transform body, string model, Transform root, out Vector3 face, out float sole)
        {
            face = default;
            sole = 0f;
            if (!ClubHead(body, model, out Transform grip, out List<Vector3> head)) return false;
            List<Vector3> pts = head.Select(p => root.InverseTransformPoint(grip.TransformPoint(p))).ToList();
            float minX = pts.Min(p => p.x);
            List<Vector3> front = pts.Where(p => p.x < minX + 0.01f).ToList();
            face = new Vector3(minX, front.Average(p => p.y), front.Average(p => p.z));
            sole = pts.Min(p => p.y);
            return true;
        }

        // ---------------------------------------------------------------- Verify

        public static List<string> Verify()
        {
            var problems = new List<string>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProShopBaker.CalebPrefabPath);
            var look = prefab != null ? prefab.GetComponent<GolferAppearance>() : null;
            if (look == null)
            {
                problems.Add("Caleb_Golfer.prefab has no GolferAppearance.");
                return problems;
            }
            for (int body = 0; body < Names.Length; body++)
            {
                string name = Names[body];
                if (body >= look.BodyCount)
                {
                    problems.Add($"{name} has no body.");
                    continue;
                }
                List<Renderer> parts = body == 0 ? look.Parts : look.Extra[body - 1].Parts;
                var have = new HashSet<string>(parts.Where(r => r != null).Select(r => r.name));
                foreach (string part in SlotParts.Concat(GolferAppearance.HeadPieces.Select(p => p + name)).Concat(Names.Select(n => GolferAppearance.HairPrefix + n)))
                {
                    if (!have.Contains(part)) problems.Add($"{name} is missing {part}.");
                }
                Material skin = body == 0 ? look.Skin : look.Extra[body - 1].Skin;
                if (!look.Tones.Any(t => t.Material != null && t.Material == skin)) problems.Add($"{name}'s skin has no baked tone (Puttshack > Characters > Bake Hair Styles).");
                Renderer ownHair = parts.FirstOrDefault(r => r != null && r.name == GolferAppearance.HairPrefix + name);
                if (ownHair != null && !look.Tones.Any(t => t.Material != null && t.Material == ownHair.sharedMaterial)) problems.Add($"{name}'s hair has no baked tone.");
                Animator anim = look.AnimatorOf(body);
                if (anim == null || anim.avatar == null || !anim.avatar.isHuman || !anim.avatar.isValid) problems.Add($"{name} has no valid Humanoid avatar.");
                if (anim != null && anim.runtimeAnimatorController == null) problems.Add($"{name} has no controller.");
                GolferHandIK ik = anim != null ? anim.GetComponent<GolferHandIK>() : null;
                foreach (string model in new[] { "Driver", "Iron", "Wedge", "Putter" })
                {
                    if (ik == null || !ik.Grips.Any(g => g.Model == model && g.Seat != null)) problems.Add($"{name} has no right-hand grip on the {model}.");
                    else if (!ik.Grips.Any(g => g.Model == model && g.Contact > 0f && !string.IsNullOrEmpty(g.Swing))) problems.Add($"{name} has no strike with the {model}.");
                }
                foreach (Renderer r in parts)
                {
                    if (r == null) continue;
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null || !m.shader.name.StartsWith("Puttshack/Character/", StringComparison.Ordinal)) problems.Add($"{name}/{r.name} uses {(m == null ? "no material" : m.shader.name)}.");
                    }
                    if (r.name.StartsWith(GolferAppearance.ClubPrefix, StringComparison.Ordinal))
                    {
                        string seat = SeatPrefix + r.name.Substring(GolferAppearance.ClubPrefix.Length);
                        if (!(r is SkinnedMeshRenderer smr) || !smr.bones.Any(b => b != null && b.name == seat)) problems.Add($"{name}/{r.name} is not seated in the hand ({seat}).");
                    }
                }
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ProShopBaker.ControllerPath);
            Dictionary<string, AnimatorState> states = controller != null
                ? controller.layers[0].stateMachine.states.ToDictionary(s => s.state.name, s => s.state)
                : new Dictionary<string, AnimatorState>();
            foreach (string s in RequiredStates)
            {
                if (!states.TryGetValue(s, out AnimatorState state)) problems.Add($"Controller has no {s} state.");
                else if (!(state.motion is AnimationClip clip) || !clip.humanMotion) problems.Add($"{s} has no Humanoid clip.");
            }
            var motion = prefab.GetComponent<GolferMotion>();
            if (motion == null) problems.Add("Caleb_Golfer.prefab has no GolferMotion.");
            else
            {
                foreach (string model in new[] { "Driver", "Iron", "Wedge", "Putter" })
                {
                    if (!motion.TryGet(model, out GolferMotion.ClubMotion m)) problems.Add($"No motion for the {model}.");
                    else if (m.Contact == null || m.Contact.Length < Names.Length || m.Contact.Any(c => c <= 0.2f) || m.Ball == null || m.Ball.Length < Names.Length)
                        problems.Add($"{model} motion is not measured (contact {string.Join(" / ", (m.Contact ?? new float[0]).Select(c => c.ToString("0.00")))} s).");
                }
            }
            return problems;
        }
    }

    /// <summary>Character textures: normal maps, linear masks, coverage-preserving hair cards, 2k cap.</summary>
    internal sealed class GolferTextureRules : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(GolferCharacterBaker.CharacterDir + "/", StringComparison.Ordinal) || !assetPath.Contains("/Textures/")) return;
            var importer = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            bool normal = file.EndsWith("_Normal", StringComparison.Ordinal);
            bool mask = file.EndsWith("_Mask", StringComparison.Ordinal) && file != "Logo_Mask";
            bool cards = file.EndsWith("_Hair", StringComparison.Ordinal) || file.EndsWith("_Brow", StringComparison.Ordinal) || file.EndsWith("_Lash", StringComparison.Ordinal);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && !mask;
            importer.alphaSource = normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = cards || file == "Logo_Mask";
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = cards;
            importer.alphaTestReferenceValue = 0.5f;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.anisoLevel = 4;
            importer.wrapMode = cards || file.Contains("_Eye_") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        }
    }
}
