using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Puttshack.Runtime.Course
{
    /// <summary>Finishing detail: bunker lips, the cart path ribbon, cup and flag.</summary>
    public static partial class CourseVisualBuilder
    {
        /// <summary>
        /// Sod lip around each bunker: from the turf, the ground rises to a rounded crest just outside the sand line,
        /// overhangs it and drops to the sand. Drawn with the ground material; the bunker mask is authored per ring so the
        /// crest stays turf and the face below it turns to sand. Purely visual (the solver keeps the analytic bowl).
        /// </summary>
        private static Mesh BuildBunkerLips(HoleDefinition hole, ICollisionWorld world, SurfaceMasks masks)
        {
            if (masks.Bunkers.Count == 0) return null;
            var rings = new (float offset, float dy, float sdB)[]
            {
                (0.85f, -0.04f, 0.85f), (0.32f, 0.03f, 0.5f), (0.12f, 0.075f, 0.28f), (-0.03f, 0.065f, 0.06f),
                (-0.08f, -0.015f, -0.12f), (-0.42f, -0.045f, -0.42f),
            };
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var stripes = new List<Vector2>();
            var masksA = new List<Vector2>();
            var masksB = new List<Vector2>();
            var along = new List<Vector2>();
            var triangles = new List<int>();
            Color32 grass = SurfaceWeights(SurfaceId.LightRough);

            foreach (List<Vec2d> bunker in masks.Bunkers)
            {
                List<Vector2> ccw = ToCounterClockwise(bunker);
                int n = ccw.Count;
                int start = vertices.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector2 prev = ccw[(i + n - 1) % n], p = ccw[i], next = ccw[(i + 1) % n];
                    Vector2 e1 = (p - prev).normalized, e2 = (next - p).normalized;
                    Vector2 outward = (new Vector2(e1.y, -e1.x) + new Vector2(e2.y, -e2.x)).normalized;
                    foreach (var ring in rings)
                    {
                        Vector2 q = p + outward * ring.offset;
                        vertices.Add(new Vector3(q.x, (float)world.GroundHeight(q.x, q.y) + ring.dy, q.y));
                        colors.Add(grass);
                        stripes.Add(masks.Stripe(q.x, q.y));
                        masks.Sample(q.x, q.y, true, out Vector2 a, out Vector2 b);
                        a.y = ring.sdB;
                        masksA.Add(a);
                        masksB.Add(b);
                        along.Add(masks.Along);
                    }
                }
                int r = rings.Length;
                for (int i = 0; i < n; i++)
                {
                    int i1 = (i + 1) % n;
                    for (int k = 0; k < r - 1; k++)
                    {
                        int a = start + i * r + k, b = start + i1 * r + k;
                        int c = a + 1, d = b + 1;
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                        triangles.Add(b); triangles.Add(c); triangles.Add(d);
                    }
                }
            }

            var mesh = new Mesh { name = $"{hole.Id}_bunker_lips", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, stripes);
            mesh.SetUVs(1, masksA);
            mesh.SetUVs(2, masksB);
            mesh.SetUVs(3, along);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The hole's stretches of the course cart path (<see cref="CourseLayout.CartPath"/>) as a draped concrete ribbon
        /// with soft bevelled edges. UV = (across, -1..1 at the concrete edge; metres along the whole loop), vertex alpha =
        /// concrete (1) or the worn verge (0).
        /// </summary>
        private static Mesh BuildCartPathMesh(HoleDefinition hole, ICollisionWorld world, CourseTiling tiling, int index)
        {
            IReadOnlyList<Vec2d> loop = CourseLayout.CartPath;
            int n = loop.Count;
            var dist = new double[n + 1];
            for (int k = 0; k < n; k++)
            {
                Vec2d a = loop[k], b = loop[(k + 1) % n];
                dist[k + 1] = dist[k] + Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
            }

            float hw = (float)CourseLayout.CartPathHalfWidthM;
            var profile = new (float across, float dy, byte alpha)[]
            {
                (-hw - 0.32f, -0.03f, 0), (-hw - 0.06f, 0.03f, 150), (-hw, 0.045f, 255), (0f, 0.05f, 255), (hw, 0.045f, 255),
                (hw + 0.06f, 0.03f, 150), (hw + 0.32f, -0.03f, 0),
            };
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            int pn = profile.Length;

            for (int k = 0; k < n; k++)
            {
                Vec2d a = loop[k], b = loop[(k + 1) % n];
                double mx = (a.x + b.x) * 0.5, mz = (a.z + b.z) * 0.5;
                if (mx < tiling.MinX || mx > tiling.MaxX || mz < tiling.MinZ || mz > tiling.MaxZ) continue;
                if (tiling.OwnerAt(mx, mz) != index) continue;
                Vec2d sa = CourseLayout.LoopSide(k), sb = CourseLayout.LoopSide((k + 1) % n);
                double len = dist[k + 1] - dist[k];
                int m = Math.Max(1, (int)Math.Ceiling(len / 0.8));
                int previous = -1;
                for (int j = 0; j <= m; j++)
                {
                    double t = (double)j / m;
                    double px = a.x + (b.x - a.x) * t, pz = a.z + (b.z - a.z) * t;
                    double sx = sa.x + (sb.x - sa.x) * t, sz = sa.z + (sb.z - sa.z) * t;
                    double sl = Math.Sqrt(sx * sx + sz * sz);
                    sx /= sl;
                    sz /= sl;
                    float v = (float)(dist[k] + len * t);
                    int section = vertices.Count;
                    foreach (var p in profile)
                    {
                        double x = px + sx * p.across, z = pz + sz * p.across;
                        vertices.Add(new Vector3((float)x, (float)world.GroundHeight(x, z) + p.dy, (float)z));
                        uvs.Add(new Vector2(p.across / hw, v));
                        colors.Add(new Color32(255, 255, 255, p.alpha));
                    }
                    if (previous >= 0)
                    {
                        for (int i = 0; i < pn - 1; i++)
                        {
                            int qa = previous + i, qb = previous + i + 1, qc = section + i, qd = section + i + 1;
                            triangles.Add(qa); triangles.Add(qc); triangles.Add(qb);
                            triangles.Add(qb); triangles.Add(qc); triangles.Add(qd);
                        }
                    }
                    previous = section;
                }
            }
            if (triangles.Count == 0) return null;

            var mesh = new Mesh { name = $"{hole.Id}_cartpath", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Cup (an interior drawn by the CupInterior shader on a disc of exactly <see cref="CupProfile.CupRadius"/>; no
        /// collider) and the flagstick, its cloth streaming downwind.
        /// </summary>
        private static Transform BuildCupAndFlag(HoleDefinition hole, ICollisionWorld world, CourseVisualAssets assets, Transform parent)
        {
            var cup = new GameObject("Cup").transform;
            cup.SetParent(parent, false);
            Vector3 p = hole.CupM.ToUnity();
            p.y = (float)world.GroundHeight(hole.CupM.x, hole.CupM.z);
            cup.position = p;

            float d = (float)hole.Cup.CupRadius * 2;
            if (assets.CupInterior != null)
            {
                GameObject liner = Primitive(PrimitiveType.Quad, "Cup Liner", cup, assets.CupInterior, false);
                liner.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                liner.transform.localScale = new Vector3(d, d, 1f);
                liner.transform.localPosition = new Vector3(0, 0.003f, 0);
                liner.GetComponent<MeshRenderer>().receiveShadows = true;
            }
            else
            {
                GameObject hole3d = Primitive(PrimitiveType.Cylinder, "Cup Liner", cup, assets.Cup, false);
                hole3d.transform.localScale = new Vector3(d, 0.004f, d);
                hole3d.transform.localPosition = new Vector3(0, 0.004f, 0);
            }

            Vector3 wind = hole.WindMps.ToUnity();
            wind.y = 0;
            Quaternion downwind = wind.sqrMagnitude > 0.01f ? Quaternion.LookRotation(wind.normalized) * Quaternion.Euler(0, -90f, 0) : Quaternion.identity;
            var stick = new GameObject("Flagstick").transform;
            stick.SetParent(cup, false);
            stick.localRotation = downwind;

            GameObject pole = Primitive(PrimitiveType.Cylinder, "Pole", stick, assets.FlagPole, true);
            pole.transform.localScale = new Vector3(0.022f, 1.15f, 0.022f);
            pole.transform.localPosition = new Vector3(0, 1.15f, 0);
            GameObject ferrule = Primitive(PrimitiveType.Cylinder, "Ferrule", stick, assets.TeeMarker, true);
            ferrule.transform.localScale = new Vector3(0.03f, 0.012f, 0.03f);
            ferrule.transform.localPosition = new Vector3(0, 2.305f, 0);

            var cloth = new GameObject("Flag");
            cloth.transform.SetParent(stick, false);
            cloth.transform.localPosition = new Vector3(0.011f, 1.93f, 0);
            cloth.AddComponent<MeshFilter>().sharedMesh = FlagMesh(0.55f, 0.36f, 16, 8);
            var renderer = cloth.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = assets.FlagCloth;
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            return cup;
        }

        /// <summary>Flat cloth grid along local +X from the pole (x = 0); UV.x = 0 at the pole, 1 at the fly end.</summary>
        private static Mesh FlagMesh(float width, float height, int nu, int nv)
        {
            var vertices = new Vector3[(nu + 1) * (nv + 1)];
            var uvs = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            for (int j = 0; j <= nv; j++)
            {
                for (int i = 0; i <= nu; i++)
                {
                    int k = j * (nu + 1) + i;
                    float u = (float)i / nu, v = (float)j / nv;
                    vertices[k] = new Vector3(u * width, v * height, 0);
                    uvs[k] = new Vector2(u, v);
                    normals[k] = Vector3.back;
                }
            }
            var triangles = new int[nu * nv * 6];
            int t = 0;
            for (int j = 0; j < nv; j++)
            {
                for (int i = 0; i < nu; i++)
                {
                    int a = j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }
            var mesh = new Mesh { name = "flag_cloth" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            // Room for the wave the shader adds.
            mesh.bounds = new Bounds(new Vector3(width * 0.5f, height * 0.5f, 0), new Vector3(width + 0.2f, height + 0.2f, 0.5f));
            return mesh;
        }
    }
}
