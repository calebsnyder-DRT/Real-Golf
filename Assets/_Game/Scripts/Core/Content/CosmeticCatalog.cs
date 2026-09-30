using System;
using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    /// <summary>
    /// One purchasable or starter look in the Pro Shop. <see cref="Style"/> names the modular part to enable
    /// (e.g. <c>Top_Polo</c>, <c>Bottom_Shorts</c>, <c>GolfBag_Tour</c>; empty = no part), and <see cref="Colors"/>
    /// maps a material role (<c>Top</c>, <c>Collar</c>, <c>Bag_Body</c> ...) to an sRGB hex colour.
    /// </summary>
    public sealed class CosmeticItem
    {
        public string Id;
        public CosmeticSlot Slot;
        public string Name;
        public string Style;
        public string StyleLabel;
        /// <summary>Display-only shop price in local credits. 0 = starter item every player already owns.</summary>
        public int Price;
        public IReadOnlyDictionary<string, string> Colors;
        public string Blurb;

        public bool Starter => Price == 0;
        public bool HasPart => !string.IsNullOrEmpty(Style);

        /// <summary>Primary swatch colour for cards and the equipped summary.</summary>
        public string Swatch
        {
            get
            {
                foreach (string role in SwatchRoles)
                {
                    if (Colors.TryGetValue(role, out string hex)) return hex;
                }
                return "#808080";
            }
        }

        public string Color(string role) => Colors.TryGetValue(role, out string hex) ? hex : null;

        private static readonly string[] SwatchRoles = { "Top", "Bottom", "Socks", "Shoes", "Belt", "Bag_Body" };
    }

    /// <summary>
    /// Pro Shop cosmetics (spec sections 17, 20, 30): clothing parts and colours for the modular golfer plus bag skins.
    /// Bags are styles and colourways only; every player always carries the full nine-club bag from
    /// <see cref="ClubCatalog"/>. Prices are local display values; there are no payments or accounts.
    /// </summary>
    public static class CosmeticCatalog
    {
        public const string Version = "cosmetics-1.0";
        public const string PriceUnit = "CR";

        public const string PartPolo = "Top_Polo";
        public const string PartTee = "Top_Tee";
        public const string PartChinos = "Bottom_Chinos";
        public const string PartShorts = "Bottom_Shorts";
        public const string PartSocksAnkle = "Socks_Ankle";
        public const string PartSocksCrew = "Socks_Crew";
        public const string PartShoes = "Shoes_Golf";
        public const string PartBelt = "Belt";
        public const string BagStand = "GolfBag_Stand";
        public const string BagCart = "GolfBag_Cart";
        public const string BagTour = "GolfBag_Tour";

        private const string Cyan = "#00CBE7";
        private const string Ink = "#192020";
        private const string Cream = "#F2EDDF";
        private const string CollarWhite = "#F7F4EC";
        private const string Sole = "#2B2D30";
        private const string Buckle = "#D9DCDF";
        private const string BagMetal = "#C9CCD0";

        public static readonly IReadOnlyList<CosmeticItem> All = Build();

        private static readonly Dictionary<string, CosmeticItem> ById = Index(All);

        public static CosmeticItem Get(string id)
        {
            if (id != null && ById.TryGetValue(id, out CosmeticItem item)) return item;
            throw new ArgumentException($"Unknown cosmetic '{id}'.", nameof(id));
        }

        public static bool TryGet(string id, out CosmeticItem item)
        {
            item = null;
            return id != null && ById.TryGetValue(id, out item);
        }

        public static List<CosmeticItem> ForSlot(CosmeticSlot slot)
        {
            var items = new List<CosmeticItem>();
            foreach (CosmeticItem item in All)
            {
                if (item.Slot == slot) items.Add(item);
            }
            return items;
        }

        public static string SlotLabel(CosmeticSlot slot)
        {
            switch (slot)
            {
                case CosmeticSlot.Top: return "TOP";
                case CosmeticSlot.Bottom: return "BOTTOMS";
                case CosmeticSlot.Socks: return "SOCKS";
                case CosmeticSlot.Shoes: return "SHOES";
                case CosmeticSlot.Belt: return "BELT";
                default: return "BAG";
            }
        }

        /// <summary>
        /// Starting look per roster identity (spec section 30 character bible; Caleb has no cap by user decision).
        /// Identity colour stays separate from outfit colour, so any player can wear any item.
        /// </summary>
        public static Loadout DefaultFor(int colorIndex)
        {
            switch (((colorIndex % 6) + 6) % 6)
            {
                case 1: return Look("top.polo.violet", "bottom.chinos.white", "socks.noshow.white", "shoes.white.white", "belt.white");
                case 2: return Look("top.polo.green", "bottom.chinos.khaki", "socks.noshow.black", "shoes.white.black", "belt.black");
                case 3: return Look("top.polo.gold", "bottom.chinos.navy", "socks.noshow.white", "shoes.white.cyan", "belt.brown");
                case 4: return Look("top.polo.violet", "bottom.shorts.grey", "socks.ankle.white", "shoes.white.white", "belt.black");
                case 5: return Look("top.polo.teal", "bottom.shorts.khaki", "socks.crew.white", "shoes.white.cyan", "belt.none");
                default: return Look("top.polo.royal", "bottom.chinos.grey", "socks.noshow.white", "shoes.white.cyan", "belt.black");
            }
        }

        /// <summary>
        /// The player's saved look, with the identity default filling a missing loadout (older journals) or any slot whose
        /// item has since left the catalog.
        /// </summary>
        public static Loadout LoadoutOf(PlayerState player)
        {
            Loadout fallback = DefaultFor(player?.ColorIndex ?? 0);
            if (player?.Loadout == null) return fallback;
            Loadout resolved = player.Loadout.Clone();
            foreach (CosmeticSlot slot in Loadout.Slots)
            {
                if (!TryGet(resolved.Get(slot), out CosmeticItem item) || item.Slot != slot) resolved.Set(slot, fallback.Get(slot));
            }
            resolved.Owned.RemoveAll(id => !TryGet(id, out _));
            return resolved;
        }

        private static Loadout Look(string top, string bottom, string socks, string shoes, string belt) => new Loadout
        {
            Top = top,
            Bottom = bottom,
            Socks = socks,
            Shoes = shoes,
            Belt = belt,
            Bag = "bag.tour.classic",
        };

        /// <summary>Loadout with every slot filled by a valid item of that slot; returns null when it is.</summary>
        public static string Validate(Loadout loadout)
        {
            if (loadout == null) return "Missing loadout.";
            foreach (CosmeticSlot slot in Loadout.Slots)
            {
                string id = loadout.Get(slot);
                if (!TryGet(id, out CosmeticItem item)) return $"Unknown {SlotLabel(slot).ToLowerInvariant()} '{id}'.";
                if (item.Slot != slot) return $"'{id}' is not a {SlotLabel(slot).ToLowerInvariant()} item.";
            }
            if (loadout.Owned != null)
            {
                foreach (string id in loadout.Owned)
                {
                    if (!TryGet(id, out _)) return $"Unknown owned item '{id}'.";
                }
            }
            return null;
        }

        /// <summary>Starter items and anything already taken home count as owned.</summary>
        public static bool IsOwned(Loadout loadout, string itemId) =>
            TryGet(itemId, out CosmeticItem item) && (item.Starter || (loadout != null && loadout.Owns(itemId)));

        /// <summary>Saving marks every priced item that is now equipped as owned for the session.</summary>
        public static Loadout WithEquippedOwned(Loadout loadout)
        {
            Loadout copy = loadout.Clone();
            foreach (CosmeticSlot slot in Loadout.Slots)
            {
                string id = copy.Get(slot);
                if (TryGet(id, out CosmeticItem item) && !item.Starter && !copy.Owned.Contains(id)) copy.Owned.Add(id);
            }
            return copy;
        }

        // ---------------------------------------------------------------- Data

        private static List<CosmeticItem> Build()
        {
            var items = new List<CosmeticItem>
            {
                Polo("royal", "Royal Blue", 0, "#2F6BD8", CollarWhite, Cyan),
                Polo("sky", "Sky Blue", 120, "#86B8E8", "#86B8E8", Cyan),
                Polo("green", "Course Green", 0, "#3E9B4F", CollarWhite, Cyan),
                Polo("coral", "Sunset Coral", 120, "#E86A58", "#E86A58", Cream),
                Polo("cream", "Clubhouse Cream", 120, "#EFE6CF", "#EFE6CF", "#008CA1"),
                Polo("midnight", "Midnight", 150, "#26304A", "#26304A", Cyan),
                Polo("violet", "Violet", 0, "#7B4FC9", CollarWhite, "#F28C28"),
                Polo("gold", "Clubhouse Gold", 0, "#E3B23C", "#E3B23C", Ink),
                Polo("teal", "Lagoon Teal", 0, "#1FA3A0", CollarWhite, Cream),
                Polo("white", "Tour White", 100, "#F4F2EC", "#28324A", Cyan),
                Tee("white", "White", 0, "#F4F2EC", Cyan),
                Tee("heather", "Heather Grey", 80, "#A9ADB2", Cyan),
                Tee("coral", "Coral", 80, "#E86A58", Cream),
                Tee("navy", "Navy", 80, "#28324A", Cyan),

                Bottom("chinos", "grey", "Light Grey", 0, "#B8BCC1"),
                Bottom("chinos", "khaki", "Khaki", 0, "#CDB68A"),
                Bottom("chinos", "navy", "Navy", 0, "#28324A"),
                Bottom("chinos", "white", "White", 0, "#EDEBE4"),
                Bottom("chinos", "black", "Black", 110, "#2A2C2F"),
                Bottom("shorts", "khaki", "Khaki", 0, "#CDB68A"),
                Bottom("shorts", "grey", "Grey", 0, "#8C9096"),
                Bottom("shorts", "navy", "Navy", 90, "#28324A"),
                Bottom("shorts", "coral", "Coral", 90, "#E86A58"),
                Bottom("shorts", "white", "White", 90, "#EDEBE4"),

                Socks("noshow", "white", "No-Show White", 0, "#F4F4F0"),
                Socks("noshow", "black", "No-Show Black", 0, "#27282B"),
                Socks("ankle", "white", "Ankle White", 0, "#F4F4F0"),
                Socks("ankle", "black", "Ankle Black", 40, "#27282B"),
                Socks("crew", "white", "Crew White", 0, "#F4F4F0"),
                Socks("crew", "navy", "Crew Navy", 50, "#28324A"),
                Socks("crew", "cyan", "Crew Cyan", 50, Cyan),

                Shoes("white.cyan", "White Spikeless", 0, "#F5F5F2", Cyan, Sole),
                Shoes("white.white", "Tour White", 0, "#F5F5F2", "#F5F5F2", "#D9D6CE"),
                Shoes("white.black", "White / Black", 0, "#F5F5F2", "#27282B", Sole),
                Shoes("black.white", "Black Tour", 140, "#27282B", "#F5F5F2", Sole),
                Shoes("black.grey", "Stealth Black", 160, "#27282B", "#9A9DA3", "#1D1E20"),

                Belt("none", "No Belt", 0, null),
                Belt("brown", "Brown Leather", 0, "#5A3A22"),
                Belt("black", "Black Leather", 0, "#1F1F21"),
                Belt("white", "White Tour", 60, "#EDEDEA"),

                Bag("tour.classic", "Puttshack Classic", BagTour, 0, Cream, Ink, Cyan, Ink, "#FBF8F0", Ink, Cyan,
                    "Cream tour staff bag with ink trim. Included with every round."),
                Bag("stand.midnight", "Midnight Stand", BagStand, 350, "#1E2426", "#2F3A3D", Cyan, Ink, "#2F3A3D", "#1E2426", Cyan,
                    "Slim carry bag with fold-out legs."),
                Bag("stand.sunset", "Sunset Stand", BagStand, 350, "#E8872A", Ink, Cream, Ink, Cream, "#E8872A", Cream,
                    "Slim carry bag in sunset orange."),
                Bag("stand.lagoon", "Lagoon Stand", BagStand, 350, "#1FA59A", Cream, Ink, Ink, Cream, "#1FA59A", Cream,
                    "Slim carry bag in lagoon teal."),
                Bag("stand.clubhouse", "Clubhouse White", BagStand, 350, "#F4F2EC", "#2F6FD6", Ink, "#2F6FD6", "#F4F2EC", "#2F6FD6", "#F4F2EC",
                    "Crisp white carry bag with royal blue trim."),
                Bag("cart.fairway", "Fairway Cart", BagCart, 420, "#2F6B45", Cream, "#D9B021", "#3B2A1E", Cream, "#2F6B45", "#D9B021",
                    "Roomy cart bag in fairway green."),
                Bag("cart.harbor", "Harbor Cart", BagCart, 420, "#28324A", "#E86A58", "#F4F2EC", "#28324A", "#F4F2EC", "#28324A", "#E86A58",
                    "Roomy cart bag in navy and coral."),
                Bag("tour.champion", "Champion Tour", BagTour, 600, "#D9B021", Ink, Cream, Ink, Cream, Ink, "#D9B021",
                    "Gold tour staff bag."),
                Bag("tour.twilight", "Twilight Tour", BagTour, 600, "#8A4FC7", Cream, Cyan, Ink, Cream, "#8A4FC7", Cyan,
                    "Violet tour staff bag."),
            };
            return items;
        }

        private static Dictionary<string, CosmeticItem> Index(IReadOnlyList<CosmeticItem> items)
        {
            var map = new Dictionary<string, CosmeticItem>();
            foreach (CosmeticItem item in items)
            {
                if (map.ContainsKey(item.Id)) throw new InvalidOperationException($"Duplicate cosmetic id '{item.Id}'.");
                map[item.Id] = item;
            }
            return map;
        }

        private static CosmeticItem Polo(string key, string name, int price, string body, string collar, string logo) => new CosmeticItem
        {
            Id = "top.polo." + key,
            Slot = CosmeticSlot.Top,
            Name = name,
            Style = PartPolo,
            StyleLabel = "POLO",
            Price = price,
            Colors = new Dictionary<string, string> { ["Top"] = body, ["Collar"] = collar, ["Logo"] = logo },
        };

        private static CosmeticItem Tee(string key, string name, int price, string body, string logo) => new CosmeticItem
        {
            Id = "top.tee." + key,
            Slot = CosmeticSlot.Top,
            Name = name,
            Style = PartTee,
            StyleLabel = "TEE",
            Price = price,
            Colors = new Dictionary<string, string> { ["Top"] = body, ["Logo"] = logo },
        };

        private static CosmeticItem Bottom(string cut, string key, string name, int price, string color) => new CosmeticItem
        {
            Id = $"bottom.{cut}.{key}",
            Slot = CosmeticSlot.Bottom,
            Name = name,
            Style = cut == "shorts" ? PartShorts : PartChinos,
            StyleLabel = cut == "shorts" ? "SHORTS" : "CHINOS",
            Price = price,
            Colors = new Dictionary<string, string> { ["Bottom"] = color },
        };

        private static CosmeticItem Socks(string cut, string key, string name, int price, string color) => new CosmeticItem
        {
            Id = $"socks.{cut}.{key}",
            Slot = CosmeticSlot.Socks,
            Name = name,
            Style = cut == "crew" ? PartSocksCrew : cut == "ankle" ? PartSocksAnkle : string.Empty,
            StyleLabel = "SOCKS",
            Price = price,
            Colors = new Dictionary<string, string> { ["Socks"] = color },
        };

        private static CosmeticItem Shoes(string key, string name, int price, string upper, string accent, string sole) => new CosmeticItem
        {
            Id = "shoes." + key,
            Slot = CosmeticSlot.Shoes,
            Name = name,
            Style = PartShoes,
            StyleLabel = "SHOES",
            Price = price,
            Colors = new Dictionary<string, string> { ["Shoes"] = upper, ["ShoeAccent"] = accent, ["Sole"] = sole },
        };

        private static CosmeticItem Belt(string key, string name, int price, string color) => new CosmeticItem
        {
            Id = "belt." + key,
            Slot = CosmeticSlot.Belt,
            Name = name,
            Style = color == null ? string.Empty : PartBelt,
            StyleLabel = "BELT",
            Price = price,
            Colors = color == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["Belt"] = color, ["Buckle"] = Buckle },
        };

        private static CosmeticItem Bag(string key, string name, string style, int price, string body, string accent, string trim, string strap,
            string panel, string headCover, string headCoverAccent, string blurb) => new CosmeticItem
        {
            Id = "bag." + key,
            Slot = CosmeticSlot.Bag,
            Name = name,
            Style = style,
            StyleLabel = style == BagStand ? "STAND BAG" : style == BagCart ? "CART BAG" : "TOUR BAG",
            Price = price,
            Blurb = blurb,
            Colors = new Dictionary<string, string>
            {
                ["Bag_Body"] = body,
                ["Bag_Accent"] = accent,
                ["Bag_Trim"] = trim,
                ["Bag_Strap"] = strap,
                ["Bag_Panel"] = panel,
                ["Bag_Metal"] = BagMetal,
                ["HeadCover"] = headCover,
                ["HeadCover_Accent"] = headCoverAccent,
            },
        };
    }
}
