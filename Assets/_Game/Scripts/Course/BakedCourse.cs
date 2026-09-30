using UnityEngine;

namespace RealGolf.Course
{
    /// <summary>
    /// Marks a course that was generated in the editor and saved into the scene.
    /// Every hole is present at once, so play looks out over the rest of the course
    /// instead of a single hole floating in fog.
    ///
    /// Gameplay still works in hole-local space: <see cref="HoleTerrain"/> is an
    /// analytic field authored around its own origin, and the ball, camera and
    /// green-read all sample it with raw world coordinates. Rather than teach every
    /// one of those about a per-hole transform, <see cref="AlignTo"/> moves the whole
    /// course so the hole being played sits back at the origin facing +Z. The other
    /// holes keep their relative positions, which is what makes them visible.
    /// </summary>
    public sealed class BakedCourse : MonoBehaviour
    {
        [Tooltip("Seed the course was baked from. Runtime refuses a mismatch rather " +
                 "than playing one layout on another's terrain.")]
        public int Seed;

        [Tooltip("Hole roots in play order, as baked.")]
        public Transform[] HoleRoots = System.Array.Empty<Transform>();

        /// <summary>Pose each hole was baked at, relative to this course root.</summary>
        [SerializeField] private Vector3[] _bakedPositions = System.Array.Empty<Vector3>();
        [SerializeField] private float[] _bakedYaws = System.Array.Empty<float>();

        public int HoleCount => HoleRoots.Length;

        public void StorePose(int index, Vector3 localPosition, float yaw)
        {
            EnsureCapacity(HoleRoots.Length);
            _bakedPositions[index] = localPosition;
            _bakedYaws[index] = yaw;
        }

        public Transform Hole(int index) =>
            index >= 0 && index < HoleRoots.Length ? HoleRoots[index] : null;

        /// <summary>
        /// Places the course so the given hole's local space coincides with world
        /// space. For a hole baked at (p, R), that is rotation R inverse and position
        /// -R inverse * p, which sends its tee to the origin and its play direction
        /// to +Z -- exactly what the analytic terrain assumes.
        /// </summary>
        public void AlignTo(int index)
        {
            if (index < 0 || index >= HoleRoots.Length)
            {
                return;
            }

            EnsureCapacity(HoleRoots.Length);
            Quaternion inverse = Quaternion.Euler(0f, -_bakedYaws[index], 0f);
            transform.rotation = inverse;
            transform.position = -(inverse * _bakedPositions[index]);
        }

        private void EnsureCapacity(int count)
        {
            if (_bakedPositions.Length != count)
            {
                System.Array.Resize(ref _bakedPositions, count);
            }

            if (_bakedYaws.Length != count)
            {
                System.Array.Resize(ref _bakedYaws, count);
            }
        }
    }
}
