using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime.Shop;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Player Setup's hair styles and tints on the golfer prefab. Each body's hair mesh is fitted to the other body and
    /// added there under its own name (Hair_Maya under Caleb's root, Hair_Caleb under Maya_Body), so either cut shows on
    /// either body: bone weights are remapped by bone name, head-weighted vertices are fitted from one head's box to the
    /// other's in head-bone bind space, and the rest ride their bone's bind space scaled by the bodies' width ratio. The
    /// fitted meshes are saved under Art/Characters/Hair. Then every skin and hair material's average albedo is measured at
    /// its mesh's UVs into <see cref="GolferAppearance.Tones"/>, which the runtime skin and hair tints are ratios against.
    /// </summary>
    public static class GolferLookBaker
    {
        public const string HairDir = GolferCharacterBaker.CharacterDir + "/Hair";
        private const string HeadBone = "Head";
        private const string BodyPart = "Body_Arms";
        /// <summary>Head-box vertices: those carried (almost) entirely by the head bone, so the neck does not stretch it.</summary>
        private const float HeadWeight = 0.9f;
        private const int SampleSize = 512;

        [MenuItem("Puttshack/Characters/Bake Hair Styles", priority = 47)]
        public static void BakeMenu()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ProShopBaker.CalebPrefabPath);
            List<string> notes;
            try
            {
                var look = root.GetComponent<GolferAppearance>();
                if (look == null) throw new InvalidOperationException("Caleb_Golfer.prefab has no GolferAppearance.");
                notes = Bake(look);
                look.Apply(CosmeticCatalog.DefaultFor(0), 0);
                PrefabUtility.SaveAsPrefabAsset(root, ProShopBaker.CalebPrefabPath, out bool ok);
                if (!ok) throw new IOException($"Could not save {ProShopBaker.CalebPrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            foreach (string n in notes) Debug.LogWarning("[Puttshack] Hair styles: " + n);
            Debug.Log("[Puttshack] Hair styles baked.");
        }

        /// <summary>Fits every hair to every other body, re-collects the parts and bakes the tones; returns problems.</summary>
        public static List<string> Bake(GolferAppearance look)
        {
            var notes = new List<string>();
            ProShopBaker.EnsureFolder(HairDir);
            int bodies = look.BodyCount;

            for (int body = 0; body < bodies; body++)
            {
                foreach (Renderer r in PartsOf(look, body).ToList())
                {
                    if (r != null && r.name.StartsWith(GolferAppearance.HairPrefix, StringComparison.Ordinal) && r.name != GolferAppearance.HairPrefix + NameOf(look, body))
                        Object.DestroyImmediate(r.gameObject);
                }
            }
            Recollect(look);

            for (int from = 0; from < bodies; from++)
            {
                for (int to = 0; to < bodies; to++)
                {
                    if (from == to) continue;
                    string fromName = NameOf(look, from), toName = NameOf(look, to);
                    SkinnedMeshRenderer hair = Part(look, from, GolferAppearance.HairPrefix + fromName);
                    SkinnedMeshRenderer ownHair = Part(look, to, GolferAppearance.HairPrefix + toName);
                    SkinnedMeshRenderer fromHead = Part(look, from, "Head_" + fromName), toHead = Part(look, to, "Head_" + toName);
                    SkinnedMeshRenderer fromBody = Part(look, from, BodyPart), toBody = Part(look, to, BodyPart);
                    if (hair == null || ownHair == null || fromHead == null || toHead == null || fromBody == null || toBody == null)
                    {
                        notes.Add($"Skipped {fromName}'s hair on {toName}: missing hair, head or body part.");
                        continue;
                    }
                    UnityEngine.Mesh fitted = Fit(hair, ownHair, fromHead, toHead, WidthRatio(fromBody.sharedMesh, toBody.sharedMesh));
                    fitted.name = $"Hair_{fromName}_On_{toName}";
                    UnityEngine.Mesh asset = SaveMesh(fitted, $"{HairDir}/{fitted.name}.asset");
                    AddHair(hair, ownHair, asset);
                }
            }
            Recollect(look);

            look.Tones.Clear();
            for (int body = 0; body < bodies; body++)
            {
                string name = NameOf(look, body);
                PlayerProfile native = ProfileCatalog.Native(body == 1 ? Gender.Female : Gender.Male);
                SkinnedMeshRenderer skinPart = Part(look, body, BodyPart);
                Material skin = body == 0 ? look.Skin : look.Extra[body - 1].Skin;
                if (skinPart != null && skin != null) AddTone(look, skin, skinPart.sharedMesh, false, native.Skin);
                else notes.Add($"No skin tone for {name}: missing {BodyPart} or skin material.");
                SkinnedMeshRenderer hairPart = Part(look, body, GolferAppearance.HairPrefix + name);
                if (hairPart != null) AddTone(look, hairPart.sharedMaterial, hairPart.sharedMesh, true, native.HairColor);
                else notes.Add($"No hair tone for {name}: missing {GolferAppearance.HairPrefix + name}.");
            }
            EditorUtility.SetDirty(look);
            return notes;
        }

        // ---------------------------------------------------------------- Bodies

        private static void Recollect(GolferAppearance look)
        {
            look.Collect();
            foreach (GolferAppearance.Body b in look.Extra.ToList())
            {
                if (b.Root != null) look.AddBody(b.Name, b.Root);
            }
        }

        private static string NameOf(GolferAppearance look, int body) => body == 0 ? GolferAppearance.Characters[0] : look.Extra[body - 1].Name;

        private static List<Renderer> PartsOf(GolferAppearance look, int body) => body == 0 ? look.Parts : look.Extra[body - 1].Parts;

        private static SkinnedMeshRenderer Part(GolferAppearance look, int body, string name) =>
            PartsOf(look, body).OfType<SkinnedMeshRenderer>().FirstOrDefault(r => r != null && r.name == name);

        /// <summary>The fitted hair as a hidden part beside the body's own hair, on the same bones and render settings.</summary>
        private static void AddHair(SkinnedMeshRenderer source, SkinnedMeshRenderer ownHair, UnityEngine.Mesh mesh)
        {
            var go = new GameObject(source.name) { layer = ownHair.gameObject.layer };
            Transform t = go.transform;
            t.SetParent(ownHair.transform.parent, false);
            t.localPosition = ownHair.transform.localPosition;
            t.localRotation = ownHair.transform.localRotation;
            t.localScale = ownHair.transform.localScale;
            t.SetSiblingIndex(ownHair.transform.GetSiblingIndex() + 1);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = ownHair.bones;
            smr.rootBone = ownHair.rootBone;
            smr.sharedMaterials = source.sharedMaterials;
            smr.shadowCastingMode = ownHair.shadowCastingMode;
            smr.receiveShadows = ownHair.receiveShadows;
            smr.updateWhenOffscreen = ownHair.updateWhenOffscreen;
            smr.quality = ownHair.quality;
            Bounds bounds = ownHair.localBounds;
            bounds.Encapsulate(source.localBounds);
            smr.localBounds = bounds;
            go.SetActive(false);
        }

        // ---------------------------------------------------------------- Fit

        /// <summary>
        /// <paramref name="hair"/> re-skinned onto <paramref name="ownHair"/>'s bones. Per bone weight, the vertex goes into
        /// that bone's bind space on the source body, is fitted there (head box to head box for the head bone, scaled by
        /// <paramref name="bodyScale"/> for the rest) and comes back out of the same bone's bind space on the target body.
        /// </summary>
        private static UnityEngine.Mesh Fit(SkinnedMeshRenderer hair, SkinnedMeshRenderer ownHair, SkinnedMeshRenderer fromHead, SkinnedMeshRenderer toHead, float bodyScale)
        {
            UnityEngine.Mesh src = hair.sharedMesh;
            Transform[] fromBones = hair.bones, toBones = ownHair.bones;
            Matrix4x4[] fromBind = src.bindposes;
            Matrix4x4[] toBind = ownHair.sharedMesh.bindposes;
            Matrix4x4[] toUnbind = toBind.Select(m => m.inverse).ToArray();
            var map = new int[fromBones.Length];
            for (int i = 0; i < fromBones.Length; i++)
            {
                string bone = fromBones[i] != null ? fromBones[i].name : null;
                map[i] = Array.FindIndex(toBones, b => b != null && b.name == bone);
                if (map[i] < 0) throw new InvalidOperationException($"{ownHair.name}'s rig has no bone '{bone}' for {hair.name}.");
            }
            int headBone = Array.FindIndex(fromBones, b => b != null && b.name == HeadBone);
            if (headBone < 0) throw new InvalidOperationException($"{hair.name} has no {HeadBone} bone.");
            Bounds fromBox = HeadBox(fromHead), toBox = HeadBox(toHead);
            Vector3 boxScale = new Vector3(toBox.size.x / fromBox.size.x, toBox.size.y / fromBox.size.y, toBox.size.z / fromBox.size.z);

            Vector3[] vertices = src.vertices;
            Vector3[] normals = src.normals;
            Vector4[] tangents = src.tangents;
            byte[] perVertex = src.GetBonesPerVertex().ToArray();
            BoneWeight1[] weights = src.GetAllBoneWeights().ToArray();
            var outV = new Vector3[vertices.Length];
            var outN = new Vector3[normals.Length];
            var outT = new Vector4[tangents.Length];
            var outW = new BoneWeight1[weights.Length];
            int k = 0;
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 p = Vector3.zero, n = Vector3.zero, t = Vector3.zero;
                for (int j = 0; j < perVertex[v]; j++, k++)
                {
                    BoneWeight1 w = weights[k];
                    int to = map[w.boneIndex];
                    Vector3 local = fromBind[w.boneIndex].MultiplyPoint3x4(vertices[v]);
                    local = w.boneIndex == headBone ? toBox.center + Vector3.Scale(local - fromBox.center, boxScale) : local * bodyScale;
                    p += w.weight * toUnbind[to].MultiplyPoint3x4(local);
                    if (normals.Length > 0) n += w.weight * toUnbind[to].MultiplyVector(fromBind[w.boneIndex].MultiplyVector(normals[v]));
                    if (tangents.Length > 0) t += w.weight * toUnbind[to].MultiplyVector(fromBind[w.boneIndex].MultiplyVector(tangents[v]));
                    outW[k] = new BoneWeight1 { boneIndex = to, weight = w.weight };
                }
                outV[v] = p;
                if (normals.Length > 0) outN[v] = n.normalized;
                if (tangents.Length > 0)
                {
                    Vector3 tn = t.normalized;
                    outT[v] = new Vector4(tn.x, tn.y, tn.z, tangents[v].w);
                }
            }

            var mesh = new UnityEngine.Mesh { indexFormat = src.indexFormat };
            mesh.vertices = outV;
            if (outN.Length > 0) mesh.normals = outN;
            if (outT.Length > 0) mesh.tangents = outT;
            var uvs = new List<Vector2>();
            for (int channel = 0; channel < 4; channel++)
            {
                if (!src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel)) continue;
                src.GetUVs(channel, uvs);
                mesh.SetUVs(channel, uvs);
            }
            if (src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color)) mesh.colors = src.colors;
            mesh.subMeshCount = src.subMeshCount;
            for (int s = 0; s < src.subMeshCount; s++) mesh.SetTriangles(src.GetTriangles(s), s, false);
            using (var bonesPerVertex = new NativeArray<byte>(perVertex, Allocator.Temp))
            using (var boneWeights = new NativeArray<BoneWeight1>(outW, Allocator.Temp))
            {
                mesh.SetBoneWeights(bonesPerVertex, boneWeights);
            }
            mesh.bindposes = toBind;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Box of the head-bone-weighted head vertices in head-bone bind space.</summary>
        private static Bounds HeadBox(SkinnedMeshRenderer head)
        {
            UnityEngine.Mesh mesh = head.sharedMesh;
            int bone = Array.FindIndex(head.bones, b => b != null && b.name == HeadBone);
            if (bone < 0) throw new InvalidOperationException($"{head.name} has no {HeadBone} bone.");
            Matrix4x4 bind = mesh.bindposes[bone];
            Vector3[] vertices = mesh.vertices;
            byte[] perVertex = mesh.GetBonesPerVertex().ToArray();
            BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();
            Bounds box = default;
            bool any = false;
            int k = 0;
            for (int v = 0; v < vertices.Length; v++)
            {
                float onHead = 0f;
                for (int j = 0; j < perVertex[v]; j++, k++)
                {
                    if (weights[k].boneIndex == bone) onHead += weights[k].weight;
                }
                if (onHead < HeadWeight) continue;
                Vector3 p = bind.MultiplyPoint3x4(vertices[v]);
                if (!any) box = new Bounds(p, Vector3.zero);
                else box.Encapsulate(p);
                any = true;
            }
            if (!any) throw new InvalidOperationException($"{head.name} has no vertices on the {HeadBone} bone.");
            return box;
        }

        /// <summary>Target over source body width, from the two horizontal (smaller) extents of the body meshes.</summary>
        private static float WidthRatio(UnityEngine.Mesh from, UnityEngine.Mesh to)
        {
            float[] a = { from.bounds.size.x, from.bounds.size.y, from.bounds.size.z };
            float[] b = { to.bounds.size.x, to.bounds.size.y, to.bounds.size.z };
            int tall = Array.IndexOf(a, a.Max());
            float sum = 0f;
            for (int i = 0; i < 3; i++)
            {
                if (i != tall) sum += b[i] / a[i];
            }
            return sum / 2f;
        }

        private static UnityEngine.Mesh SaveMesh(UnityEngine.Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ---------------------------------------------------------------- Tones

        private static void AddTone(GolferAppearance look, Material material, UnityEngine.Mesh mesh, bool alphaWeighted, string nativeId)
        {
            if (material == null || look.Tones.Exists(t => t.Material == material)) return;
            Color average = Average(material, mesh, alphaWeighted);
            look.Tones.Add(new GolferAppearance.Tone { Material = material, Average = average, NativeId = nativeId });
            string swatch = (ProfileCatalog.Find(ProfileCatalog.SkinTones, nativeId) ?? ProfileCatalog.Find(ProfileCatalog.HairColors, nativeId))?.Hex;
            Debug.Log($"[Puttshack] Hair styles: {material.name} averages #{ColorUtility.ToHtmlStringRGB(average.gamma)} (its swatch {nativeId} is {swatch}).");
        }

        /// <summary>
        /// Linear albedo the material shows on its mesh: the base map sampled at each triangle's corners and centre, weighted
        /// by triangle area (and texel alpha for hair cards), times the base colour.
        /// </summary>
        private static Color Average(Material material, UnityEngine.Mesh mesh, bool alphaWeighted)
        {
            Color tint = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor").linear : Color.white;
            Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
            if (texture == null || mesh == null) return tint;

            RenderTexture rt = RenderTexture.GetTemporary(SampleSize, SampleSize, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var read = new Texture2D(SampleSize, SampleSize, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Repeat };
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, SampleSize, SampleSize), 0, 0);
                read.Apply(false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;
            double r = 0, g = 0, b = 0, total = 0;
            void Sample(Vector2 at, double weight)
            {
                Color c = read.GetPixelBilinear(at.x, at.y);
                double w = weight * (alphaWeighted ? c.a : 1f);
                r += c.r * w;
                g += c.g * w;
                b += c.b * w;
                total += w;
            }
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int i0 = triangles[i], i1 = triangles[i + 1], i2 = triangles[i + 2];
                double area = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]).magnitude * 0.5;
                if (area <= 0) continue;
                Sample(uv[i0], area / 6);
                Sample(uv[i1], area / 6);
                Sample(uv[i2], area / 6);
                Sample((uv[i0] + uv[i1] + uv[i2]) / 3f, area / 2);
            }
            Object.DestroyImmediate(read);
            if (total <= 0) return tint;
            return new Color((float)(r / total) * tint.r, (float)(g / total) * tint.g, (float)(b / total) * tint.b, 1f);
        }
    }
}
