using System;
using System.Collections.Generic;

namespace Puttshack.Domain
{
    /// <summary>Cosmetic slots a player can dress. Clubs are not a slot: the full bag is shared (spec section 17).</summary>
    public enum CosmeticSlot
    {
        Top,
        Bottom,
        Socks,
        Shoes,
        Belt,
        Bag,
    }

    /// <summary>
    /// One player's equipped cosmetics, stored on <see cref="PlayerState"/>. Appearance only: nothing in the rules,
    /// solver, meter or scoring reads it (spec sections 20 and 32).
    /// </summary>
    [Serializable]
    public sealed class Loadout
    {
        public string Top;
        public string Bottom;
        public string Socks;
        public string Shoes;
        public string Belt;
        public string Bag;
        /// <summary>Priced catalog items this player has taken home this session. Starter items are always owned.</summary>
        public List<string> Owned = new List<string>();

        public static readonly CosmeticSlot[] Slots =
        {
            CosmeticSlot.Top, CosmeticSlot.Bottom, CosmeticSlot.Socks, CosmeticSlot.Shoes, CosmeticSlot.Belt, CosmeticSlot.Bag,
        };

        public string Get(CosmeticSlot slot)
        {
            switch (slot)
            {
                case CosmeticSlot.Top: return Top;
                case CosmeticSlot.Bottom: return Bottom;
                case CosmeticSlot.Socks: return Socks;
                case CosmeticSlot.Shoes: return Shoes;
                case CosmeticSlot.Belt: return Belt;
                default: return Bag;
            }
        }

        public void Set(CosmeticSlot slot, string itemId)
        {
            switch (slot)
            {
                case CosmeticSlot.Top: Top = itemId; break;
                case CosmeticSlot.Bottom: Bottom = itemId; break;
                case CosmeticSlot.Socks: Socks = itemId; break;
                case CosmeticSlot.Shoes: Shoes = itemId; break;
                case CosmeticSlot.Belt: Belt = itemId; break;
                default: Bag = itemId; break;
            }
        }

        public Loadout Clone() => new Loadout
        {
            Top = Top,
            Bottom = Bottom,
            Socks = Socks,
            Shoes = Shoes,
            Belt = Belt,
            Bag = Bag,
            Owned = Owned != null ? new List<string>(Owned) : new List<string>(),
        };

        /// <summary>Same equipped items (ownership is bookkeeping and does not count as a visible change).</summary>
        public bool SameLook(Loadout other)
        {
            if (other == null) return false;
            foreach (CosmeticSlot slot in Slots)
            {
                if (Get(slot) != other.Get(slot)) return false;
            }
            return true;
        }

        public bool Owns(string itemId) => Owned != null && Owned.Contains(itemId);

        public override string ToString() => $"{Top}/{Bottom}/{Socks}/{Shoes}/{Belt}/{Bag}";
    }
}
