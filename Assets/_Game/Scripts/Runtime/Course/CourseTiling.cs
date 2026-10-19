using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// One ground grid over the whole course (every hole's playable area plus <see cref="CourseVisualBuilder.GroundMargin"/>).
    /// Each cell belongs to the hole whose boundary contains its centre, or else the nearest one, so the per-hole ground
    /// meshes tile the rectangle exactly with shared vertices along their seams. The coastal scenery's inner ring is built
    /// on the same rectangle.
    /// </summary>
    public sealed class CourseTiling
    {
        public readonly float MinX, MinZ, MaxX, MaxZ;
        public readonly int Nx, Nz;
        public readonly IReadOnlyList<HoleDefinition> Holes;
        private readonly byte[] _owner;

        public CourseTiling(IReadOnlyList<HoleDefinition> holes, float spacing, float margin)
        {
            Holes = holes;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (HoleDefinition hole in holes)
            {
                foreach (Vec2d p in hole.PlayableBoundary)
                {
                    minX = Mathf.Min(minX, (float)p.x);
                    minZ = Mathf.Min(minZ, (float)p.z);
                    maxX = Mathf.Max(maxX, (float)p.x);
                    maxZ = Mathf.Max(maxZ, (float)p.z);
                }
            }
            MinX = minX - margin;
            MinZ = minZ - margin;
            Nx = Mathf.CeilToInt((maxX + margin - MinX) / spacing) + 1;
            Nz = Mathf.CeilToInt((maxZ + margin - MinZ) / spacing) + 1;
            MaxX = MinX + (Nx - 1) * spacing;
            MaxZ = MinZ + (Nz - 1) * spacing;
            Spacing = spacing;

            _owner = new byte[(Nx - 1) * (Nz - 1)];
            for (int iz = 0; iz < Nz - 1; iz++)
            {
                for (int ix = 0; ix < Nx - 1; ix++)
                    _owner[iz * (Nx - 1) + ix] = (byte)OwnerAt(MinX + (ix + 0.5f) * spacing, MinZ + (iz + 0.5f) * spacing);
            }
        }

        public float Spacing { get; }

        public int CellOwner(int ix, int iz) => _owner[iz * (Nx - 1) + ix];

        /// <summary>Index of the hole that draws the ground at this point.</summary>
        public int OwnerAt(double x, double z)
        {
            if (Holes.Count == 1) return 0;
            int best = 0;
            double bestD = double.MaxValue;
            for (int h = 0; h < Holes.Count; h++)
            {
                List<Vec2d> boundary = Holes[h].PlayableBoundary;
                if (Shapes.Contains(boundary, x, z)) return h;
                double d = TerrainModel.DistanceToEdge(boundary, x, z);
                if (d < bestD)
                {
                    bestD = d;
                    best = h;
                }
            }
            return best;
        }

        /// <summary>Distance from a point to the nearest playable boundary of any hole other than <paramref name="except"/>.</summary>
        public double DistanceToOtherHole(int except, double x, double z)
        {
            double best = double.MaxValue;
            for (int h = 0; h < Holes.Count; h++)
            {
                if (h == except) continue;
                List<Vec2d> boundary = Holes[h].PlayableBoundary;
                double d = Shapes.Contains(boundary, x, z) ? 0 : TerrainModel.DistanceToEdge(boundary, x, z);
                if (d < best) best = d;
            }
            return best;
        }
    }
}
