using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime;
using Puttshack.Runtime.Shop;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Bakes the Pro Shop into editor assets: cosmetic materials, the dressed Caleb prefab, bag prefabs, item icons, the
    /// UI prefab and ProShop.prefab (Blender room + catalog displays + lights + camera markers + portrait studio), then
    /// places the prefab in Main.unity and writes the ProShop.unity preview scene. Nothing is generated at runtime.
    /// Menu: Puttshack > Pro Shop. Batch: -executeMethod Puttshack.EditorTools.ProShopBaker.BatchBake
    /// </summary>
    public static class ProShopBaker
    {
        public const string ArtRoot = ProShopImportRules.ProShopRoot;
        public const string MaterialDir = ArtRoot + "Materials/Cosmetic";
        public const string MeshDir = ArtRoot + "Meshes";
        public const string TextureDir = ArtRoot + "Textures";
        public const string IconDir = ProShopImportRules.ProShopRoot + "UI/Icons";
        public const string ArtAssetPath = ArtRoot + "CosmeticArt.asset";
        public const string VolumePath = ArtRoot + "ProShop_PostVolume.asset";
        public const string PrefabDir = "Assets/_Game/Prefabs/ProShop";
        public const string CalebPrefabPath = PrefabDir + "/Caleb_Golfer.prefab";
        public const string ShopPrefabPath = PrefabDir + "/ProShop.prefab";
        public const string ControllerPath = "Assets/_Game/Art/Characters/Caleb/Caleb_Shop.controller";
        public const string PreviewScenePath = "Assets/_Game/Scenes/ProShop.unity";
        public const string RoomModel = ProShopImportRules.ModelRoot + "ProShop_Room.fbx";
        public const string ShopObjectName = "Pro Shop";

        /// <summary>Off-course location of the shop in Main.unity: beyond the course camera's far plane and fog.</summary>
        public static readonly Vector3 ShopPosition = new Vector3(2500f, 0f, 0f);
        /// <summary>Turns the window wall toward the course sun (Euler 48, -35) so daylight falls through the glass.</summary>
        public const float ShopYaw = -150f;
        private const string MannequinSkinHex = "#E6DED0";

        private static readonly string[] BagStyles = { CosmeticCatalog.BagStand, CosmeticCatalog.BagCart, CosmeticCatalog.BagTour };
        private static readonly string[] ClubKinds = { "Driver", "Wood", "Iron", "Wedge", "Putter" };

        // ---------------------------------------------------------------- Menu / batch

        [MenuItem("Puttshack/Pro Shop/Bake Pro Shop", priority = 40)]
        public static void BakeMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            List<string> notes = BakeAll();
            PlaceInMainScene();
            WritePreviewScene();
            EditorUtility.DisplayDialog("Puttshack", notes.Count == 0 ? "Pro Shop baked." : "Pro Shop baked with notes:\n" + string.Join("\n", notes), "OK");
        }

        [MenuItem("Puttshack/Pro Shop/Open Preview Scene", priority = 41)]
        public static void OpenPreviewMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(PreviewScenePath)) WritePreviewScene();
            EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Puttshack/Pro Shop/Verify Pro Shop", priority = 42)]
        public static void VerifyMenu()
        {
            List<string> problems = Verify();
            EditorUtility.DisplayDialog("Puttshack", problems.Count == 0 ? "Pro Shop bake is complete." : string.Join("\n", problems), "OK");
        }

        public static void BatchBake()
        {
            List<string> notes = BakeAll();
            PlaceInMainScene();
            WritePreviewScene();
            foreach (string n in notes) Debug.LogWarning("[Puttshack] Pro Shop bake: " + n);
            List<string> problems = Verify();
            if (problems.Count > 0) throw new Exception("Pro Shop verification failed: " + string.Join("; ", problems));
            Debug.Log("[Puttshack] Pro Shop batch bake complete.");
        }

        // ---------------------------------------------------------------- Bake

        public static List<string> BakeAll()
        {
            var notes = new List<string>();
            foreach (string dir in new[] { MaterialDir, MeshDir, TextureDir, IconDir, PrefabDir, PrefabDir + "/Bags" }) EnsureFolder(dir);

            _propFront = RoomFront();
            CosmeticArt art = LoadOrCreate<CosmeticArt>(ArtAssetPath);
            BakeMaterials(art);
            AnimatorController controller = BakeAnimator();
            GameObject caleb = BakeCaleb(art, controller, notes);
            Dictionary<string, Mesh> posed = BakePosedMeshes(controller, notes);
            Dictionary<string, GameObject> bags = BakeBags(art, notes);
            BakeIcons(art, posed, bags, notes);

            GameObject ui = ProShopUIBuilder.Build();
            GameObject uiPrefab = SavePrefab(ui, ProShopUIBuilder.PrefabPath);

            BakeShopPrefab(art, caleb, controller, posed, bags, uiPrefab, notes);
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            return notes;
        }

        /// <summary>
        /// Where the Blender props' front (Blender -Y) points once imported, as a turn from +Z (Caleb's front). Read from
        /// the room's own markers: the portrait camera stands straight in front of Mark_Golfer.
        /// </summary>
        private static Quaternion _propFront = Quaternion.identity;

        private static Quaternion RoomFront()
        {
            var room = AssetDatabase.LoadAssetAtPath<GameObject>(RoomModel);
            Transform golfer = room != null ? FindDeep(room.transform, "Mark_Golfer") : null;
            Transform cam = room != null ? FindDeep(room.transform, "Cam_Portrait") : null;
            if (golfer == null || cam == null) return Quaternion.identity;
            Vector3 front = cam.position - golfer.position;
            front.y = 0f;
            return front.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(front.normalized, Vector3.up) : Quaternion.identity;
        }

        private static void BakeMaterials(CosmeticArt art)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            string templatePath = MaterialDir + "/Cosmetic_Template.mat";
            Material template = AssetDatabase.LoadAssetAtPath<Material>(templatePath);
            if (template == null)
            {
                template = new Material(lit);
                AssetDatabase.CreateAsset(template, templatePath);
            }
            template.shader = lit;
            template.SetFloat("_Smoothness", 0.25f);
            art.Template = template;

            var pairs = new SortedDictionary<string, (string Role, string Hex)>();
            foreach (CosmeticItem item in CosmeticCatalog.All)
            {
                foreach (KeyValuePair<string, string> kv in item.Colors) pairs[CosmeticArt.MaterialKey(kv.Key, kv.Value)] = (kv.Key, kv.Value);
            }
            pairs[CosmeticArt.MaterialKey("Skin", MannequinSkinHex)] = ("Skin", MannequinSkinHex);

            art.Materials.Clear();
            foreach (KeyValuePair<string, (string Role, string Hex)> pair in pairs)
            {
                string path = $"{MaterialDir}/{pair.Key}.mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(lit);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = lit;
                mat.SetColor("_BaseColor", CosmeticArt.ParseHex(pair.Value.Hex));
                (float smooth, float metal) = Finish(pair.Value.Role);
                mat.SetFloat("_Smoothness", smooth);
                mat.SetFloat("_Metallic", metal);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                art.Materials.Add(new CosmeticArt.MaterialEntry { Key = pair.Key, Material = mat });
            }
            art.Invalidate();
        }

        /// <summary>Smoothness/metallic by material role: matte fabric, satin leather, glossy shoes, bright metal.</summary>
        private static (float Smoothness, float Metallic) Finish(string role)
        {
            switch (role)
            {
                case "Buckle":
                case "Bag_Metal":
                    return (0.72f, 0.9f);
                case "Shoes":
                case "ShoeAccent":
                    return (0.5f, 0f);
                case "Sole":
                    return (0.3f, 0f);
                case "Belt":
                    return (0.45f, 0f);
                case "Logo":
                    return (0.35f, 0f);
                case "Skin":
                    return (0.32f, 0f);
                case "HeadCover":
                case "HeadCover_Accent":
                case "Socks":
                    return (0.12f, 0f);
                case "Bag_Body":
                case "Bag_Panel":
                case "Bag_Accent":
                case "Bag_Trim":
                    return (0.42f, 0f);
                default:
                    return (0.2f, 0f);
            }
        }

        private static AnimatorController BakeAnimator()
        {
            AnimationClip idle = CalebClip("Idle");
            if (idle == null) throw new InvalidOperationException($"No Idle clip in {ProShopImportRules.CalebModel}.");
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(ControllerPath, idle);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle") ?? sm.AddState("Idle");
            state.motion = idle;
            sm.defaultState = state;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimationClip CalebClip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(ProShopImportRules.CalebModel).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == name && !c.name.StartsWith("__preview__", StringComparison.Ordinal));

        private static GameObject BakeCaleb(CosmeticArt art, AnimatorController controller, List<string> notes)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ProShopImportRules.CalebModel);
            if (model == null) throw new InvalidOperationException($"Missing {ProShopImportRules.CalebModel}.");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                go.name = "Caleb_Golfer";
                Animator anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
                anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (anim.avatar == null || !anim.avatar.isHuman || !anim.avatar.isValid) notes.Add("Caleb avatar is not a valid Humanoid.");

                var look = go.AddComponent<GolferAppearance>();
                look.Art = art;
                look.Collect();
                foreach (Renderer r in look.Parts)
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                }
                look.MannequinHead = BuildMannequinHead(go.transform, art);
                look.MannequinHead.SetActive(false);
                look.Apply(CosmeticCatalog.DefaultFor(0), false);
                return SavePrefab(go, CalebPrefabPath, destroy: false);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Display head on the neck bone: the Blender mesh when exported, otherwise a smooth egg + neck.</summary>
        private static GameObject BuildMannequinHead(Transform rig, CosmeticArt art)
        {
            Transform neck = FindDeep(rig, "Neck");
            Transform head = FindDeep(rig, "Head");
            Material skin = art.MaterialFor("Skin", MannequinSkinHex);
            var holder = new GameObject("Mannequin Head");
            Vector3 neckPos = neck != null ? neck.position : new Vector3(0, 1.55f, 0);
            holder.transform.SetPositionAndRotation(neckPos, rig.rotation * Quaternion.Inverse(_propFront));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ProShopImportRules.ModelRoot + "Display_MannequinHead.fbx");
            if (model != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, holder.transform);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => skin).ToArray();
            }
            else
            {
                float headY = head != null ? head.position.y - neckPos.y : 0.1f;
                Primitive(PrimitiveType.Cylinder, "Neck", holder.transform, skin, new Vector3(0, 0.05f, 0), new Vector3(0.105f, 0.07f, 0.105f));
                Primitive(PrimitiveType.Sphere, "Head", holder.transform, skin, new Vector3(0, headY + 0.1f, 0.012f), new Vector3(0.2f, 0.25f, 0.215f));
            }
            holder.transform.SetParent(neck != null ? neck : rig, true);
            return holder;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat, Vector3 localPos, Vector3 scale)
        {
            GameObject p = GameObject.CreatePrimitive(type);
            p.name = name;
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.transform.SetParent(parent, false);
            p.transform.localPosition = localPos;
            p.transform.localScale = scale;
            p.GetComponent<Renderer>().sharedMaterial = mat;
            return p;
        }

        /// <summary>Caleb's parts frozen in the Idle pose (arms down) as mesh assets, for mannequins and icons.</summary>
        private static Dictionary<string, Mesh> BakePosedMeshes(AnimatorController controller, List<string> notes)
        {
            var result = new Dictionary<string, Mesh>();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ProShopImportRules.CalebModel);
            GameObject go = Object.Instantiate(model);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator anim = go.GetComponent<Animator>();
                if (!PoseIdle(go, anim, controller)) notes.Add("Could not pose Caleb in Idle; mannequins and icons use the rest pose.");
                Transform neck = FindDeep(go.transform, "Neck");
                _posedNeck = neck != null ? Matrix4x4.TRS(neck.position, neck.rotation, Vector3.one) : Matrix4x4.Translate(new Vector3(0f, 1.51f, 0.04f));
                foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.name == "Caleb_MaterialSwatches") continue;
                    var baked = new Mesh();
                    smr.BakeMesh(baked, true);
                    // Bake into root space so every posed part shares one origin (the feet).
                    Matrix4x4 toRoot = go.transform.worldToLocalMatrix * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    Vector3[] v = baked.vertices;
                    Vector3[] n = baked.normals;
                    for (int i = 0; i < v.Length; i++)
                    {
                        v[i] = toRoot.MultiplyPoint3x4(v[i]);
                        if (i < n.Length) n[i] = toRoot.MultiplyVector(n[i]).normalized;
                    }
                    baked.vertices = v;
                    baked.normals = n;
                    baked.RecalculateBounds();
                    if (baked.uv.Length == v.Length) baked.RecalculateTangents();
                    baked.name = "Posed_" + smr.name;
                    result[smr.name] = PersistMesh(baked, $"{MeshDir}/Posed_{smr.name}.asset");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            return result;
        }

        /// <summary>Neck bone of the Idle pose in root space, where display mannequins get their head.</summary>
        private static Matrix4x4 _posedNeck = Matrix4x4.Translate(new Vector3(0f, 1.51f, 0.04f));

        private static bool PoseIdle(GameObject go, Animator anim, AnimatorController controller)
        {
            Transform l = FindDeep(go.transform, "Wrist.L"), r = FindDeep(go.transform, "Wrist.R");
            bool ArmsDown() => l != null && r != null && Vector3.Distance(l.position, r.position) < 0.9f;
            AnimationClip idle = CalebClip("Idle");
            if (anim != null)
            {
                anim.runtimeAnimatorController = controller;
                anim.Rebind();
                anim.Update(0f);
                if (ArmsDown()) return true;
            }
            if (idle != null)
            {
                idle.SampleAnimation(go, 0f);
                if (ArmsDown()) return true;
            }
            return ArmsDown();
        }

        private static Mesh PersistMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        private static Dictionary<string, GameObject> BakeBags(CosmeticArt art, List<string> notes)
        {
            var bags = new Dictionary<string, GameObject>();
            foreach (string style in BagStyles)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ProShopImportRules.ModelRoot}{style}.fbx");
                if (model == null)
                {
                    notes.Add($"Missing {style}.fbx (Blender export); bag style not baked.");
                    continue;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                try
                {
                    go.name = style;
                    var look = go.AddComponent<BagAppearance>();
                    look.Style = style;
                    look.Collect();
                    CosmeticItem first = CosmeticCatalog.ForSlot(CosmeticSlot.Bag).First(b => b.Style == style);
                    look.Apply(first, art);
                    bags[style] = SavePrefab(go, $"{PrefabDir}/Bags/{style}.prefab", destroy: false);
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
            return bags;
        }

        // ---------------------------------------------------------------- Icons

        private static void BakeIcons(CosmeticArt art, Dictionary<string, Mesh> posed, Dictionary<string, GameObject> bags, List<string> notes)
        {
            art.Icons.Clear();
            art.CategoryIcons.Clear();
            var written = new List<(string Key, string Path, bool Category)>();
            const int size = 256;

            foreach (CosmeticItem item in CosmeticCatalog.All)
            {
                GameObject subject = IconSubject(item, art, posed, bags, out ProShopIconRenderer.Shot shot);
                if (subject == null) continue;
                Texture2D tex = ProShopIconRenderer.Render(subject, size, shot);
                if (item.Id == "belt.none") Fade(tex, 0.28f);
                written.Add((item.Id, WritePng(tex, item.Id), false));
            }
            foreach (ClubDefinition club in ClubCatalog.All)
            {
                GameObject subject = ClubSubject(BagDisplay.ClubKind(club.Id));
                if (subject == null) continue;
                written.Add(("club." + club.Id, WritePng(ProShopIconRenderer.Render(subject, size, ClubShot(0.17f)), "club." + club.Id), false));
            }

            var categories = new (string Key, Func<GameObject> Make, ProShopIconRenderer.Shot Shot)[]
            {
                ("Top", () => PosedSubject(posed, CosmeticCatalog.PartPolo), FrontShot()),
                ("Bottom", () => PosedSubject(posed, CosmeticCatalog.PartChinos), FrontShot("Skin")),
                ("Shoes", () => LoadModel("Display_Shoes_Pair") is GameObject s ? Instantiate(s) : PosedSubject(posed, CosmeticCatalog.PartShoes), ShoeShot()),
                ("Socks", () => LoadModel("Display_Socks_Pair") is GameObject k ? Instantiate(k) : PosedSubject(posed, CosmeticCatalog.PartSocksCrew), SockShot()),
                ("Bag", () => bags.TryGetValue(CosmeticCatalog.BagTour, out GameObject b) ? Instantiate(b) : null, BagShot()),
                ("Clubs", () => ClubSubject("Iron"), ClubShot(0.45f)),
            };
            foreach ((string key, Func<GameObject> make, ProShopIconRenderer.Shot shot) in categories)
            {
                GameObject subject = make();
                if (subject == null)
                {
                    notes.Add($"No model for the {key} category icon yet.");
                    continue;
                }
                shot.Silhouette = true;
                written.Add((key, WritePng(ProShopIconRenderer.Render(subject, 128, shot), "category." + key), true));
            }

            AssetDatabase.Refresh();
            foreach ((string key, string path, bool category) in written)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                var entry = new CosmeticArt.SpriteEntry { Key = key, Sprite = sprite };
                if (category) art.CategoryIcons.Add(entry);
                else art.Icons.Add(entry);
            }
            art.Invalidate();
        }

        private static GameObject IconSubject(CosmeticItem item, CosmeticArt art, Dictionary<string, Mesh> posed, Dictionary<string, GameObject> bags, out ProShopIconRenderer.Shot shot)
        {
            shot = FrontShot();
            GameObject subject;
            switch (item.Slot)
            {
                case CosmeticSlot.Top:
                    subject = PosedSubject(posed, item.Style);
                    break;
                case CosmeticSlot.Bottom:
                    subject = PosedSubject(posed, item.Style);
                    shot = FrontShot("Skin");
                    break;
                case CosmeticSlot.Shoes:
                {
                    GameObject display = LoadModel("Display_Shoes_Pair");
                    subject = display != null ? Instantiate(display) : PosedSubject(posed, item.Style);
                    shot = ShoeShot();
                    break;
                }
                case CosmeticSlot.Socks:
                {
                    GameObject display = LoadModel("Display_Socks_Pair");
                    subject = display != null ? Instantiate(display) : PosedSubject(posed, item.HasPart ? item.Style : CosmeticCatalog.PartSocksAnkle);
                    shot = SockShot();
                    break;
                }
                case CosmeticSlot.Belt:
                {
                    GameObject display = LoadModel("Display_Belt_Coiled");
                    subject = display != null ? Instantiate(display) : PosedSubject(posed, CosmeticCatalog.PartBelt);
                    shot = display != null ? BagShot() : new ProShopIconRenderer.Shot { ViewDirection = new Vector3(0f, -0.9f, -0.45f) };
                    item = item.HasPart ? item : CosmeticCatalog.Get("belt.black");
                    break;
                }
                default:
                    subject = bags.TryGetValue(item.Style, out GameObject bag) ? Instantiate(bag) : null;
                    shot = BagShot();
                    break;
            }
            if (subject == null) return null;
            foreach (Renderer r in subject.GetComponentsInChildren<Renderer>(true)) CosmeticPaint.Apply(r, item.Colors, art);
            return subject;
        }

        private static ProShopIconRenderer.Shot FrontShot(params string[] hidden) =>
            new ProShopIconRenderer.Shot { ViewDirection = new Vector3(0.18f, -0.1f, -1f), HiddenRoles = hidden };

        private static ProShopIconRenderer.Shot ShoeShot() =>
            new ProShopIconRenderer.Shot { ViewDirection = new Vector3(0.62f, -0.5f, -0.78f), Margin = 0.06f, HiddenRoles = new[] { "Socks" } };

        private static ProShopIconRenderer.Shot SockShot() => new ProShopIconRenderer.Shot { ViewDirection = new Vector3(0.6f, -0.35f, -0.8f), HiddenRoles = new[] { "Skin" } };

        private static ProShopIconRenderer.Shot BagShot() => new ProShopIconRenderer.Shot { ViewDirection = new Vector3(0.45f, -0.25f, -1f), Margin = 0.05f };

        /// <summary>Frames the head plus <paramref name="height"/> metres of shaft, as the reference club cards do.</summary>
        private static ProShopIconRenderer.Shot ClubShot(float height) =>
            new ProShopIconRenderer.Shot { ViewDirection = new Vector3(0.15f, -0.2f, -1f), Margin = 0.06f, FocusHeight = height };

        private static GameObject ClubSubject(string kind)
        {
            GameObject model = LoadModel($"Display_Club_{kind}");
            if (model == null) return null;
            var holder = new GameObject($"Club {kind}");
            GameObject club = Instantiate(model);
            club.transform.SetParent(holder.transform, false);
            StandClub(club.transform, Quaternion.Euler(0f, 0f, -30f));
            return holder;
        }

        private static GameObject PosedSubject(Dictionary<string, Mesh> posed, string part)
        {
            var calebModel = AssetDatabase.LoadAssetAtPath<GameObject>(ProShopImportRules.CalebModel);
            if (!posed.TryGetValue(part, out Mesh mesh)) return null;
            var root = new GameObject(part);
            var child = new GameObject(part);
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = child.AddComponent<MeshRenderer>();
            Renderer source = calebModel.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == part);
            mr.sharedMaterials = source != null ? source.sharedMaterials : new Material[mesh.subMeshCount];
            return root;
        }

        private static void Fade(Texture2D tex, float alpha)
        {
            Color[] px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                float grey = (px[i].r + px[i].g + px[i].b) / 3f;
                px[i] = new Color(grey, grey, grey, px[i].a * alpha);
            }
            tex.SetPixels(px);
            tex.Apply();
        }

        private static string WritePng(Texture2D tex, string name)
        {
            string path = $"{IconDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return path;
        }

        // ---------------------------------------------------------------- Shop prefab

        private sealed class Markers
        {
            public readonly Dictionary<string, Transform> ByName = new Dictionary<string, Transform>();
            public Quaternion Basis = Quaternion.identity;

            public Transform this[string name] => ByName.TryGetValue(name, out Transform t) ? t : null;

            public List<Transform> Series(string prefix) =>
                ByName.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();

            /// <summary>Unity-space facing of a Blender slot (its local -Y), measured against Mark_Golfer (rotZ 0, faces +Z).</summary>
            public Quaternion Facing(Transform marker) =>
                Quaternion.Euler(0f, (marker.rotation * Quaternion.Inverse(Basis)).eulerAngles.y, 0f);
        }

        private static void BakeShopPrefab(CosmeticArt art, GameObject calebPrefab, AnimatorController controller, Dictionary<string, Mesh> posed,
            Dictionary<string, GameObject> bags, GameObject uiPrefab, List<string> notes)
        {
            var root = new GameObject(ShopObjectName);
            try
            {
                var shop = root.AddComponent<ProShopController>();
                var rig = root.AddComponent<ProShopCameraRig>();
                shop.Art = art;
                shop.CameraRig = rig;

                Markers markers = BuildRoom(root.transform, notes);
                PopulateDisplays(root.transform, markers, art, posed, bags, notes);

                // Stage: golfer on the rug turntable, plinth with the bag (or the focused club).
                var stage = new GameObject("Stage").transform;
                stage.SetParent(root.transform, false);
                var turntable = new GameObject("Turntable").transform;
                turntable.SetParent(stage, false);
                turntable.position = markers["Mark_Golfer"].position;
                var golfer = (GameObject)PrefabUtility.InstantiatePrefab(calebPrefab, turntable);
                golfer.transform.localPosition = Vector3.zero;
                golfer.transform.localRotation = _propFront;
                shop.Golfer = golfer.GetComponent<GolferAppearance>();
                shop.Turntable = turntable;

                Transform standMark = markers["Mark_BagStand"];
                var plinth = new GameObject("Plinth Display").transform;
                plinth.SetParent(stage, false);
                plinth.SetPositionAndRotation(standMark.position, markers.Facing(standMark) * Quaternion.Euler(0f, -24f, 0f));
                var display = plinth.gameObject.AddComponent<BagDisplay>();
                display.Art = art;
                foreach (string style in BagStyles)
                {
                    if (!bags.TryGetValue(style, out GameObject bagPrefab)) continue;
                    var bag = (GameObject)PrefabUtility.InstantiatePrefab(bagPrefab, plinth);
                    bag.transform.localPosition = Vector3.zero;
                    bag.transform.localRotation = Quaternion.identity;
                    display.Styles.Add(bag.GetComponent<BagAppearance>());
                }
                foreach (string kind in ClubKinds)
                {
                    GameObject model = LoadModel("Display_Club_" + kind);
                    if (model == null) continue;
                    var club = new GameObject("Display_Club_" + kind).transform;
                    club.SetParent(plinth, false);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, club);
                    StandClub(inst.transform);
                    club.gameObject.SetActive(false);
                    display.Clubs.Add(club.gameObject);
                }
                display.ShowBag(CosmeticCatalog.DefaultFor(0).Bag);
                shop.Plinth = display;

                rig.Portrait = markers["Cam_Portrait"];
                rig.PortraitTarget = markers["Cam_Portrait_Target"];
                rig.Overview = markers["Cam_Overview"];
                rig.OverviewTarget = markers["Cam_Overview_Target"];
                rig.PortraitFov = 32f;
                rig.OverviewFov = 44f;

                BuildLighting(root.transform, markers);
                shop.Studio = BuildStudio(root.transform, calebPrefab);

                var ui = (GameObject)PrefabUtility.InstantiatePrefab(uiPrefab, root.transform);
                shop.View = ui.GetComponent<ProShopView>();
                SavePrefab(root, ShopPrefabPath, destroy: false);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Markers BuildRoom(Transform root, List<string> notes)
        {
            var markers = new Markers();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RoomModel);
            Transform roomRoot;
            if (model != null)
            {
                var room = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
                room.name = "Room";
                room.transform.localPosition = Vector3.zero;
                room.transform.localRotation = Quaternion.identity;
                foreach (Renderer r in room.GetComponentsInChildren<Renderer>(true))
                {
                    bool glass = r.name.StartsWith("Glass", StringComparison.Ordinal);
                    r.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = !glass;
                }
                GameObjectUtility.SetStaticEditorFlags(room, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                roomRoot = room.transform;
            }
            else
            {
                notes.Add($"Missing {RoomModel}; baked a marker-only stage.");
                roomRoot = new GameObject("Room (markers only)").transform;
                roomRoot.SetParent(root, false);
                // Blender (x, y, z) -> Unity (-x, z, -y); same values the room export uses.
                void Mark(string name, float x, float y, float z)
                {
                    var t = new GameObject(name).transform;
                    t.SetParent(roomRoot, false);
                    t.localPosition = new Vector3(-x, z, -y);
                }
                Mark("Mark_Golfer", 0, 0, 0.01f);
                Mark("Mark_BagStand", 1.35f, 0.45f, 0.45f);
                Mark("Cam_Portrait", 0, -4.65f, 1.2f);
                Mark("Cam_Portrait_Target", 0, 0, 0.78f);
                Mark("Cam_Overview", -4.1f, -7.7f, 2.45f);
                Mark("Cam_Overview_Target", 0.3f, 1.9f, 0.9f);
            }
            foreach (Transform t in roomRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Slot_", StringComparison.Ordinal) || t.name.StartsWith("Mark_", StringComparison.Ordinal) || t.name.StartsWith("Cam_", StringComparison.Ordinal))
                    markers.ByName[t.name] = t;
            }
            foreach (string required in new[] { "Mark_Golfer", "Mark_BagStand", "Cam_Portrait", "Cam_Portrait_Target", "Cam_Overview", "Cam_Overview_Target" })
            {
                if (markers[required] == null) throw new InvalidOperationException($"{RoomModel} has no {required} empty.");
            }
            markers.Basis = markers["Mark_Golfer"].rotation;
            return markers;
        }

        /// <summary>Stands a club head-down on the plinth with the shaft leaning back, like the reference's iron stand.</summary>
        private static void StandClub(Transform club) => StandClub(club, Quaternion.Euler(-12f, 0f, 8f));

        private static void StandClub(Transform club, Quaternion lean)
        {
            club.localPosition = Vector3.zero;
            club.localRotation = Quaternion.identity;
            Bounds b = ProShopIconRenderer.WorldBounds(club.gameObject);
            Vector3 size = b.size;
            // The club's long axis becomes vertical; the head (origin) sits on the plinth.
            Quaternion upright = size.y >= size.x && size.y >= size.z ? Quaternion.identity
                : size.x >= size.z ? Quaternion.Euler(0, 0, 90) : Quaternion.Euler(90, 0, 0);
            club.localRotation = lean * upright;
            b = ProShopIconRenderer.WorldBounds(club.gameObject);
            if (b.center.y < club.position.y) club.localRotation = lean * upright * Quaternion.Euler(180, 0, 0);
            b = ProShopIconRenderer.WorldBounds(club.gameObject);
            club.position += Vector3.up * (club.parent.position.y - b.min.y + 0.005f);
        }

        private static void PopulateDisplays(Transform root, Markers markers, CosmeticArt art, Dictionary<string, Mesh> posed, Dictionary<string, GameObject> bags, List<string> notes)
        {
            var displays = new GameObject("Displays").transform;
            displays.SetParent(root, false);

            List<CosmeticItem> tops = CosmeticCatalog.ForSlot(CosmeticSlot.Top);
            List<CosmeticItem> bottoms = CosmeticCatalog.ForSlot(CosmeticSlot.Bottom);
            List<CosmeticItem> shoes = CosmeticCatalog.ForSlot(CosmeticSlot.Shoes);
            List<CosmeticItem> socks = CosmeticCatalog.ForSlot(CosmeticSlot.Socks);
            List<CosmeticItem> shopBags = CosmeticCatalog.ForSlot(CosmeticSlot.Bag).Where(b => !b.Starter).ToList();
            // The hanging rail reads as a colour run like the reference: order tops by hue.
            tops = tops.OrderBy(t => Hue(t.Swatch)).ToList();

            Fill(displays, "Hanging", markers.Series("Slot_Hang_"), tops, i => i.Style == CosmeticCatalog.PartTee ? "Display_Tee_Hanger" : "Display_Polo_Hanger", markers, art, notes);
            Fill(displays, "Folded", markers.Series("Slot_Fold_"), bottoms, i => i.Style == CosmeticCatalog.PartShorts ? "Display_Shorts_Folded" : "Display_Chinos_Folded", markers, art, notes);
            Fill(displays, "Shoes", markers.Series("Slot_Shoe_"), shoes, _ => "Display_Shoes_Pair", markers, art, notes);
            Fill(displays, "Socks", markers.Series("Slot_Sock_"), socks, _ => "Display_Socks_Pair", markers, art, notes);

            var bagRoot = new GameObject("Bags").transform;
            bagRoot.SetParent(displays, false);
            List<Transform> bagSlots = markers.Series("Slot_Bag_");
            for (int i = 0; i < bagSlots.Count && shopBags.Count > 0; i++)
            {
                CosmeticItem item = shopBags[i % shopBags.Count];
                if (!bags.TryGetValue(item.Style, out GameObject prefab)) continue;
                var bag = (GameObject)PrefabUtility.InstantiatePrefab(prefab, bagRoot);
                bag.name = $"{item.Id} ({bagSlots[i].name})";
                bag.transform.SetPositionAndRotation(bagSlots[i].position, markers.Facing(bagSlots[i]));
                bag.AddComponent<CosmeticDisplay>().ItemId = item.Id;
                bag.GetComponent<BagAppearance>().Apply(item, art);
            }

            var mannequins = new GameObject("Mannequins").transform;
            mannequins.SetParent(displays, false);
            var looks = new[]
            {
                CosmeticCatalog.DefaultFor(5),
                new Loadout { Top = "top.polo.midnight", Bottom = "bottom.chinos.khaki", Socks = "socks.noshow.black", Shoes = "shoes.black.white", Belt = "belt.brown", Bag = "bag.tour.classic" },
            };
            List<Transform> mannequinSlots = markers.Series("Slot_Mannequin_");
            for (int i = 0; i < mannequinSlots.Count; i++)
            {
                GameObject m = BuildMannequin(posed, art, looks[i % looks.Length]);
                if (m == null) break;
                m.transform.SetParent(mannequins, false);
                m.transform.SetPositionAndRotation(mannequinSlots[i].position, markers.Facing(mannequinSlots[i]) * _propFront);
            }
        }

        private static void Fill(Transform parent, string group, List<Transform> slots, List<CosmeticItem> items, Func<CosmeticItem, string> modelFor, Markers markers, CosmeticArt art, List<string> notes)
        {
            if (slots.Count == 0 || items.Count == 0) return;
            var root = new GameObject(group).transform;
            root.SetParent(parent, false);
            var missing = new HashSet<string>();
            for (int i = 0; i < slots.Count; i++)
            {
                CosmeticItem item = items[i % items.Count];
                string modelName = modelFor(item);
                GameObject model = LoadModel(modelName);
                if (model == null)
                {
                    missing.Add(modelName);
                    continue;
                }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
                inst.name = $"{item.Id} ({slots[i].name})";
                inst.transform.SetPositionAndRotation(slots[i].position, markers.Facing(slots[i]));
                var d = inst.AddComponent<CosmeticDisplay>();
                d.ItemId = item.Id;
                d.Paint(art);
                GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic);
            }
            foreach (string m in missing) notes.Add($"Missing {m}.fbx; {group} slots left empty.");
        }

        /// <summary>Static display mannequin: Caleb's posed meshes, mannequin skin and display head, dressed in a catalog look.</summary>
        private static GameObject BuildMannequin(Dictionary<string, Mesh> posed, CosmeticArt art, Loadout look)
        {
            if (!posed.ContainsKey("Body_Arms")) return null;
            var calebModel = AssetDatabase.LoadAssetAtPath<GameObject>(ProShopImportRules.CalebModel);
            var root = new GameObject($"Mannequin ({look.Top})");
            var parts = new List<string> { "Body_Arms", CosmeticCatalog.PartShoes };
            var colors = new Dictionary<string, string> { ["Skin"] = MannequinSkinHex };
            foreach (CosmeticSlot slot in Loadout.Slots)
            {
                if (slot == CosmeticSlot.Bag) continue;
                CosmeticItem item = CosmeticCatalog.Get(look.Get(slot));
                if (item.HasPart) parts.Add(item.Style);
                foreach (KeyValuePair<string, string> kv in item.Colors) colors[kv.Key] = kv.Value;
            }
            foreach (string part in parts)
            {
                if (!posed.TryGetValue(part, out Mesh mesh)) continue;
                var child = new GameObject(part);
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = child.AddComponent<MeshRenderer>();
                Renderer source = calebModel.GetComponentsInChildren<Renderer>(true).First(r => r.name == part);
                mr.sharedMaterials = source.sharedMaterials;
                CosmeticPaint.Apply(mr, colors, art);
            }
            // Display head on the Idle-pose neck, upright like the live stand-in's.
            Material skin = art.MaterialFor("Skin", MannequinSkinHex);
            var head = new GameObject("Mannequin Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = _posedNeck.GetColumn(3);
            head.localRotation = Quaternion.Inverse(_propFront);
            GameObject headModel = LoadModel("Display_MannequinHead");
            if (headModel != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(headModel, head);
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => skin).ToArray();
            }
            else
            {
                Primitive(PrimitiveType.Cylinder, "Neck", head, skin, new Vector3(0, 0.05f, 0), new Vector3(0.105f, 0.07f, 0.105f));
                Primitive(PrimitiveType.Sphere, "Head", head, skin, new Vector3(0, 0.2f, 0.012f), new Vector3(0.2f, 0.25f, 0.215f));
            }
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic);
            return root;
        }

        private static void BuildLighting(Transform root, Markers markers)
        {
            var lights = new GameObject("Lighting").transform;
            lights.SetParent(root, false);
            Vector3 g = markers["Mark_Golfer"].position;
            Vector3 cam = markers["Cam_Portrait"].position;
            Vector3 toCam = cam - g;
            toCam.y = 0;
            toCam.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, toCam);

            Spot(lights, "Key (golfer)", g + toCam * 2.6f - right * 1.5f + Vector3.up * 3.4f, g + Vector3.up * 1.1f, new Color(1f, 0.9f, 0.78f), 26f, 9f, 42f, LightShadows.Soft);
            Spot(lights, "Fill (golfer)", g + toCam * 3.0f + right * 2.4f + Vector3.up * 2.2f, g + Vector3.up * 1.0f, new Color(0.88f, 0.93f, 1f), 8f, 9f, 55f, LightShadows.None);
            Spot(lights, "Rim (golfer)", g - toCam * 1.8f + right * 0.6f + Vector3.up * 3.2f, g + Vector3.up * 1.4f, new Color(1f, 0.95f, 0.85f), 14f, 7f, 45f, LightShadows.None);
            Spot(lights, "Plinth", markers["Mark_BagStand"].position + Vector3.up * 2.8f + toCam * 1.2f, markers["Mark_BagStand"].position + Vector3.up * 0.4f, new Color(1f, 0.92f, 0.8f), 10f, 6f, 38f, LightShadows.None);

            // Wall washes along the clothing wall, pointing into the rail and shelves.
            List<Transform> hang = markers.Series("Slot_Hang_");
            if (hang.Count > 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    Transform slot = hang[Mathf.Clamp(Mathf.RoundToInt((i + 0.5f) / 4f * hang.Count), 0, hang.Count - 1)];
                    Vector3 at = slot.position;
                    Vector3 facing = markers.Facing(slot) * _propFront * Vector3.forward;
                    Spot(lights, $"Wall Wash {i + 1}", at + facing * 1.4f + Vector3.up * 1.6f, at - Vector3.up * 0.4f, new Color(1f, 0.86f, 0.68f), 9f, 6f, 70f, LightShadows.None);
                }
            }

            var ambient = new GameObject("Room Bounce").AddComponent<Light>();
            ambient.transform.SetParent(lights, false);
            ambient.transform.position = g + Vector3.up * 3.2f + toCam * 1.5f;
            ambient.type = LightType.Point;
            ambient.color = new Color(1f, 0.88f, 0.74f);
            ambient.intensity = 3f;
            ambient.range = 12f;
            ambient.shadows = LightShadows.None;

            var probe = new GameObject("Reflection Probe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(lights, false);
            probe.transform.position = g + Vector3.up * 1.6f;
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.size = new Vector3(16f, 6f, 16f);
            probe.boxProjection = true;
            probe.resolution = 128;

            var volumeGo = new GameObject("Shop Post Volume");
            volumeGo.transform.SetParent(lights, false);
            volumeGo.transform.position = g + Vector3.up * 2f;
            var box = volumeGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(40f, 16f, 40f);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = false;
            volume.priority = 10f;
            volume.blendDistance = 0f;
            volume.sharedProfile = BakeVolumeProfile();
        }

        private static Light Spot(Transform parent, string name, Vector3 position, Vector3 target, Color color, float intensity, float range, float angle, LightShadows shadows)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.position = position;
            light.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.45f;
            light.shadows = shadows;
            light.shadowStrength = 0.75f;
            return light;
        }

        private static VolumeProfile BakeVolumeProfile()
        {
            if (File.Exists(VolumePath)) AssetDatabase.DeleteAsset(VolumePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumePath);
            T Add<T>() where T : VolumeComponent
            {
                T c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            Add<Tonemapping>().mode.Override(TonemappingMode.Neutral);
            var color = Add<ColorAdjustments>();
            color.postExposure.Override(0.15f);
            color.contrast.Override(8f);
            color.saturation.Override(6f);
            var white = Add<WhiteBalance>();
            white.temperature.Override(6f);
            var bloom = Add<Bloom>();
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            var vignette = Add<Vignette>();
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>Photo booth 30 m under the floor: its own subject, backdrop and short-range lights.</summary>
        private static PortraitStudio BuildStudio(Transform root, GameObject calebPrefab)
        {
            var studioGo = new GameObject("Portrait Studio");
            studioGo.transform.SetParent(root, false);
            studioGo.transform.localPosition = new Vector3(0f, -30f, 0f);
            var studio = studioGo.AddComponent<PortraitStudio>();

            var subject = (GameObject)PrefabUtility.InstantiatePrefab(calebPrefab, studioGo.transform);
            subject.transform.localPosition = Vector3.zero;
            subject.transform.localRotation = Quaternion.identity;
            studio.Subject = subject.GetComponent<GolferAppearance>();
            studio.SubjectAnimator = subject.GetComponent<Animator>();
            foreach (Renderer r in subject.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;

            Material backdrop = BackdropMaterial();
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Backdrop";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(studioGo.transform, false);
            quad.transform.localPosition = new Vector3(0f, 1.45f, -0.9f);
            quad.transform.localScale = new Vector3(3f, 2.2f, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = backdrop;
            quad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var camGo = new GameObject("Portrait Camera");
            camGo.transform.SetParent(studioGo.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.62f, 1.6f);
            camGo.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 1.55f, 0f) - camGo.transform.localPosition, Vector3.up);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Hex(0xE8DFCB);
            cam.allowHDR = false;
            cam.allowMSAA = false;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            studio.Camera = cam;

            Vector3 head = new Vector3(0f, 1.6f, 0f);
            StudioLight(studioGo.transform, "Key", new Vector3(-0.9f, 2.3f, 1.6f), head, new Color(1f, 0.92f, 0.82f), 7f);
            StudioLight(studioGo.transform, "Fill", new Vector3(1.2f, 1.7f, 1.4f), head, new Color(0.9f, 0.94f, 1f), 3f);
            StudioLight(studioGo.transform, "Rim", new Vector3(0.5f, 2.4f, -1.0f), head, Color.white, 5f);
            return studio;
        }

        private static void StudioLight(Transform parent, string name, Vector3 local, Vector3 target, Color color, float intensity)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = local;
            light.transform.localRotation = Quaternion.LookRotation(target - local, Vector3.up);
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = 5f;
            light.spotAngle = 60f;
            light.shadows = LightShadows.None;
        }

        private static Material BackdropMaterial()
        {
            string texPath = TextureDir + "/PortraitBackdrop.png";
            var tex = new Texture2D(8, 256, TextureFormat.RGBA32, false);
            Color top = Palette.Hex(0xF4EEE1), bottom = Palette.Hex(0xD9CCB0);
            for (int y = 0; y < 256; y++)
            {
                Color c = Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, y / 255f));
                for (int x = 0; x < 8; x++) tex.SetPixel(x, y, c);
            }
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            string matPath = TextureDir + "/PortraitBackdrop.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- Scenes

        /// <summary>Replaces the shop instance in the open scene. Called by Build Main Scene and by the shop bake.</summary>
        public static GameObject PlaceInScene(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShopPrefabPath);
            if (prefab == null) return null;
            foreach (GameObject old in scene.GetRootGameObjects().Where(g => g.GetComponent<ProShopController>() != null).ToList()) Object.DestroyImmediate(old);
            var shop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            shop.name = ShopObjectName;
            shop.transform.SetPositionAndRotation(ShopPosition, Quaternion.Euler(0f, ShopYaw, 0f));
            if (!scene.GetRootGameObjects().Any(g => g.GetComponentInChildren<EventSystem>(true) != null))
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                SceneManager.MoveGameObjectToScene(es, scene);
            }
            return shop;
        }

        public static void PlaceInMainScene()
        {
            Scene scene = EditorSceneManager.OpenScene(CourseBaker.ScenePath, OpenSceneMode.Single);
            PlaceInScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException($"Could not save {CourseBaker.ScenePath}.");
        }

        /// <summary>Stand-alone art scene (not in the build): the shop at the origin, lit by a copy of the course sun.</summary>
        public static void WritePreviewScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShopPrefabPath);
            if (prefab == null) return;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Sun (course sun, shop-relative)").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Inverse(Quaternion.Euler(0f, ShopYaw, 0f)) * Quaternion.Euler(48, -35, 0);
            RenderSettings.sun = sun;
            var sky = AssetDatabase.LoadAssetAtPath<Material>(CourseBaker.MaterialFolder + "/Sky.mat");
            if (sky != null)
            {
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = AmbientMode.Skybox;
            }
            var shop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var cam = new GameObject("Preview Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            Transform portrait = FindDeep(shop.transform, "Cam_Portrait");
            Transform target = FindDeep(shop.transform, "Cam_Portrait_Target");
            if (portrait != null && target != null)
            {
                cam.transform.SetPositionAndRotation(portrait.position, Quaternion.LookRotation(target.position - portrait.position, Vector3.up));
                cam.fieldOfView = 32f;
            }
            cam.nearClipPlane = 0.05f;
            cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            if (!EditorSceneManager.SaveScene(scene, PreviewScenePath)) throw new IOException($"Could not save {PreviewScenePath}.");
        }

        // ---------------------------------------------------------------- Verify

        public static List<string> Verify()
        {
            var problems = new List<string>();
            var art = AssetDatabase.LoadAssetAtPath<CosmeticArt>(ArtAssetPath);
            if (art == null) problems.Add($"No {ArtAssetPath}.");
            else
            {
                foreach (CosmeticItem item in CosmeticCatalog.All)
                {
                    foreach (KeyValuePair<string, string> kv in item.Colors)
                    {
                        if (!art.Materials.Any(m => m.Key == CosmeticArt.MaterialKey(kv.Key, kv.Value) && m.Material != null))
                            problems.Add($"No baked material for {item.Id} {kv.Key}.");
                    }
                }
            }
            var shop = AssetDatabase.LoadAssetAtPath<GameObject>(ShopPrefabPath);
            ProShopController controller = shop != null ? shop.GetComponent<ProShopController>() : null;
            if (controller == null) problems.Add($"No ProShopController in {ShopPrefabPath}.");
            else
            {
                if (controller.View == null) problems.Add("Shop has no UI view.");
                if (controller.Golfer == null) problems.Add("Shop has no golfer.");
                if (controller.Plinth == null) problems.Add("Shop has no plinth display.");
                if (controller.Studio == null) problems.Add("Shop has no portrait studio.");
                if (controller.CameraRig == null || controller.CameraRig.Portrait == null) problems.Add("Shop camera markers are missing.");
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ProShopImportRules.CalebModel);
            Animator anim = model != null ? model.GetComponent<Animator>() : null;
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman || !anim.avatar.isValid) problems.Add("Caleb_Modular.fbx is not imported as a valid Humanoid.");
            return problems;
        }

        // ---------------------------------------------------------------- Helpers

        private static GameObject LoadModel(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{ProShopImportRules.ModelRoot}{name}.fbx");

        /// <summary>Unpacked copy of a Blender prop turned to face +Z, the way the icon camera expects Caleb's parts.</summary>
        private static GameObject Instantiate(GameObject prefab)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Inverse(_propFront));
            return go;
        }

        private static float Hue(string hex)
        {
            Color.RGBToHSV(CosmeticArt.ParseHex(hex), out float h, out float s, out float v);
            // Neutrals (whites, greys, navy) gather at the end of the run.
            return s < 0.22f ? 2f + (1f - v) : h;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject SavePrefab(GameObject go, string path, bool destroy = true)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
            if (!ok || prefab == null) throw new IOException($"Could not save {path}.");
            if (destroy) Object.DestroyImmediate(go);
            return prefab;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
