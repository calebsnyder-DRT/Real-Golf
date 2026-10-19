using UnityEngine;

namespace Puttshack.Runtime.Clubs
{
    public enum ClubFaceStyle
    {
        Iron,
        Wood,
        Putter,
    }

    /// <summary>
    /// One club model (ArtSource/Blender/scripts/pp_clubs.py), authored in the lead-hand seat frame the golfers hold
    /// clubs in (<c>ClubGrip_&lt;Kind&gt;</c> under ClubGrip.L): origin at the lead-hand grip point, +Y down the shaft toward
    /// the head, +Z the square face normal, toe toward -X. Parented to a seat with an identity local pose it sits where
    /// the baked club of the same <see cref="Kind"/> sits, so impact and both-hands grip measurements still hold.
    /// Reference points are children with seat-aligned axes (Face: +Z along the face normal).
    /// </summary>
    public sealed class ClubModel : MonoBehaviour
    {
        public const string GripName = "Grip";
        public const string TrailGripName = "Grip_Trail";
        public const string ButtName = "Butt";
        public const string HoselName = "Hosel";
        public const string FaceName = "Face";
        public const string SoleName = "Sole";

        public string ModelId;
        [Tooltip("Seat the club rides in the lead hand: Driver, Iron, Wedge or Putter.")]
        public string Kind;
        public string Label;
        public ClubFaceStyle FaceStyle;
        public bool Graphite;
        [Tooltip("Butt to sole, as built (metres).")]
        public float LengthMeters;
        [Tooltip("Shaft to ground at address with the sole flat (degrees).")]
        public float LieDegrees;
        public float LoftDegrees;
        public Renderer Body;
        [Tooltip("Material role per submesh of Body (Head, Face, Accent, Crown, Shaft, Grip, Trim, Decal).")]
        public string[] Roles = new string[0];
        public Transform Grip;
        public Transform TrailGrip;
        public Transform Butt;
        public Transform Hosel;
        public Transform Face;
        public Transform Sole;

        public string PaletteId { get; private set; }

        /// <summary>Rotation that stands the club on its sole at its lie, face toward +Z, toe toward +X, grip leaning to -X.</summary>
        public Quaternion AddressRotation => Quaternion.Euler(0f, 0f, 90f - LieDegrees) * Quaternion.Euler(0f, 0f, 180f);

        public void Paint(ClubPalette palette)
        {
            if (palette == null || Body == null) return;
            var mats = new Material[Roles.Length];
            for (int i = 0; i < Roles.Length; i++) mats[i] = palette.For(Roles[i], this);
            Body.sharedMaterials = mats;
            PaletteId = palette.Id;
        }
    }
}
