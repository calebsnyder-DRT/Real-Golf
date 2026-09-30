using Puttshack.Content;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>A catalog item dressed onto a wall hanger, shelf or mannequin. Painted at bake time from the catalog.</summary>
    public sealed class CosmeticDisplay : MonoBehaviour
    {
        public string ItemId;

        public void Paint(CosmeticArt art)
        {
            if (!CosmeticCatalog.TryGet(ItemId, out CosmeticItem item)) return;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) CosmeticPaint.Apply(r, item.Colors, art);
        }
    }
}
