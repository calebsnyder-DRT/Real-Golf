using System.Collections.Generic;
using UnityEngine;

namespace RealGolf.Course
{
    /// <summary>Small procedural meshes used for course dressing.</summary>
    public static class MeshFactory
    {
        private static Mesh _cone;
        private static Mesh _disc;

        public static Mesh Cone(int segments = 10)
        {
            if (_cone != null)
            {
                return _cone;
            }

            var vertices = new List<Vector3> { new(0f, 1f, 0f) };
            var triangles = new List<int>();

            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }

            for (int i = 0; i < segments; i++)
            {
                int current = 1 + i;
                int next = 1 + (i + 1) % segments;
                triangles.Add(0);
                triangles.Add(next);
                triangles.Add(current);
            }

            int baseCenter = vertices.Count;
            vertices.Add(Vector3.zero);
            for (int i = 0; i < segments; i++)
            {
                int current = 1 + i;
                int next = 1 + (i + 1) % segments;
                triangles.Add(baseCenter);
                triangles.Add(current);
                triangles.Add(next);
            }

            _cone = new Mesh { name = "Cone" };
            _cone.SetVertices(vertices);
            _cone.SetTriangles(triangles, 0);
            _cone.RecalculateNormals();
            _cone.RecalculateBounds();
            return _cone;
        }

        public static Mesh Disc(int segments = 48)
        {
            if (_disc != null)
            {
                return _disc;
            }

            var vertices = new List<Vector3> { Vector3.zero };
            var triangles = new List<int>();

            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }

            for (int i = 0; i < segments; i++)
            {
                triangles.Add(0);
                triangles.Add(1 + (i + 1) % segments);
                triangles.Add(1 + i);
            }

            _disc = new Mesh { name = "Disc" };
            _disc.SetVertices(vertices);
            _disc.SetTriangles(triangles, 0);
            _disc.RecalculateNormals();
            _disc.RecalculateBounds();
            return _disc;
        }
    }
}
