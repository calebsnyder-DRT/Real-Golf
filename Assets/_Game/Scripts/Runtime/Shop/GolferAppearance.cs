using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>
    /// Dresses the modular golfers (one shared skeleton layout, one SkinnedMeshRenderer per part) from a
    /// <see cref="Loadout"/>: shows the roster member's own body, enables exactly the parts the items name and swaps their
    /// materials by role. Purely visual. Body 0 is the model this component sits on (Caleb); further characters live under
    /// child roots with their own Animator (<see cref="Extra"/>, index 1 = Maya). Roster players without their own
    /// character yet are shown as a stand-in: body 0 with the display mannequin head and mannequin skin, so an outfit never
    /// borrows someone else's face.
    /// </summary>
    public sealed class GolferAppearance : MonoBehaviour
    {
        public const string HeadPart = "Head_Caleb";
        public const string ClubPrefix = "Club_";
        /// <summary>Character index for roster players shown on the mannequin.</summary>
        public const int StandIn = -1;
        /// <summary>Roster colour index to character: colour 0 is Caleb, colour 1 is Maya.</summary>
        public static readonly string[] Characters = { "Caleb", "Maya" };
        private static readonly string[] AlwaysOn = { "Body_Arms", CosmeticCatalog.PartShoes };
        private const string MannequinSkinHex = "#E6DED0";

        [Serializable]
        public sealed class Body
        {
            public string Name;
            [Tooltip("Child root holding this body's rig and parts; inactive while another body is shown.")]
            public GameObject Root;
            public Animator Animator;
            [Tooltip("The model's own skin material, restored whenever the body is shown as itself.")]
            public Material Skin;
            public List<Renderer> Parts = new List<Renderer>();
        }

        public CosmeticArt Art;
        [Tooltip("Body 0's part renderers (the model this component sits on), clubs included.")]
        public List<Renderer> Parts = new List<Renderer>();
        [Tooltip("Optional display head parented to the neck; shown for stand-ins and mannequins.")]
        public GameObject MannequinHead;
        [Tooltip("Body 0's own skin material, restored when Caleb is shown.")]
        public Material Skin;
        [Tooltip("Further characters on the same skeleton layout; Extra[0] is character 1.")]
        public List<Body> Extra = new List<Body>();

        public string AppliedKey { get; private set; }
        /// <summary>Character index currently shown (StandIn for the mannequin).</summary>
        public int Shown { get; private set; }
        /// <summary>Club model shown in the hands ("Driver", "Iron", "Wedge", "Putter"), or null.</summary>
        public string HeldClub { get; private set; }

        public int BodyCount => 1 + Extra.Count;

        /// <summary>Body 0's renderers, skipping anything under an extra body's root.</summary>
        public void Collect()
        {
            Parts.Clear();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Caleb_MaterialSwatches") r.gameObject.SetActive(false);
                if (!(r is SkinnedMeshRenderer) || r.name == "Caleb_MaterialSwatches" || UnderExtra(r.transform)) continue;
                Parts.Add(r);
            }
            Skin = SkinOf(Parts) ?? Skin;
        }

        /// <summary>Registers (or refreshes) a further character under <paramref name="root"/>; returns its index.</summary>
        public int AddBody(string characterName, GameObject root)
        {
            Body body = Extra.Find(b => b.Name == characterName);
            if (body == null)
            {
                body = new Body { Name = characterName };
                Extra.Add(body);
            }
            body.Root = root;
            body.Animator = root.GetComponent<Animator>();
            body.Parts.Clear();
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) body.Parts.Add(r);
            body.Skin = SkinOf(body.Parts) ?? body.Skin;
            return 1 + Extra.IndexOf(body);
        }

        public Animator AnimatorOf(int character)
        {
            if (character >= 1 && character - 1 < Extra.Count) return Extra[character - 1].Animator;
            return GetComponent<Animator>();
        }

        public Animator ShownAnimator => AnimatorOf(Shown);

        /// <summary>The shown body's part renderers.</summary>
        public List<Renderer> ShownParts => Shown >= 1 && Shown - 1 < Extra.Count ? Extra[Shown - 1].Parts : Parts;

        public Renderer ShownPart(string partName)
        {
            foreach (Renderer r in ShownParts)
            {
                if (r != null && r.name == partName) return r;
            }
            return null;
        }

        public string ShownName => Shown >= 1 && Shown - 1 < Extra.Count ? Extra[Shown - 1].Name : Characters[0];

        public void Apply(Loadout look, bool standIn) => Apply(look, standIn ? StandIn : 0);

        public void Apply(Loadout look, int character)
        {
            Loadout resolved = Resolve(look);
            int body = character >= 0 && character < BodyCount ? character : StandIn;
            bool standIn = body == StandIn;
            int shown = standIn ? 0 : body;

            var parts = new HashSet<string>(AlwaysOn);
            var colors = new Dictionary<string, string>();
            foreach (CosmeticSlot slot in Loadout.Slots)
            {
                if (slot == CosmeticSlot.Bag) continue;
                CosmeticItem item = CosmeticCatalog.Get(resolved.Get(slot));
                if (item.HasPart) parts.Add(item.Style);
                foreach (KeyValuePair<string, string> kv in item.Colors) colors[kv.Key] = kv.Value;
            }
            string bodyName = shown == 0 ? Characters[0] : Extra[shown - 1].Name;
            if (!standIn) parts.Add("Head_" + bodyName);
            if (HeldClub != null) parts.Add(ClubPrefix + HeldClub);

            Material ownSkin = shown == 0 ? Skin : Extra[shown - 1].Skin;
            Dictionary<string, Material> overrides = null;
            if (standIn) colors["Skin"] = MannequinSkinHex;
            else if (ownSkin != null) overrides = new Dictionary<string, Material> { ["Skin"] = ownSkin };

            for (int i = 0; i < Extra.Count; i++)
            {
                GameObject root = Extra[i].Root;
                if (root != null && root.activeSelf != (shown == i + 1)) root.SetActive(shown == i + 1);
            }
            Animator own = GetComponent<Animator>();
            if (own != null) own.enabled = shown == 0;

            Paint(Parts, shown == 0, parts, colors, overrides);
            for (int i = 0; i < Extra.Count; i++) Paint(Extra[i].Parts, shown == i + 1, parts, colors, overrides);
            if (MannequinHead != null && MannequinHead.activeSelf != standIn) MannequinHead.SetActive(standIn);
            Shown = standIn ? StandIn : shown;
            AppliedKey = Key(resolved, Shown);
        }

        /// <summary>Shows one club model in the hands of the shown body (null puts it away). Clubs are the same for everyone.</summary>
        public void Hold(string clubModel)
        {
            HeldClub = clubModel;
            string wanted = clubModel != null ? ClubPrefix + clubModel : null;
            foreach (Renderer r in ShownParts)
            {
                if (r == null || !r.name.StartsWith(ClubPrefix, StringComparison.Ordinal)) continue;
                bool on = r.name == wanted;
                if (r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
            }
        }

        private void Paint(List<Renderer> renderers, bool visible, HashSet<string> parts, Dictionary<string, string> colors, Dictionary<string, Material> overrides)
        {
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                bool on = visible && parts.Contains(r.name);
                if (r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
                if (on) CosmeticPaint.Apply(r, colors, Art, overrides);
            }
        }

        private bool UnderExtra(Transform t)
        {
            foreach (Body b in Extra)
            {
                if (b.Root != null && t.IsChildOf(b.Root.transform)) return true;
            }
            return false;
        }

        private static Material SkinOf(List<Renderer> renderers)
        {
            foreach (Renderer r in renderers)
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (CosmeticPaint.RoleOf(m) == "Skin" && CosmeticPaint.BaseOf(m) != null) return m;
                }
            }
            return null;
        }

        /// <summary>Which character a roster player is shown as; colours without a character of their own are stand-ins.</summary>
        public static int CharacterOf(PlayerState player)
        {
            int c = player != null ? player.ColorIndex : StandIn;
            return c >= 0 && c < Characters.Length ? c : StandIn;
        }

        public static bool IsStandIn(PlayerState player) => CharacterOf(player) == StandIn;

        public static string Key(Loadout look, bool standIn) => Key(look, standIn ? StandIn : 0);

        public static string Key(Loadout look, int character) =>
            $"{look.Top}|{look.Bottom}|{look.Socks}|{look.Shoes}|{look.Belt}|{(character == StandIn ? "stand-in" : Characters[Mathf.Clamp(character, 0, Characters.Length - 1)])}";

        private static Loadout Resolve(Loadout look)
        {
            if (look != null && CosmeticCatalog.Validate(look) == null) return look;
            return CosmeticCatalog.DefaultFor(0);
        }
    }
}
