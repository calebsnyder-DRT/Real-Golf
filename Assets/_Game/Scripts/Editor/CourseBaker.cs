using System.Collections.Generic;
using System.IO;
using Puttshack.Content;
using Puttshack.Runtime;
using Puttshack.Runtime.Course;
using Puttshack.Runtime.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Bakes every hole of the authored course into editor assets (meshes, materials, one prefab per hole) and places
    /// them in Main.unity, so the course is visible and editable in the Scene view instead of generated at runtime.
    /// The baked content hash must match the <see cref="HoleDefinition"/> the solver uses.
    /// </summary>
    public static class CourseBaker
    {
        public const string BakedRoot = "Assets/_Game/Course/Baked";
        public const string MeshFolder = BakedRoot + "/Meshes";
        public const string MaterialFolder = BakedRoot + "/Materials";
        public const string ScenePath = "Assets/_Game/Scenes/Main.unity";
        private const string SkyTexturePath = "Assets/_Game/Resources/Models/Course/Sky/sky_partly_cloudy.hdr";

        [MenuItem("Puttshack/Bake Course", priority = 1)]
        public static void BakeCourseMenu()
        {
            List<GameObject> prefabs = BakeCourse();
            EditorUtility.DisplayDialog("Puttshack", $"Baked {prefabs.Count} hole prefab(s) into {BakedRoot}.", "OK");
        }

        [MenuItem("Puttshack/Build Main Scene", priority = 2)]
        public static void BuildMainSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildMainScene();
        }

        [MenuItem("Puttshack/Verify Baked Course", priority = 3)]
        public static void VerifyMenu()
        {
            List<string> problems = Verify();
            if (problems.Count == 0) EditorUtility.DisplayDialog("Puttshack", "Baked course matches the course definition.", "OK");
            else EditorUtility.DisplayDialog("Puttshack", "Stale bake:\n" + string.Join("\n", problems), "OK");
        }

        /// <summary>Entry point for <c>-executeMethod Puttshack.EditorTools.CourseBaker.BatchBuild</c>.</summary>
        public static void BatchBuild()
        {
            BuildMainScene();
            List<string> problems = Verify();
            if (problems.Count > 0) throw new System.Exception("Baked course verification failed: " + string.Join("; ", problems));
            Debug.Log("[Puttshack] Batch bake complete.");
        }

        public static List<GameObject> BakeCourse()
        {
            CourseDefinition course = GrayboxCourse.Create();
            EnsureFolder(MeshFolder);
            EnsureFolder(MaterialFolder);

            CourseVisualAssets assets = CourseVisualAssets.CreateDefault();
            PersistMaterials(assets);

            var prefabs = new List<GameObject>();
            foreach (HoleDefinition hole in course.Holes)
            {
                BakedHole built = CourseVisualBuilder.Build(hole, assets, null);
                try
                {
                    PersistMeshes(hole, built.gameObject);
                    string prefabPath = $"{BakedRoot}/{hole.Id}.prefab";
                    GameObject prefab = PrefabUtility.SaveAsPrefabAsset(built.gameObject, prefabPath, out bool ok);
                    if (!ok || prefab == null) throw new IOException($"Could not save {prefabPath}.");
                    prefabs.Add(prefab);
                }
                finally
                {
                    Object.DestroyImmediate(built.gameObject);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Puttshack] Baked {prefabs.Count} hole(s) for {course.Id} {course.Version} ({course.ContentHash.Substring(0, 12)}).");
            return prefabs;
        }

        public static void BuildMainScene()
        {
            List<GameObject> prefabs = BakeCourse();
            CourseDefinition course = GrayboxCourse.Create();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.sun = sun;

            ConfigureEnvironment();

            HoleDefinition first = course.Holes[0];
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1500f;
            cam.fieldOfView = 50f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<GolfCameraRig>();
            Vector3 tee = first.TeeM.ToUnity();
            Vector3 cup = first.CupM.ToUnity();
            Vector3 forward = (cup - tee);
            forward.y = 0;
            forward.Normalize();
            camGo.transform.position = tee - forward * 9f + Vector3.up * 4f;
            camGo.transform.rotation = Quaternion.LookRotation(cup + Vector3.up * 2f - camGo.transform.position, Vector3.up);

            var courseRoot = new GameObject("Course").transform;
            for (int i = 0; i < prefabs.Count; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], courseRoot);
                instance.SetActive(i == 0);
            }

            var host = new GameObject("Game Host").AddComponent<GameHost>();
            host.ShowEmulatorPanel = true;
            ProShopBaker.PlaceInScene(scene);
            CourseGolferBaker.PlaceInScene(scene);
            HudBaker.PlaceInScene(scene);

            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException($"Could not save {ScenePath}.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[Puttshack] Built {ScenePath} with {prefabs.Count} baked hole(s).");
        }

        [MenuItem("Puttshack/Capture Course Preview", priority = 20)]
        public static void CapturePreviewMenu() => CapturePreview(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CoursePreview"));

        /// <summary>Renders the baked Main scene from the tee and above the green. Batch: <c>-executeMethod ...CapturePreviewBatch</c>.</summary>
        public static void CapturePreviewBatch() => CapturePreview(System.Environment.GetEnvironmentVariable("PUTTSHACK_PREVIEW_DIR") ?? "/tmp/puttshack-verify");

        public static void CapturePreview(string outputDir)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(outputDir);
            HoleDefinition hole = GrayboxCourse.Create().Holes[0];
            Vector3 tee = hole.TeeM.ToUnity();
            Vector3 cup = hole.CupM.ToUnity();
            Vector3 fwd = cup - tee;
            fwd.y = 0;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            var shots = new (string name, Vector3 pos, Vector3 look)[]
            {
                ("tee", tee - fwd * 7.5f + Vector3.up * 2.6f, tee + fwd * 60f + Vector3.up * 2f),
                ("approach", Vector3.Lerp(tee, cup, 0.55f) - right * 22f + Vector3.up * 14f, cup),
                ("overview", Vector3.Lerp(tee, cup, 0.5f) + right * 90f + Vector3.up * 85f - fwd * 40f, Vector3.Lerp(tee, cup, 0.55f)),
            };

            var go = new GameObject("Preview Camera") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 50f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 1500f;
                var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
                cam.targetTexture = rt;
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                foreach (var shot in shots)
                {
                    go.transform.position = shot.pos;
                    go.transform.rotation = Quaternion.LookRotation(shot.look - shot.pos, Vector3.up);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    string file = Path.Combine(outputDir, $"course-{shot.name}.png");
                    File.WriteAllBytes(file, tex.EncodeToPNG());
                    Debug.Log($"[Puttshack] Preview written: {file}");
                }
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Returns one message per hole whose baked prefab is missing or does not match the definition.</summary>
        public static List<string> Verify()
        {
            var problems = new List<string>();
            CourseDefinition course = GrayboxCourse.Create();
            foreach (HoleDefinition hole in course.Holes)
            {
                string path = $"{BakedRoot}/{hole.Id}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                BakedHole baked = prefab != null ? prefab.GetComponent<BakedHole>() : null;
                if (baked == null) problems.Add($"{hole.Id}: no baked prefab at {path}.");
                else if (!baked.Matches(hole)) problems.Add($"{hole.Id}: baked hash {Short(baked.ContentHash)} != definition {Short(hole.ContentHash)}.");
            }
            return problems;
        }

        private static void ConfigureEnvironment()
        {
            var sky = AssetDatabase.LoadAssetAtPath<Texture>(SkyTexturePath);
            Shader panoramic = Shader.Find("Skybox/Panoramic");
            if (sky != null && panoramic != null)
            {
                string path = $"{MaterialFolder}/Sky.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(panoramic) { name = "Sky" };
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = panoramic;
                mat.SetTexture("_MainTex", sky);
                if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 1.1f);
                EditorUtility.SetDirty(mat);
                RenderSettings.skybox = mat;
                RenderSettings.ambientMode = AmbientMode.Skybox;
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.85f);
                RenderSettings.ambientEquatorColor = new Color(0.55f, 0.6f, 0.52f);
                RenderSettings.ambientGroundColor = new Color(0.3f, 0.32f, 0.25f);
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.84f, 0.88f);
            RenderSettings.fogStartDistance = 180f;
            RenderSettings.fogEndDistance = 900f;
        }

        /// <summary>Saves each generated material as an asset, updating an existing asset in place so references survive.</summary>
        private static void PersistMaterials(CourseVisualAssets assets)
        {
            assets.Ground = Persist(assets.Ground);
            assets.Water = Persist(assets.Water);
            assets.CartPath = Persist(assets.CartPath);
            assets.Trunk = Persist(assets.Trunk);
            assets.Canopy = Persist(assets.Canopy);
            assets.FlagPole = Persist(assets.FlagPole);
            assets.FlagCloth = Persist(assets.FlagCloth);
            assets.Cup = Persist(assets.Cup);
            assets.TeeMarker = Persist(assets.TeeMarker);
            assets.BoundaryStake = Persist(assets.BoundaryStake);
        }

        private static Material Persist(Material generated)
        {
            string path = $"{MaterialFolder}/{generated.name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            existing.shader = generated.shader;
            existing.CopyPropertiesFromMaterial(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void PersistMeshes(HoleDefinition hole, GameObject root)
        {
            var used = new HashSet<string>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || EditorUtility.IsPersistent(mesh)) continue;
                string baseName = $"{hole.Id}_{filter.gameObject.name.Replace(' ', '_').ToLowerInvariant()}";
                string name = baseName;
                for (int n = 2; used.Contains(name); n++) name = $"{baseName}_{n}";
                used.Add(name);

                string path = $"{MeshFolder}/{name}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(mesh, existing);
                    existing.name = name;
                    filter.sharedMesh = existing;
                    Object.DestroyImmediate(mesh);
                }
                else
                {
                    mesh.name = name;
                    AssetDatabase.CreateAsset(mesh, path);
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string Short(string hash) => string.IsNullOrEmpty(hash) ? "(none)" : hash.Substring(0, Mathf.Min(12, hash.Length));
    }
}
