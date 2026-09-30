using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Marks a hole's authored, baked visuals in the scene. The content hash ties the visuals to the exact collision
    /// definition; a mismatch means the bake is stale and must be redone before shipping.
    /// Gizmos draw the gameplay data (regions, hazards, drops, boundary, tee, cup) in the Scene view.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BakedHole : MonoBehaviour
    {
        public string HoleId;
        public string HoleVersion;
        public string ContentHash;
        public Transform Tee;
        public Transform Cup;

        public bool Matches(HoleDefinition hole) =>
            hole != null && hole.Id == HoleId && hole.Version == HoleVersion && hole.ContentHash == ContentHash;

        public static BakedHole FindInScene(HoleDefinition hole, out BakedHole stale)
        {
            stale = null;
            foreach (BakedHole baked in FindObjectsByType<BakedHole>(FindObjectsInactive.Include))
            {
                if (baked.HoleId != hole.Id) continue;
                if (baked.Matches(hole)) return baked;
                stale = baked;
            }
            return null;
        }

#if UNITY_EDITOR
        private static CourseDefinition _gizmoCourse;

        private void OnDrawGizmosSelected()
        {
            if (_gizmoCourse == null) _gizmoCourse = GrayboxCourse.Create();
            HoleDefinition hole = _gizmoCourse.Holes.Find(h => h.Id == HoleId);
            if (hole == null) return;

            foreach (SurfaceRegion region in hole.Surfaces)
            {
                Gizmos.color = RegionColor(region.Surface);
                DrawPolygon(hole, region.Polygon, 0.08f);
            }

            Gizmos.color = new Color(0.1f, 0.5f, 1f);
            foreach (HazardVolume hazard in hole.Hazards) DrawPolygon(hole, hazard.Polygon, 0.12f);

            Gizmos.color = Color.white;
            DrawPolygon(hole, hole.PlayableBoundary, 0.6f);

            Gizmos.color = Color.yellow;
            foreach (DropPoint drop in hole.Drops)
                Gizmos.DrawWireSphere(Ground(hole, drop.Position.x, drop.Position.z) + Vector3.up * 0.3f, 1.0f);

            Gizmos.color = new Color(0.6f, 0.35f, 0.15f);
            foreach (TreeObstacle tree in hole.Trees)
            {
                Vector3 b = Ground(hole, tree.Position.x, tree.Position.z);
                Gizmos.DrawWireCube(b + Vector3.up * (float)tree.TrunkHeight * 0.5f,
                    new Vector3((float)tree.TrunkRadius * 2, (float)tree.TrunkHeight, (float)tree.TrunkRadius * 2));
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(hole.TeeM.ToUnity() + Vector3.up * 0.2f, 0.6f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(hole.CupM.ToUnity(), (float)hole.Cup.CaptureRadius * 4);
            Gizmos.DrawLine(hole.TeeM.ToUnity() + Vector3.up * 0.3f, hole.CupM.ToUnity() + Vector3.up * 0.3f);
        }

        private static void DrawPolygon(HoleDefinition hole, IReadOnlyList<Vec2d> polygon, float lift)
        {
            for (int i = 0; i < polygon.Count; i++)
            {
                Vec2d a = polygon[i];
                Vec2d b = polygon[(i + 1) % polygon.Count];
                Gizmos.DrawLine(Ground(hole, a.x, a.z) + Vector3.up * lift, Ground(hole, b.x, b.z) + Vector3.up * lift);
            }
        }

        private static Vector3 Ground(HoleDefinition hole, double x, double z) =>
            new Vector3((float)x, (float)TerrainModel.Height(hole.Terrain, x, z), (float)z);

        private static Color RegionColor(SurfaceId surface)
        {
            switch (surface)
            {
                case SurfaceId.Green: return new Color(0.3f, 1f, 0.4f);
                case SurfaceId.Fairway: return new Color(0.6f, 0.9f, 0.3f);
                case SurfaceId.Bunker: return new Color(1f, 0.9f, 0.6f);
                case SurfaceId.DeepRough: return new Color(0.2f, 0.4f, 0.1f);
                case SurfaceId.Tee: return Color.cyan;
                case SurfaceId.CartPath: return Color.gray;
                default: return Color.green;
            }
        }
#endif
    }
}
