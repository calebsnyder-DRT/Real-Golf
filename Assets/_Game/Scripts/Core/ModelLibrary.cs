using System.Collections.Generic;
using UnityEngine;

namespace RealGolf.Core
{
    /// <summary>
    /// Loads the CC0 Kenney kit models from Resources and instantiates them at a
    /// requested real-world size. Sizing is derived from each model's own bounds,
    /// so the game does not depend on the scale the kits were authored at.
    /// </summary>
    public static class ModelLibrary
    {
        private const string TreeRoot = "Models/Trees/";
        private const string PropRoot = "Models/Props/";
        private const string GolfRoot = "Models/Golf/";

        // Models authored in Blender for this project. They take precedence over the
        // Kenney kit where they exist; the kit stays as the fallback so a missing
        // authored import degrades rather than emptying the tree line.
        private const string AuthoredTreeRoot = "Models/Authored/Trees/";
        private const string AuthoredPropRoot = "Models/Authored/Props/";
        private const string AuthoredStructureRoot = "Models/Authored/Structures/";
        private const string AuthoredGolfRoot = "Models/Authored/Golf/";

        private static readonly string[] TreeNames =
        {
            "tree_default", "tree_oak", "tree_detailed", "tree_fat",
            "tree_blocks", "tree_cone", "tree_pineRoundA", "tree_pineRoundC", "tree_pineTallA"
        };

        private static readonly string[] PropNames =
        {
            "rock_smallA", "rock_smallB", "rock_smallFlatA", "grass", "grass_large", "flower_yellowA"
        };

        private static readonly string[] AuthoredTreeNames = { "tree_broad", "tree_slim" };

        private static readonly string[] AuthoredPropNames =
        {
            "bush_mound", "plant_flowering", "rock_cluster"
        };

        private static readonly string[] BallNames = { "ball-red", "ball-blue", "ball-green" };

        private static readonly Dictionary<string, GameObject> Cache = new();
        private static List<GameObject> _trees;
        private static List<GameObject> _props;

        public static IReadOnlyList<GameObject> Trees => _trees ??= PreferAuthored(
            AuthoredTreeRoot, AuthoredTreeNames, TreeRoot, TreeNames);

        public static IReadOnlyList<GameObject> Props => _props ??= PreferAuthored(
            AuthoredPropRoot, AuthoredPropNames, PropRoot, PropNames);

        /// <summary>Authored clubhouse, for the course's practice area. Null until imported.</summary>
        public static GameObject Clubhouse => Load(AuthoredStructureRoot + "clubhouse");

        /// <summary>Authored stone arch bridge, spanning 8 m. Null until imported.</summary>
        public static GameObject StoneBridge => Load(AuthoredStructureRoot + "bridge_stone");

        /// <summary>
        /// Authored hole complex: turf plug plus flagstick, built for standalone
        /// presentation. NOT the in-game pin -- the plug scales up into a disc on the
        /// green and the cup is already cut into the terrain. Use
        /// <see cref="AuthoredFlagstick"/> in play.
        /// </summary>
        public static GameObject HoleComplex => Load(AuthoredGolfRoot + "hole_complex");

        /// <summary>Authored pin: pole, ferrules and pennant only, pivot at its base.</summary>
        public static GameObject AuthoredFlagstick => Load(AuthoredGolfRoot + "flagstick");

        /// <summary>Purpose-built flagstick baked in Blender, or null if not imported yet.</summary>
        public static GameObject BakedFlagstick => Load(GolfRoot + "flagstick");

        public static GameObject Flag => Load(GolfRoot + "flag-red");
        public static GameObject Ball(int playerIndex) => Load(GolfRoot + BallNames[playerIndex % BallNames.Length]);

        /// <summary>
        /// Authored models if any of them imported, otherwise the kit. Mixing the two
        /// sets is deliberately avoided: they are lit and scaled to different palettes
        /// and reading as two species in one tree line looks like a bug.
        /// </summary>
        private static List<GameObject> PreferAuthored(string authoredRoot, string[] authoredNames,
                                                       string fallbackRoot, string[] fallbackNames)
        {
            List<GameObject> authored = LoadAll(authoredRoot, authoredNames);
            return authored.Count > 0 ? authored : LoadAll(fallbackRoot, fallbackNames);
        }

        private static List<GameObject> LoadAll(string root, string[] names)
        {
            var list = new List<GameObject>();
            foreach (string name in names)
            {
                GameObject prefab = Load(root + name);
                if (prefab != null)
                {
                    list.Add(prefab);
                }
            }

            return list;
        }

        public static GameObject Load(string resourcePath)
        {
            if (Cache.TryGetValue(resourcePath, out GameObject cached))
            {
                return cached;
            }

            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            Cache[resourcePath] = prefab;
            return prefab;
        }

        /// <summary>
        /// Instantiates a model and uniformly scales it so its tallest axis measures
        /// <paramref name="targetHeight"/> metres, with its base resting on the pivot.
        /// </summary>
        public static GameObject Spawn(GameObject prefab, Transform parent, Vector3 position, float targetHeight, Quaternion rotation)
        {
            if (prefab == null)
            {
                return null;
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            if (TryGetLocalBounds(instance, out Bounds bounds) && bounds.size.y > 0.0001f)
            {
                float scale = targetHeight / bounds.size.y;
                instance.transform.localScale = Vector3.one * scale;
                // Lift so the model's lowest point lands on the requested position.
                position -= new Vector3(0f, bounds.min.y * scale, 0f);
            }

            instance.transform.rotation = rotation;
            instance.transform.position = position;
            return instance;
        }

        /// <summary>Combined renderer bounds expressed in the instance's local space.</summary>
        private static bool TryGetLocalBounds(GameObject instance, out Bounds bounds)
        {
            var renderers = instance.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            Transform root = instance.transform;
            bool initialised = false;
            bounds = default;

            foreach (MeshRenderer renderer in renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                Bounds meshBounds = filter.sharedMesh.bounds;
                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;

                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? meshBounds.min.x : meshBounds.max.x,
                        (corner & 2) == 0 ? meshBounds.min.y : meshBounds.max.y,
                        (corner & 4) == 0 ? meshBounds.min.z : meshBounds.max.z);

                    Vector3 point = toRoot.MultiplyPoint3x4(offset);
                    if (!initialised)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        initialised = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return initialised;
        }
    }
}
