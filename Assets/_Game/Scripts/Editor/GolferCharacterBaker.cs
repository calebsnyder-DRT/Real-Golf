using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Bakes the stylized golfers (ArtSource/Blender/scripts/pp_stylized_build.py, pp_stylized_rig.py) and their Mixamo
    /// clips: copies the staged exports from ArtSource/Exports/Characters and the clips the controller uses from
    /// ArtSource/Mixamo/YBot_TPose into Art/Characters, builds the skin / face / garment materials on Puttshack/Character/Lit,
    /// remaps the FBX materials onto them, bakes a tinted variant of every garment base for each catalogue colour, runs the
    /// Pro Shop bake (which rebuilds Caleb_Golfer.prefab), points the shared controller's states at the Humanoid clips, then
    /// adds Maya's body, the per-club seats in the lead hand and the <see cref="GolferMotion"/> table to that prefab. Each
    /// club is seated once at address (sole on the ground) and rides the hand from there; impact times are per clip.
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

        public static readonly string[] Names = { "Caleb", "Maya" };
        public static readonly string[] SlotParts =
        {
            "Body_Arms", CosmeticCatalog.PartPolo, CosmeticCatalog.PartTee, CosmeticCatalog.PartChinos, CosmeticCatalog.PartShorts,
            CosmeticCatalog.PartSocksAnkle, CosmeticCatalog.PartSocksCrew, CosmeticCatalog.PartShoes, CosmeticCatalog.PartBelt,
            "Club_Driver", "Club_Iron", "Club_Wedge", "Club_Putter",
        };

        /// <summary>Controller state to Mixamo clip (ArtSource/Mixamo/YBot_TPose file name).</summary>
        public static readonly (string State, string Clip)[] StateClips =
        {
            ("Idle", "Idle_Standard"),
            ("Swing_Driver", "Golf_Drive_Swing"),
            ("Swing_Iron", "Golf_Drive_Medium"),
            ("Putt", "Golf_Putt_Swing"),
            ("React_Good", "React_FistPump"),
            ("React_Miss", "React_Disappointed"),
            ("Celebrate", "React_Victory"),
        };

        /// <summary>
        /// Seconds from the start of each swing clip to impact: the lead hand's low point on the downswing, read off the
        /// Y Bot clips (ArtSource/Mixamo/YBot_TPose).
        /// </summary>
        public static readonly Dictionary<string, float> ImpactTimes = new Dictionary<string, float>
        {
            { "Golf_Drive_Swing", 1.18f },
            { "Golf_Drive_Medium", 1.20f },
            { "Golf_Putt_Swing", 1.30f },
        };

        /// <summary>Address states hold the first frame of their swing, so the swing always starts from the stance.</summary>
        public static readonly (string State, string Swing)[] AddressStates =
        {
            ("Address_Driver", "Swing_Driver"), ("Address_Iron", "Swing_Iron"), ("Putt_Address", "Putt"),
        };

        private static readonly (string Model, string Address, string Swing)[] ClubStates =
        {
            ("Driver", "Address_Driver", "Swing_Driver"),
            ("Iron", "Address_Iron", "Swing_Iron"),
            ("Wedge", "Address_Iron", "Swing_Iron"),
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

        private static readonly string[] StagedSuffixes =
        {
            "_Modular.fbx", "_Head_Albedo.png", "_Head_Mask.png", "_Skin_Mask.png", "_Top_Mask.png", "_Bottom_Mask.png", "_skin.txt",
        };

        private static bool IsStagedCharacterFile(string file)
        {
            string name = Path.GetFileName(file);
            return Names.Any(n => StagedSuffixes.Any(s => name == n + s));
        }

        /// <summary>
        /// Copies changed character exports (FBX, head albedo, AO masks, skin colour) and the Mixamo clips the controller
        /// uses into the project, and drops imported clips no state uses any more; returns the number of files changed.
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
                    if (!IsStagedCharacterFile(file)) continue;
                    string rel = file.Substring(source.Length + 1).Replace('\\', '/');
                    if (Copy(file, Path.Combine(project, CharacterDir, rel))) copied++;
                }
            }

            string clipSource = Path.Combine(project, ClipStagingDir);
            string clipTarget = Path.Combine(project, ProShopImportRules.AnimationRoot);
            var used = new HashSet<string>();
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

        /// <summary>Median face skin from the head projection (sRGB), so the body matches the painted face.</summary>
        private static Color SkinColor(string name)
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>($"{CharacterDir}/{name}/Textures/{name}_skin.txt");
            float[] rgb = text != null
                ? text.text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray()
                : new float[0];
            return rgb.Length >= 3 ? new Color(rgb[0], rgb[1], rgb[2]) : new Color(0.75f, 0.55f, 0.42f);
        }

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
            m.SetFloat("_CavityStrength", 0f);
            m.SetTexture("_BaseMap", null);
            m.SetTexture("_BumpMap", null);
            m.SetTexture("_MaskMap", null);
            m.SetTexture("_DetailNormalMap", null);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.renderQueue = -1;
            return m;
        }

        /// <summary>Soft stylized skin: painted albedo, gentle wrap and a little red scatter, no pores.</summary>
        private static void Skin(Material m)
        {
            m.SetFloat("_Smoothness", 0.42f);
            m.SetColor("_ScatterColor", new Color(1f, 0.55f, 0.4f));
            m.SetFloat("_Wrap", 0.4f);
            m.SetFloat("_Transmission", 0.4f);
            m.SetColor("_TransmissionColor", new Color(1f, 0.38f, 0.24f));
            m.SetFloat("_SecondLobeSmoothness", 0.3f);
            m.SetFloat("_SecondLobeMix", 0.1f);
        }

        private static void Cloth(Material m, float smoothness, float sheen, float sheenRoughness)
        {
            m.SetFloat("_Smoothness", smoothness);
            m.SetColor("_SheenColor", new Color(sheen, sheen, sheen, 1f));
            m.SetFloat("_SheenRoughness", sheenRoughness);
            m.SetFloat("_Wrap", 0.3f);
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
                // The head is one painted scan: face, hair and cap share its albedo and AO.
                Texture2D head = Tex($"{name}/Textures/{name}_Head_Albedo");
                Texture2D headAo = Tex($"{name}/Textures/{name}_Head_Mask");
                Material face = Lit($"Mat_Face_{name}", "Skin");
                face.SetTexture("_BaseMap", head);
                face.SetTexture("_MaskMap", headAo);
                Skin(face);
                Keep(face);
                Material hair = Lit($"Mat_Hair_{name}", "Standard");
                hair.SetTexture("_BaseMap", head);
                hair.SetTexture("_MaskMap", headAo);
                hair.SetFloat("_Smoothness", 0.32f);
                Keep(hair);
                Material cap = Lit($"Mat_Cap_{name}", "Cloth");
                cap.SetTexture("_BaseMap", head);
                cap.SetTexture("_MaskMap", headAo);
                Cloth(cap, 0.22f, 0.18f, 0.5f);
                Keep(cap);

                Material skin = Lit($"Mat_Skin_{name}", "Skin");
                skin.SetColor("_BaseColor", SkinColor(name));
                skin.SetTexture("_MaskMap", Tex($"{name}/Textures/{name}_Skin_Mask"));
                Skin(skin);
                Keep(skin);

                // Garment bases: white with the body's own AO, tinted per catalogue colour by BakeVariants.
                foreach ((string role, float smooth) in new[] { ("Top", 0.26f), ("Bottom", 0.2f) })
                {
                    Material m = Lit($"Mat_{role}_{name}", "Cloth");
                    m.SetTexture("_MaskMap", Tex($"{name}/Textures/{name}_{role}_Mask"));
                    Cloth(m, smooth, 0.16f, 0.6f);
                    Keep(m);
                }
            }

            Material collar = Lit("Mat_Collar_Polo", "Cloth");
            Cloth(collar, 0.26f, 0.16f, 0.6f);
            Keep(collar);
            foreach (string sock in new[] { "Mat_Socks_Knit", "Mat_Socks_Band" })
            {
                Material m = Lit(sock, "Cloth");
                Cloth(m, 0.12f, 0.22f, 0.8f);
                Keep(m);
            }
            foreach ((string flat, float smooth, float metal) in new[]
            {
                ("Mat_Logo_Polo", 0.3f, 0f), ("Mat_Logo_Tee", 0.3f, 0f), ("Mat_Shoes_Golf", 0.5f, 0f), ("Mat_ShoeAccent_Golf", 0.42f, 0f),
                ("Mat_Sole_Golf", 0.25f, 0f), ("Mat_Belt_Leather", 0.45f, 0f), ("Mat_Buckle_Metal", 0.78f, 0.9f),
            })
            {
                Material m = Lit(flat, "Standard");
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", metal);
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

            // Materials of the retired realistic golfers (eyes, hair cards, cornea) go with them.
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
                GolferMotion motion = root.GetComponent<GolferMotion>() ?? root.AddComponent<GolferMotion>();
                motion.Appearance = look;
                motion.Clubs = MeasureClubs(look, clips, notes);
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

        private static List<GolferMotion.ClubMotion> MeasureClubs(GolferAppearance look, Dictionary<string, AnimationClip> clips, List<string> notes)
        {
            var result = new List<GolferMotion.ClubMotion>();
            foreach (var spec in ClubStates)
            {
                if (!clips.ContainsKey(spec.Address) || !clips.TryGetValue(spec.Swing, out AnimationClip swing))
                {
                    notes.Add($"No address/swing clips for the {spec.Model}.");
                    continue;
                }
                if (!ImpactTimes.TryGetValue(swing.name, out float contact)) notes.Add($"No impact time for {swing.name}.");
                var m = new GolferMotion.ClubMotion
                {
                    Model = spec.Model,
                    Address = spec.Address,
                    Swing = spec.Swing,
                    Contact = contact,
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
                        if (!ClubFace(bodyRoot.transform, spec.Model, look.transform, out Vector3 face, out float sole)) continue;
                        m.Ball[body] = new Vector3(face.x - BallRadius, 0f, face.z);
                        if (Mathf.Abs(sole) > 0.02f) notes.Add($"{BodyName(body)}'s {spec.Model} sole sits {sole:0.000} m off the ground at address.");
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
                foreach (string part in SlotParts.Append("Head_" + name))
                {
                    if (!have.Contains(part)) problems.Add($"{name} is missing {part}.");
                }
                Animator anim = look.AnimatorOf(body);
                if (anim == null || anim.avatar == null || !anim.avatar.isHuman || !anim.avatar.isValid) problems.Add($"{name} has no valid Humanoid avatar.");
                if (anim != null && anim.runtimeAnimatorController == null) problems.Add($"{name} has no controller.");
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
                    else if (m.Contact <= 0.2f || m.Ball == null || m.Ball.Length < Names.Length)
                        problems.Add($"{model} motion is not measured (contact {m.Contact:0.00} s).");
                }
            }
            return problems;
        }
    }

    /// <summary>Character textures: linear AO masks, sRGB painted albedo, 2k cap.</summary>
    internal sealed class GolferTextureRules : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(GolferCharacterBaker.CharacterDir + "/", StringComparison.Ordinal) || !assetPath.Contains("/Textures/")) return;
            var importer = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            bool mask = file.EndsWith("_Mask", StringComparison.Ordinal);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !mask;
            importer.alphaSource = mask ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.anisoLevel = 4;
            importer.wrapMode = TextureWrapMode.Clamp;
        }
    }
}
