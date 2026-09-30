using System.Collections.Generic;
using Puttshack.Content;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>One bag model (stand, cart or tour). Every visible bag surface sits on a <c>Bag_*</c>/<c>HeadCover*</c> slot.</summary>
    public sealed class BagAppearance : MonoBehaviour
    {
        [Tooltip("Catalog style this model represents, e.g. GolfBag_Tour.")]
        public string Style;
        public List<Renderer> Renderers = new List<Renderer>();

        public void Collect()
        {
            Renderers.Clear();
            Renderers.AddRange(GetComponentsInChildren<Renderer>(true));
        }

        public void Apply(CosmeticItem bag, CosmeticArt art)
        {
            foreach (Renderer r in Renderers) CosmeticPaint.Apply(r, bag.Colors, art);
        }
    }
}
