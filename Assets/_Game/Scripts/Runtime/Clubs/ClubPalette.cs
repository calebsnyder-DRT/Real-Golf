using UnityEngine;

namespace Puttshack.Runtime.Clubs
{
    /// <summary>A colourway: one URP material per club material role. Purely visual.</summary>
    [CreateAssetMenu(menuName = "Puttshack/Club Palette")]
    public sealed class ClubPalette : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public Material Head;
        public Material FaceIron;
        public Material FaceWood;
        public Material FacePutter;
        public Material Accent;
        public Material Crown;
        public Material ShaftSteel;
        public Material ShaftGraphite;
        public Material Grip;
        public Material Trim;
        public Material Decal;

        public Material For(string role, ClubModel model)
        {
            switch (role)
            {
                case "Face":
                    return model.FaceStyle == ClubFaceStyle.Wood ? FaceWood : model.FaceStyle == ClubFaceStyle.Putter ? FacePutter : FaceIron;
                case "Accent": return Accent;
                case "Crown": return Crown;
                case "Shaft": return model.Graphite ? ShaftGraphite : ShaftSteel;
                case "Grip": return Grip;
                case "Trim": return Trim;
                case "Decal": return Decal;
                default: return Head;
            }
        }
    }
}
