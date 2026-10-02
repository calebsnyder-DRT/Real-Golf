using System;
using System.IO;
using System.Linq;
using Puttshack.Content;
using Puttshack.Runtime.Shop;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Look-dev stills of the golfers under the Pro Shop lighting (face close-ups and full body), written to
    /// ArtSource/Review/Characters. Opens the Pro Shop preview scene, so Main.unity is never touched.
    /// </summary>
    public static class GolferReviewShots
    {
        public const string OutDir = "ArtSource/Review/Characters";

        [MenuItem("Puttshack/Characters/Render Review Stills", priority = 47)]
        public static void RenderMenu() => Render("latest");

        /// <summary>Batch-mode entry point; <c>-reviewTag name</c> suffixes the file names.</summary>
        public static void BatchRender()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-reviewTag");
            Render(i >= 0 && i + 1 < args.Length ? args[i + 1] : "latest");
        }

        public static void Render(string tag)
        {
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                RenderAll(tag);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
            }
        }

        private static void RenderAll(string tag)
        {
            EditorSceneManager.OpenScene(ProShopBaker.PreviewScenePath);
            var shop = Object.FindFirstObjectByType<ProShopController>();
            GolferAppearance golfer = shop != null ? shop.Golfer : null;
            if (golfer == null) throw new InvalidOperationException("The Pro Shop preview scene has no golfer.");
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), OutDir);
            Directory.CreateDirectory(dir);
            for (int c = 0; c < GolferAppearance.Characters.Length; c++)
            {
                string name = GolferAppearance.Characters[c];
                golfer.Apply(CosmeticCatalog.DefaultFor(c), c);
                Animator anim = golfer.ShownAnimator;
                GolferCharacterBaker.PoseState(anim, "Idle", 0.5f);
                Transform root = golfer.transform;
                Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
                Vector3 face = head.position + root.up * 0.035f + root.forward * 0.03f;
                using (new Frozen(anim.gameObject))
                {
                    Shoot(Orbit(root, face, 1.05f, 0f, 0f), face, 16f, 1200, 1200, Path.Combine(dir, $"{name}_face_front_{tag}.png"));
                    Shoot(Orbit(root, face, 1.05f, 32f, 0.02f), face, 16f, 1200, 1200, Path.Combine(dir, $"{name}_face_34_{tag}.png"));
                    Vector3 mid = root.position + root.up * 0.92f;
                    Shoot(Orbit(root, mid, 3.6f, 18f, 0.08f), mid, 30f, 1000, 1500, Path.Combine(dir, $"{name}_body_{tag}.png"));
                }
            }
            golfer.Apply(CosmeticCatalog.DefaultFor(0), 0);
        }

        /// <summary>
        /// Batch-mode pose strips: <c>-reviewDir path -reviewStates Swing_Driver,Putt -reviewFrames 8</c> samples each
        /// controller state's clip on both bodies with the club in hand, renders face-on stills and logs the hips,
        /// lead-hand grip and clubhead in golfer-root space for every frame.
        /// </summary>
        public static void BatchRenderStates()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Arg(string key, string fallback)
            {
                int i = Array.IndexOf(args, key);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }
            string dir = Arg("-reviewDir", Path.Combine(Path.GetDirectoryName(Application.dataPath), OutDir, "Poses"));
            string[] states = Arg("-reviewStates", "Swing_Driver").Split(',');
            int frames = int.Parse(Arg("-reviewFrames", "8"));
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                RenderStates(dir, states, frames);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
            }
        }

        private static void RenderStates(string dir, string[] states, int frames)
        {
            EditorSceneManager.OpenScene(ProShopBaker.PreviewScenePath);
            var shop = Object.FindFirstObjectByType<ProShopController>();
            GolferAppearance golfer = shop != null ? shop.Golfer : null;
            if (golfer == null) throw new InvalidOperationException("The Pro Shop preview scene has no golfer.");
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ProShopBaker.ControllerPath);
            var clips = controller.layers[0].stateMachine.states.ToDictionary(s => s.state.name, s => s.state.motion as AnimationClip);
            var motion = golfer.GetComponent<GolferMotion>();
            Directory.CreateDirectory(dir);
            Transform root = golfer.transform;
            for (int c = 0; c < GolferAppearance.Characters.Length; c++)
            {
                string name = GolferAppearance.Characters[c];
                golfer.Apply(CosmeticCatalog.DefaultFor(c), c);
                Animator anim = golfer.ShownAnimator;
                foreach (string state in states)
                {
                    if (!clips.TryGetValue(state, out AnimationClip clip) || clip == null) continue;
                    GolferMotion.ClubMotion club = motion != null ? motion.Clubs.FirstOrDefault(m => m.Swing == state || m.Address == state) : default;
                    golfer.Hold(club.Model);
                    var smr = club.Model != null ? golfer.ShownPart(GolferAppearance.ClubPrefix + club.Model) as SkinnedMeshRenderer : null;
                    AnimationClipSettings cs = AnimationUtility.GetAnimationClipSettings(clip);
                    var log = new System.Text.StringBuilder($"[Puttshack] Pose {name} {state} ({clip.name}, {clip.length:0.00} s, yaw {cs.orientationOffsetY}, feet {cs.heightFromFeet}):");
                    var times = Enumerable.Range(0, frames).Select(f => frames > 1 ? clip.length * f / (frames - 1) : 0f).ToList();
                    if (club.Swing == state && club.Contact > 0f) times.Add(club.Contact);
                    for (int f = 0; f < times.Count; f++)
                    {
                        float t = times[f];
                        GolferCharacterBaker.PoseState(anim, state, t);
                        Vector3 hips = root.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.Hips).position);
                        Vector3 grip = root.InverseTransformPoint(GolferCharacterBaker.GripPoint(anim, true));
                        string head = "";
                        if (smr != null)
                        {
                            Mesh baked = new Mesh();
                            smr.BakeMesh(baked, true);
                            Vector3 low = baked.vertices.Select(v => root.InverseTransformPoint(smr.transform.TransformPoint(v))).OrderBy(p => p.y).First();
                            Object.DestroyImmediate(baked);
                            head = $" head {low.x:0.00},{low.y:0.00},{low.z:0.00}";
                        }
                        float feet = Mathf.Min(anim.GetBoneTransform(HumanBodyBones.LeftToes).position.y, anim.GetBoneTransform(HumanBodyBones.RightToes).position.y) - root.position.y;
                        log.Append($"\n  t={t:0.00} hips {hips.x:0.00},{hips.y:0.00},{hips.z:0.00} toes {feet:0.00} grip {grip.x:0.00},{grip.y:0.00},{grip.z:0.00}{head}");
                        Vector3 mid = root.position + root.up * 0.9f;
                        using (new Frozen(anim.gameObject))
                        {
                            Shoot(Orbit(root, mid, 4.2f, 0f, 0.05f), mid, 34f, 360, 520, Path.Combine(dir, $"{name}_{state}_{f:00}.png"));
                            Shoot(Orbit(root, mid, 4.2f, 90f, 0.05f), mid, 34f, 360, 520, Path.Combine(dir, $"{name}_{state}_{f:00}_side.png"));
                        }
                    }
                    Debug.Log(log.ToString());
                }
                golfer.Hold(null);
            }
            golfer.Apply(CosmeticCatalog.DefaultFor(0), 0);
        }

        /// <summary>
        /// Skinned meshes are only re-skinned once per editor tick, so a sampled pose is rendered from static copies of
        /// the skinned meshes baked in that pose.
        /// </summary>
        private sealed class Frozen : IDisposable
        {
            private readonly System.Collections.Generic.List<SkinnedMeshRenderer> _hidden = new System.Collections.Generic.List<SkinnedMeshRenderer>();
            private readonly System.Collections.Generic.List<Object> _made = new System.Collections.Generic.List<Object>();

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

        private static Vector3 Orbit(Transform root, Vector3 target, float distance, float yaw, float rise)
        {
            Vector3 dir = Quaternion.AngleAxis(yaw, root.up) * root.forward;
            return target + (dir + root.up * rise) * distance;
        }

        /// <summary>Renders at 2x and box-filters down, so hair cards and lashes read as they do with in-game AA.</summary>
        public static void Shoot(Vector3 from, Vector3 at, float fov, int width, int height, string path)
        {
            const int ss = 2;
            var go = new GameObject("Review Camera");
            var rt = new RenderTexture(width * ss, height * ss, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D big = null, small = null;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 300f;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                big = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                big.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = previous;

                Color32[] src = big.GetPixels32();
                var dst = new Color32[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int r = 0, g = 0, b = 0;
                        for (int dy = 0; dy < ss; dy++)
                        {
                            for (int dx = 0; dx < ss; dx++)
                            {
                                Color32 p = src[(y * ss + dy) * rt.width + x * ss + dx];
                                r += p.r;
                                g += p.g;
                                b += p.b;
                            }
                        }
                        const int n = ss * ss;
                        dst[y * width + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                    }
                }
                small = new Texture2D(width, height, TextureFormat.RGBA32, false);
                small.SetPixels32(dst);
                File.WriteAllBytes(path, small.EncodeToPNG());
            }
            finally
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                if (big != null) Object.DestroyImmediate(big);
                if (small != null) Object.DestroyImmediate(small);
                Object.DestroyImmediate(go);
            }
        }
    }
}
