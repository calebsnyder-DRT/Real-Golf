using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Photographs a model into a transparent icon inside an isolated preview scene. Each icon is rendered over black and
    /// over white; the difference gives exact coverage, so edges stay clean whatever the pipeline does with alpha.
    /// </summary>
    public static class ProShopIconRenderer
    {
        public sealed class Shot
        {
            public Vector3 ViewDirection = new Vector3(0f, -0.08f, -1f);
            public float Margin = 0.08f;
            public bool Silhouette;
            public bool DoubleSided = true;
            /// <summary>Material roles drawn invisible (e.g. the bare legs baked into the shorts mesh).</summary>
            public string[] HiddenRoles = new string[0];
            /// <summary>When positive, frames only the geometry within this many metres of the subject's lowest point.</summary>
            public float FocusHeight;
        }

        private const int Supersample = 2;

        /// <summary>Renders and destroys <paramref name="subject"/> (a root object, not an asset).</summary>
        public static Texture2D Render(GameObject subject, int size, Shot shot)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<Object>();
            try
            {
                SceneManager.MoveGameObjectToScene(subject, scene);
                Restyle(subject, shot, temporary);

                AddLight(scene, "Key", Quaternion.Euler(28f, 150f, 0f), new Color(1f, 0.96f, 0.9f), 1.25f);
                AddLight(scene, "Fill", Quaternion.Euler(10f, 225f, 0f), new Color(0.85f, 0.9f, 1f), 0.55f);
                AddLight(scene, "Rim", Quaternion.Euler(20f, -20f, 0f), Color.white, 0.6f);

                var camGo = new GameObject("Icon Camera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.scene = scene;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
                Frame(cam, subject, shot);

                int px = size * Supersample;
                var rt = new RenderTexture(px, px, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                temporary.Add(rt);
                // The first render in a fresh preview scene runs before its ambient probe settles and comes out tinted.
                Capture(cam, rt, Color.black);
                Color[] black = Capture(cam, rt, Color.black);
                Color[] white = Capture(cam, rt, Color.white);
                return Compose(black, white, px, size);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (Object o in temporary)
                {
                    if (o is RenderTexture r) r.Release();
                    Object.DestroyImmediate(o);
                }
            }
        }

        private static void AddLight(Scene scene, string name, Quaternion rotation, Color color, float intensity)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = rotation;
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        private static void Restyle(GameObject subject, Shot shot, List<Object> temporary)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material white = null, hidden = null;
            var copies = new Dictionary<Material, Material>();
            foreach (Renderer r in subject.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string role = Runtime.Shop.CosmeticPaint.RoleOf(mats[i]);
                    if (System.Array.IndexOf(shot.HiddenRoles, role) >= 0)
                    {
                        if (hidden == null)
                        {
                            hidden = new Material(unlit) { name = "Hidden" };
                            ProShopImportRules.MakeTransparent(hidden, new Color(0, 0, 0, 0));
                            temporary.Add(hidden);
                        }
                        mats[i] = hidden;
                    }
                    else if (shot.Silhouette)
                    {
                        if (white == null)
                        {
                            white = new Material(unlit) { name = "Silhouette" };
                            white.SetColor("_BaseColor", Color.white);
                            white.SetFloat("_Cull", 0f);
                            temporary.Add(white);
                        }
                        mats[i] = white;
                    }
                    else if (shot.DoubleSided && mats[i] != null && mats[i].shader == lit)
                    {
                        if (!copies.TryGetValue(mats[i], out Material copy))
                        {
                            copy = new Material(mats[i]) { name = mats[i].name };
                            copy.SetFloat("_Cull", 0f);
                            copy.doubleSidedGI = true;
                            copies[mats[i]] = copy;
                            temporary.Add(copy);
                        }
                        mats[i] = copy;
                    }
                }
                r.sharedMaterials = mats;
            }
        }

        private static void Frame(Camera cam, GameObject subject, Shot shot)
        {
            Vector3 dir = shot.ViewDirection.normalized;
            cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            Bounds bounds = shot.FocusHeight > 0f ? LowerBounds(subject, shot.FocusHeight) : WorldBounds(subject);
            Vector3 right = cam.transform.right, up = cam.transform.up;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 c in Corners(bounds))
            {
                Vector3 d = c - bounds.center;
                float x = Vector3.Dot(d, right), y = Vector3.Dot(d, up);
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            Vector3 center = bounds.center + right * (minX + maxX) * 0.5f + up * (minY + maxY) * 0.5f;
            float half = Mathf.Max(maxX - minX, maxY - minY) * 0.5f * (1f + shot.Margin * 2f);
            cam.orthographicSize = Mathf.Max(0.01f, half);
            cam.transform.position = center - dir * (bounds.extents.magnitude + 5f);
        }

        public static Bounds WorldBounds(GameObject go)
        {
            bool any = false;
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds rb = r is SkinnedMeshRenderer smr && smr.sharedMesh != null ? smr.bounds : r.bounds;
                if (!any) b = rb;
                else b.Encapsulate(rb);
                any = true;
            }
            return b;
        }

        private static Bounds LowerBounds(GameObject go, float height)
        {
            float floor = WorldBounds(go).min.y;
            bool any = false;
            var b = new Bounds();
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 w = m.MultiplyPoint3x4(v);
                    if (w.y > floor + height) continue;
                    if (!any) b = new Bounds(w, Vector3.zero);
                    else b.Encapsulate(w);
                    any = true;
                }
            }
            return any ? b : WorldBounds(go);
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
            {
                yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            }
        }

        private static Color[] Capture(Camera cam, RenderTexture rt, Color background)
        {
            cam.backgroundColor = background;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            Color[] pixels = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return pixels;
        }

        /// <summary>Recovers coverage from the black/white pair, un-premultiplies, then box-filters down by the supersample.</summary>
        private static Texture2D Compose(Color[] black, Color[] white, int px, int size)
        {
            var result = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            var output = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            int i = (y * Supersample + sy) * px + x * Supersample + sx;
                            Color k = black[i], w = white[i];
                            float alpha = Mathf.Clamp01(1f - ((w.r - k.r) + (w.g - k.g) + (w.b - k.b)) / 3f);
                            r += k.r;
                            g += k.g;
                            b += k.b;
                            a += alpha;
                        }
                    }
                    float n = Supersample * Supersample;
                    a /= n;
                    if (a <= 0.001f)
                    {
                        output[y * size + x] = new Color(0, 0, 0, 0);
                        continue;
                    }
                    Color linear = new Color(Mathf.Clamp01(r / n / a), Mathf.Clamp01(g / n / a), Mathf.Clamp01(b / n / a), 1f);
                    Color srgb = QualitySettings.activeColorSpace == ColorSpace.Linear ? linear.gamma : linear;
                    srgb.a = a;
                    output[y * size + x] = srgb;
                }
            }
            result.SetPixels(output);
            result.Apply();
            return result;
        }
    }
}
