using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Dense GPU-instanced grass over a hole's first cut, rough, deep rough and fescue. Clumps are scattered when the
    /// component is enabled, deterministically, from the baked Ground mesh: its UV1/UV2 surface masks (signed distances to
    /// the green, bunkers, fairway and deep rough) pick the grass class per clump, and a bitmap written at bake time keeps
    /// the cart paths and hazards clear. Clumps are bucketed into world tiles, shuffled within each tile, and drawn with
    /// <see cref="Graphics.RenderMeshPrimitives"/> per camera: a tile draws only the prefix of its clumps still alive at its
    /// nearest distance, and GrassInstanced.shader fades the rest by rank, so on-screen density stays constant.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GrassField : MonoBehaviour
    {
        public const string MaterialResource = "Models/Course/Nature/GrassInstanced";
        public const float ExclusionCell = 0.5f;
        public const float Tile = 16f;

        /// <summary>Master switch (e.g. a low quality tier); the dressing builder has its own bake-time toggle.</summary>
        public static bool GlobalEnabled = true;

        /// <summary>Fraction of clumps drawn, for quality tiers. Thinning is by shuffled rank, so it never pops.</summary>
        public static float DensityScale = 1f;

        public enum GrassClass { FirstCut = 0, Rough = 1, DeepRough = 2, Fescue = 3 }

        [StructLayout(LayoutKind.Sequential)]
        private struct GrassInstance
        {
            public Vector3 Position;
            public uint Packed;
        }

        private sealed class GrassTile
        {
            public int Offset, Count;
            public Bounds Bounds;
            public MaterialPropertyBlock Props;
        }

        [SerializeField] private MeshFilter _ground;
        [SerializeField] private Material _material;
        [SerializeField] private int _seed;
        [SerializeField] private float _minHeight = float.MinValue;
        [Tooltip("Clumps per square metre: first cut, rough, deep rough, fescue.")]
        [SerializeField] private Vector4 _density = new Vector4(6f, 5f, 4f, 2.5f);
        [SerializeField] private float _lodDistance = 22f;
        [SerializeField, HideInInspector] private Vector2 _exclusionOrigin;
        [SerializeField, HideInInspector] private int _exclusionWidth, _exclusionHeight;
        [SerializeField, HideInInspector] private byte[] _exclusion;

        private static readonly int InstancesId = Shader.PropertyToID("_GrassInstances");
        private static readonly int OffsetId = Shader.PropertyToID("_GrassInstanceOffset");
        private static readonly int CountId = Shader.PropertyToID("_GrassInstanceCount");
        private static readonly int InteractorId = Shader.PropertyToID("_GrassInteractor");
        private static readonly int DensityStartId = Shader.PropertyToID("_DensityStart");
        private static readonly int FadeEndId = Shader.PropertyToID("_FadeEnd");

        private readonly List<GrassTile> _tiles = new List<GrassTile>();
        private readonly Plane[] _planes = new Plane[6];
        private GraphicsBuffer _buffer;
        private Mesh _clumpNear, _clumpFar;

        public int InstanceCount { get; private set; }

        /// <summary>Called once by the dressing builder when the hole is baked.</summary>
        public void Configure(MeshFilter ground, Material material, int seed, float minHeight, Vector2 exclusionOrigin, int exclusionWidth, int exclusionHeight, byte[] exclusion)
        {
            _ground = ground;
            _material = material;
            _seed = seed;
            _minHeight = minHeight;
            _exclusionOrigin = exclusionOrigin;
            _exclusionWidth = exclusionWidth;
            _exclusionHeight = exclusionHeight;
            _exclusion = exclusion;
            if (isActiveAndEnabled) Rebuild();
        }

        /// <summary>Folds the grass away from the ball (radius in metres; 0 turns it off). Shared by every field.</summary>
        public static void SetBallPosition(Vector3 position, float radius = 0.18f) =>
            Shader.SetGlobalVector(InteractorId, new Vector4(position.x, position.y, position.z, radius));

        public static void ClearBall() => Shader.SetGlobalVector(InteractorId, Vector4.zero);

        private void OnEnable()
        {
            Rebuild();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Release();
        }

        public void Rebuild()
        {
            Release();
            if (_ground == null || _ground.sharedMesh == null || _material == null) return;
            Mesh ground = _ground.sharedMesh;
            if (!ground.isReadable)
            {
                Debug.LogWarning($"GrassField: '{ground.name}' is not readable; no grass on {name}.", this);
                return;
            }

            var tiles = new Dictionary<(int, int), List<GrassInstance>>();
            Scatter(ground, _ground.transform.localToWorldMatrix, tiles);
            if (tiles.Count == 0) return;

            var all = new List<GrassInstance>();
            var keys = new List<(int, int)>(tiles.Keys);
            keys.Sort();
            foreach ((int tx, int tz) key in keys)
            {
                List<GrassInstance> list = tiles[key];
                var rng = new Rng(Hash((uint)(key.tx * 73856093) ^ (uint)(key.tz * 19349663) ^ (uint)_seed));
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = (int)(rng.NextUInt() % (uint)(i + 1));
                    (list[i], list[j]) = (list[j], list[i]);
                }
                Vector3 min = list[0].Position, max = min;
                foreach (GrassInstance g in list)
                {
                    min = Vector3.Min(min, g.Position);
                    max = Vector3.Max(max, g.Position);
                }
                var bounds = new Bounds();
                bounds.SetMinMax(min - new Vector3(0.4f, 0.1f, 0.4f), max + new Vector3(0.4f, 0.9f, 0.4f));
                _tiles.Add(new GrassTile { Offset = all.Count, Count = list.Count, Bounds = bounds });
                all.AddRange(list);
            }

            InstanceCount = all.Count;
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, all.Count, Marshal.SizeOf<GrassInstance>());
            _buffer.SetData(all);
            foreach (GrassTile tile in _tiles)
            {
                tile.Props = new MaterialPropertyBlock();
                tile.Props.SetBuffer(InstancesId, _buffer);
                tile.Props.SetFloat(OffsetId, tile.Offset);
                tile.Props.SetFloat(CountId, tile.Count);
            }
            _clumpNear = BuildClump("grass_clump_lod0", 7, new[] { 0f, 0.38f, 0.72f }, 1f, 11);
            _clumpFar = BuildClump("grass_clump_lod1", 4, new[] { 0f, 0.55f }, 1.45f, 23);
        }

        private void Release()
        {
            _buffer?.Release();
            _buffer = null;
            _tiles.Clear();
            InstanceCount = 0;
            DestroyMesh(ref _clumpNear);
            DestroyMesh(ref _clumpFar);
        }

        private static void DestroyMesh(ref Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!GlobalEnabled || _buffer == null || _material == null || DensityScale <= 0) return;
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if ((cam.cullingMask & (1 << gameObject.layer)) == 0) return;

            Vector3 eye = cam.transform.position;
            float densityStart = Mathf.Max(0.5f, _material.GetFloat(DensityStartId));
            float fadeEnd = Mathf.Min(_material.GetFloat(FadeEndId), cam.farClipPlane);
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            foreach (GrassTile tile in _tiles)
            {
                float d = Mathf.Sqrt(tile.Bounds.SqrDistance(eye));
                if (d > fadeEnd || !GeometryUtility.TestPlanesAABB(_planes, tile.Bounds)) continue;
                float alive = d <= densityStart ? 1f : densityStart * densityStart / (d * d);
                int count = Mathf.Min(tile.Count, Mathf.CeilToInt(tile.Count * alive * DensityScale));
                if (count <= 0) continue;
                var rp = new RenderParams(_material)
                {
                    camera = cam,
                    worldBounds = tile.Bounds,
                    matProps = tile.Props,
                    layer = gameObject.layer,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                };
                Graphics.RenderMeshPrimitives(rp, d < _lodDistance ? _clumpNear : _clumpFar, 0, count);
            }
        }

        // ---------------------------------------------------------------- scatter

        private void Scatter(Mesh ground, Matrix4x4 toWorld, Dictionary<(int, int), List<GrassInstance>> tiles)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var masksA = new List<Vector2>();
            var masksB = new List<Vector2>();
            ground.GetVertices(vertices);
            ground.GetNormals(normals);
            ground.GetUVs(1, masksA);
            ground.GetUVs(2, masksB);
            if (normals.Count != vertices.Count || masksA.Count != vertices.Count || masksB.Count != vertices.Count) return;
            int[] triangles = ground.GetTriangles(0);

            float maxDensity = Mathf.Max(Mathf.Max(_density.x, _density.y), Mathf.Max(_density.z, _density.w));
            if (maxDensity <= 0) return;
            float noiseX = (_seed & 1023) * 0.37f, noiseZ = ((_seed >> 10) & 1023) * 0.29f;

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                Vector2 a0 = masksA[i0], a1 = masksA[i1], a2 = masksA[i2];
                Vector2 b0 = masksB[i0], b1 = masksB[i1], b2 = masksB[i2];
                // Whole triangle on the fairway, green or sand: nothing to grow.
                if (Mathf.Max(b0.x, Mathf.Max(b1.x, b2.x)) < 0f) continue;
                if (Mathf.Max(a0.x, Mathf.Max(a1.x, a2.x)) < 0.6f) continue;
                if (Mathf.Max(a0.y, Mathf.Max(a1.y, a2.y)) < 0.2f) continue;
                Vector3 p0 = vertices[i0], p1 = vertices[i1], p2 = vertices[i2];
                if (Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)) < _minHeight) continue;
                Vector3 n0 = normals[i0], n1 = normals[i1], n2 = normals[i2];
                if (Mathf.Max(n0.y, Mathf.Max(n1.y, n2.y)) < 0.8f) continue;

                float area = 0.5f * Vector3.Cross(p1 - p0, p2 - p0).magnitude;
                var rng = new Rng(Hash((uint)t * 2654435761u ^ (uint)_seed));
                int samples = (int)(area * maxDensity + rng.Next01());
                for (int k = 0; k < samples; k++)
                {
                    float r1 = rng.Next01(), r2 = rng.Next01();
                    if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
                    float w0 = 1f - r1 - r2;
                    Vector2 ma = a0 * w0 + a1 * r1 + a2 * r2;
                    Vector2 mb = b0 * w0 + b1 * r1 + b2 * r2;
                    float jitter = rng.Next01() - 0.5f, keep = rng.Next01();
                    float green = ma.x, bunker = ma.y, fairway = mb.x, deep = mb.y;
                    if (fairway < 0.05f || green < 1f + jitter * 0.4f || bunker < 0.5f) continue;

                    Vector3 p = p0 * w0 + p1 * r1 + p2 * r2;
                    if (p.y < _minHeight || Excluded(p.x, p.z)) continue;
                    Vector3 n = (n0 * w0 + n1 * r1 + n2 * r2).normalized;
                    if (n.y < 0.8f) continue;

                    GrassClass cls;
                    if (fairway < 1.8f + jitter * 0.9f || green < 2.2f + jitter * 0.9f) cls = GrassClass.FirstCut;
                    else if (bunker < 1.6f + jitter) cls = GrassClass.Fescue;
                    else if (deep < jitter * 0.8f)
                    {
                        float patch = Mathf.PerlinNoise(p.x * 0.045f + noiseX, p.z * 0.045f + noiseZ) * 0.7f
                                      + Mathf.PerlinNoise(p.x * 0.17f + noiseZ, p.z * 0.17f + noiseX) * 0.3f;
                        cls = patch + jitter * 0.15f > 0.52f ? GrassClass.Fescue : GrassClass.DeepRough;
                    }
                    else cls = GrassClass.Rough;
                    if (keep * maxDensity > _density[(int)cls]) continue;

                    Vector3 world = toWorld.MultiplyPoint3x4(p);
                    Vector3 nw = toWorld.MultiplyVector(n).normalized;
                    uint packed = (rng.NextUInt() & 0xFFu)
                                  | ((rng.NextUInt() & 0x3Fu) << 8)
                                  | ((uint)cls << 14)
                                  | (Quantize6(nw.x) << 16)
                                  | (Quantize6(nw.z) << 22)
                                  | ((rng.NextUInt() & 0xFu) << 28);
                    var key = (Mathf.FloorToInt(world.x / Tile), Mathf.FloorToInt(world.z / Tile));
                    if (!tiles.TryGetValue(key, out List<GrassInstance> list)) tiles[key] = list = new List<GrassInstance>();
                    list.Add(new GrassInstance { Position = world, Packed = packed });
                }
            }
        }

        private bool Excluded(float x, float z)
        {
            if (_exclusion == null || _exclusion.Length == 0) return false;
            int ix = Mathf.FloorToInt((x - _exclusionOrigin.x) / ExclusionCell);
            int iz = Mathf.FloorToInt((z - _exclusionOrigin.y) / ExclusionCell);
            if (ix < 0 || iz < 0 || ix >= _exclusionWidth || iz >= _exclusionHeight) return false;
            int bit = iz * _exclusionWidth + ix;
            return (_exclusion[bit >> 3] & (1 << (bit & 7))) != 0;
        }

        private static uint Quantize6(float v) => (uint)Mathf.Clamp(Mathf.RoundToInt((v + 1f) * 31.5f), 0, 63);

        // ---------------------------------------------------------------- clump meshes

        /// <summary>
        /// A clump of tapered blades in unit-radius clump space, laid out as GrassInstanced.shader expects (see its header);
        /// the far LOD has fewer, wider blades so the clump keeps its coverage.
        /// </summary>
        private static Mesh BuildClump(string meshName, int blades, float[] rows, float widen, int seed)
        {
            var rng = new Rng(Hash((uint)seed));
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var indices = new List<int>();
            for (int b = 0; b < blades; b++)
            {
                float radius = b == 0 ? 0.08f : Mathf.Sqrt((b + 0.5f) / blades) * (0.85f + 0.15f * rng.Next01());
                float angle = b * 2.39996f + rng.Next01() * 0.4f;
                var root = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                float facing = rng.Next01() * Mathf.PI * 2f;
                float lean = b == 0 ? rng.Next01() * Mathf.PI * 2f : angle + (rng.Next01() - 0.5f) * 1.2f;
                var normal = new Vector3(Mathf.Cos(facing), 0, Mathf.Sin(facing));
                var extra = new Vector2(rng.Next01(), lean);
                int first = vertices.Count;
                foreach (float t in rows)
                {
                    float taper = (1f - 0.7f * t) * widen;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        vertices.Add(new Vector3(root.x, t, root.y));
                        normals.Add(normal);
                        uv0.Add(new Vector2(side * taper, t));
                        uv1.Add(extra);
                    }
                }
                vertices.Add(new Vector3(root.x, 1f, root.y));
                normals.Add(normal);
                uv0.Add(new Vector2(0f, 1f));
                uv1.Add(extra);
                for (int r = 0; r + 1 < rows.Length; r++)
                {
                    int v = first + r * 2;
                    indices.Add(v); indices.Add(v + 2); indices.Add(v + 1);
                    indices.Add(v + 1); indices.Add(v + 2); indices.Add(v + 3);
                }
                int top = first + (rows.Length - 1) * 2;
                indices.Add(top); indices.Add(top + 2); indices.Add(top + 1);
            }
            var mesh = new Mesh { name = meshName, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(indices, 0);
            mesh.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(2, 1, 2));
            mesh.UploadMeshData(true);
            return mesh;
        }

        // ---------------------------------------------------------------- deterministic random

        private static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        private struct Rng
        {
            private uint _state;

            public Rng(uint seed) => _state = seed;

            public uint NextUInt()
            {
                _state = _state * 1664525u + 1013904223u;
                return Hash(_state);
            }

            public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);
        }
    }
}
