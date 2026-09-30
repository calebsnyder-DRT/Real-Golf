using System.Collections.Generic;
using Puttshack.Content;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>The plinth beside the golfer: shows the edited player's bag, or the focused club on its stand.</summary>
    public sealed class BagDisplay : MonoBehaviour
    {
        public CosmeticArt Art;
        public List<BagAppearance> Styles = new List<BagAppearance>();
        [Tooltip("Club models named Display_Club_<Kind>, shown instead of the bag on the Clubs tab.")]
        public List<GameObject> Clubs = new List<GameObject>();

        public string ShownItem { get; private set; }
        public string ShownClub { get; private set; }

        public void ShowBag(string itemId)
        {
            ShownClub = null;
            foreach (GameObject club in Clubs) club.SetActive(false);
            if (!CosmeticCatalog.TryGet(itemId, out CosmeticItem bag)) return;
            foreach (BagAppearance style in Styles)
            {
                bool on = style.Style == bag.Style;
                style.gameObject.SetActive(on);
                if (on) style.Apply(bag, Art);
            }
            ShownItem = itemId;
        }

        public void ShowClub(string clubId)
        {
            foreach (BagAppearance style in Styles) style.gameObject.SetActive(false);
            string kind = ClubKind(clubId);
            foreach (GameObject club in Clubs) club.SetActive(club.name.EndsWith(kind));
            ShownClub = clubId;
            ShownItem = null;
        }

        /// <summary>Which display model stands in for a club id (the shop has five club silhouettes).</summary>
        public static string ClubKind(string clubId)
        {
            switch (clubId)
            {
                case "driver": return "Driver";
                case "wood3": return "Wood";
                case "pw":
                case "sw":
                case "lw": return "Wedge";
                case ClubCatalog.PutterId: return "Putter";
                default: return "Iron";
            }
        }
    }
}
