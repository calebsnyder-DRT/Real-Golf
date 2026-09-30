using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>
    /// Dresses the modular golfer (one rig, one SkinnedMeshRenderer per part) from a <see cref="Loadout"/>: enables
    /// exactly the parts the items name and swaps their materials by role. Purely visual.
    /// Roster players without their own character model yet are shown as a stand-in: Caleb's rig with the display
    /// mannequin head and mannequin skin, so an outfit never borrows Caleb's face.
    /// </summary>
    public sealed class GolferAppearance : MonoBehaviour
    {
        public const string HeadPart = "Head_Caleb";
        private static readonly string[] AlwaysOn = { "Body_Arms", CosmeticCatalog.PartShoes };
        private const string MannequinSkinHex = "#E6DED0";

        public CosmeticArt Art;
        [Tooltip("Every part renderer under the rig (not the material swatch helper).")]
        public List<Renderer> Parts = new List<Renderer>();
        [Tooltip("Optional display head parented to the neck; shown for stand-ins and mannequins.")]
        public GameObject MannequinHead;
        [Tooltip("The model's own skin material, restored when a real character is shown.")]
        public Material Skin;

        public string AppliedKey { get; private set; }

        public void Collect()
        {
            Parts.Clear();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer && r.name != "Caleb_MaterialSwatches") Parts.Add(r);
                if (r.name == "Caleb_MaterialSwatches") r.gameObject.SetActive(false);
            }
            foreach (Renderer r in Parts)
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (Skin == null && CosmeticPaint.RoleOf(m) == "Skin") Skin = m;
                }
            }
        }

        public void Apply(Loadout look, bool standIn)
        {
            Loadout resolved = Resolve(look);
            var parts = new HashSet<string>(AlwaysOn);
            var colors = new Dictionary<string, string>();
            foreach (CosmeticSlot slot in Loadout.Slots)
            {
                if (slot == CosmeticSlot.Bag) continue;
                CosmeticItem item = CosmeticCatalog.Get(resolved.Get(slot));
                if (item.HasPart) parts.Add(item.Style);
                foreach (KeyValuePair<string, string> kv in item.Colors) colors[kv.Key] = kv.Value;
            }
            if (!standIn) parts.Add(HeadPart);

            Dictionary<string, Material> overrides = null;
            if (standIn) colors["Skin"] = MannequinSkinHex;
            else if (Skin != null) overrides = new Dictionary<string, Material> { ["Skin"] = Skin };

            foreach (Renderer r in Parts)
            {
                if (r == null) continue;
                bool on = parts.Contains(r.name);
                if (r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
                if (on) CosmeticPaint.Apply(r, colors, Art, overrides);
            }
            if (MannequinHead != null && MannequinHead.activeSelf != standIn) MannequinHead.SetActive(standIn);
            AppliedKey = Key(resolved, standIn);
        }

        /// <summary>Only Caleb (the first roster colour) has his own head so far; everyone else wears the mannequin.</summary>
        public static bool IsStandIn(PlayerState player) => player.ColorIndex != 0;

        public static string Key(Loadout look, bool standIn) =>
            $"{look.Top}|{look.Bottom}|{look.Socks}|{look.Shoes}|{look.Belt}|{(standIn ? "stand-in" : "own")}";

        private static Loadout Resolve(Loadout look)
        {
            if (look != null && CosmeticCatalog.Validate(look) == null) return look;
            return CosmeticCatalog.DefaultFor(0);
        }
    }
}
