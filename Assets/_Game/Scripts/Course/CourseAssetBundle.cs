using UnityEngine;

namespace RealGolf.Course
{
    /// <summary>
    /// Container asset the editor bake parks its generated meshes and materials in.
    /// The course builder creates those at runtime with <c>new Mesh()</c>; left alone
    /// they would be serialised loose into the scene file, so a baked course would
    /// carry hundreds of orphan objects and duplicate them on every re-bake. Holding
    /// them as sub-assets of one file keeps the scene referencing real assets.
    /// </summary>
    public sealed class CourseAssetBundle : ScriptableObject
    {
        [Tooltip("Seed of the course these meshes were generated from.")]
        public int Seed;

        [Tooltip("Meshes parked as sub-assets of this file.")]
        public int MeshCount;

        [Tooltip("Materials parked as sub-assets of this file.")]
        public int MaterialCount;
    }
}
