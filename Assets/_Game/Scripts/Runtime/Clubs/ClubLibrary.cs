using System;
using System.Collections.Generic;
using UnityEngine;

namespace Puttshack.Runtime.Clubs
{
    /// <summary>
    /// Club models and the sets players carry. A set picks a model per <see cref="Puttshack.Content.ClubCatalog"/> club id
    /// (e.g. blade or cavity irons, a hybrid in place of the 5 iron, blade / mallet / fang putter) plus a colourway. The
    /// nine gameplay clubs are unchanged; sets are cosmetic and assigned by roster colour (<see cref="SetFor"/>).
    /// Baked by Puttshack > Clubs > Bake Clubs.
    /// </summary>
    public sealed class ClubLibrary : ScriptableObject
    {
        public const string ResourcePath = "Clubs/ClubLibrary";

        [Serializable]
        public struct Slot
        {
            public string ClubId;
            public ClubModel Model;
        }

        [Serializable]
        public sealed class ClubSet
        {
            public string Id;
            public string DisplayName;
            public ClubPalette Palette;
            public List<Slot> Clubs = new List<Slot>();
        }

        public List<ClubModel> Models = new List<ClubModel>();
        public List<ClubPalette> Palettes = new List<ClubPalette>();
        public List<ClubSet> Sets = new List<ClubSet>();

        private static ClubLibrary _loaded;

        public static ClubLibrary Load()
        {
            if (_loaded == null) _loaded = Resources.Load<ClubLibrary>(ResourcePath);
            return _loaded;
        }

        /// <summary>Set carried by a roster colour (colour 0 Caleb, 1 Maya, ... wrapping over the sets).</summary>
        public ClubSet SetFor(int colorIndex)
        {
            if (Sets.Count == 0) return null;
            return Sets[((colorIndex % Sets.Count) + Sets.Count) % Sets.Count];
        }

        public ClubModel ModelFor(ClubSet set, string clubId)
        {
            if (set == null) return null;
            foreach (Slot s in set.Clubs)
            {
                if (s.ClubId == clubId) return s.Model;
            }
            return null;
        }

        /// <summary>Instantiates the set's model for a club under <paramref name="parent"/> (identity local pose), painted.</summary>
        public ClubModel Spawn(string clubId, ClubSet set, Transform parent)
        {
            ClubModel prefab = ModelFor(set, clubId);
            if (prefab == null) return null;
            ClubModel club = Instantiate(prefab, parent, false);
            club.name = prefab.name;
            club.Paint(set.Palette);
            return club;
        }
    }
}
