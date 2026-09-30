using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>
    /// Unity-side bindings for <see cref="Content.CosmeticCatalog"/>: item/club/category icons and one baked material per
    /// (role, colour) pair so recolouring swaps shared assets instead of instancing. Rebuilt by
    /// <c>Puttshack > Pro Shop > Bake Pro Shop</c>; a colour the bake did not cover falls back to a runtime copy.
    /// </summary>
    [CreateAssetMenu(menuName = "Puttshack/Cosmetic Art", fileName = "CosmeticArt")]
    public sealed class CosmeticArt : ScriptableObject
    {
        [Serializable]
        public struct SpriteEntry
        {
            public string Key;
            public Sprite Sprite;
        }

        [Serializable]
        public struct MaterialEntry
        {
            public string Key;
            public Material Material;
        }

        [Tooltip("Keyed by cosmetic item id (top.polo.royal) or club.<clubId>.")]
        public List<SpriteEntry> Icons = new List<SpriteEntry>();
        [Tooltip("Keyed by category: Top, Bottom, Socks, Shoes, Belt, Bag, Clubs.")]
        public List<SpriteEntry> CategoryIcons = new List<SpriteEntry>();
        [Tooltip("Keyed by MaterialKey(role, hex).")]
        public List<MaterialEntry> Materials = new List<MaterialEntry>();
        [Tooltip("Copied for colours missing from Materials.")]
        public Material Template;

        private Dictionary<string, Sprite> _icons, _categories;
        private Dictionary<string, Material> _materials;

        public static string MaterialKey(string role, string hex) => role + "@" + hex.TrimStart('#').ToUpperInvariant();

        public Sprite Icon(string key) => Lookup(ref _icons, Icons, key);

        public Sprite CategoryIcon(string key) => Lookup(ref _categories, CategoryIcons, key);

        public Material MaterialFor(string role, string hex)
        {
            if (_materials == null)
            {
                _materials = new Dictionary<string, Material>();
                foreach (MaterialEntry e in Materials)
                {
                    if (e.Material != null && !string.IsNullOrEmpty(e.Key)) _materials[e.Key] = e.Material;
                }
            }
            string key = MaterialKey(role, hex);
            if (_materials.TryGetValue(key, out Material found)) return found;
            if (Template == null) return null;
            var made = new Material(Template) { name = key };
            made.SetColor("_BaseColor", ParseHex(hex));
            _materials[key] = made;
            return made;
        }

        /// <summary>Drops lookup caches after the baker edits the lists.</summary>
        public void Invalidate()
        {
            _icons = _categories = null;
            _materials = null;
        }

        private static Sprite Lookup(ref Dictionary<string, Sprite> cache, List<SpriteEntry> list, string key)
        {
            if (cache == null)
            {
                cache = new Dictionary<string, Sprite>();
                foreach (SpriteEntry e in list)
                {
                    if (e.Sprite != null && !string.IsNullOrEmpty(e.Key)) cache[e.Key] = e.Sprite;
                }
            }
            return key != null && cache.TryGetValue(key, out Sprite s) ? s : null;
        }

        public static Color ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.magenta;
            int rgb = int.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Palette.Hex(rgb);
        }
    }

    /// <summary>
    /// Recolours renderers by material role. Roles come from material names: <c>Mat_Top_Blue</c> is role <c>Top</c>
    /// (golfer convention), <c>Bag_Body</c> is itself (bag convention), and a baked <c>Top@2F6BD8</c> keeps its role,
    /// so a renderer can be repainted any number of times.
    /// </summary>
    public static class CosmeticPaint
    {
        public static string RoleOf(Material material)
        {
            if (material == null) return null;
            string name = material.name;
            int instance = name.IndexOf(" (Instance)", StringComparison.Ordinal);
            if (instance >= 0) name = name.Substring(0, instance);
            int at = name.IndexOf('@');
            if (at >= 0) return name.Substring(0, at);
            if (name.StartsWith("Mat_", StringComparison.Ordinal))
            {
                int end = name.IndexOf('_', 4);
                return end > 4 ? name.Substring(4, end - 4) : name.Substring(4);
            }
            int dot = name.LastIndexOf('.');
            if (dot > 0 && dot < name.Length - 1 && char.IsDigit(name[dot + 1])) name = name.Substring(0, dot);
            return name;
        }

        /// <summary>Swaps every material whose role has a colour in <paramref name="colors"/>; others are left alone.</summary>
        public static void Apply(Renderer renderer, IReadOnlyDictionary<string, string> colors, CosmeticArt art, IReadOnlyDictionary<string, Material> overrides = null)
        {
            if (renderer == null) return;
            Material[] mats = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                string role = RoleOf(mats[i]);
                if (role == null) continue;
                Material next = null;
                if (overrides != null && overrides.TryGetValue(role, out Material fixedMat)) next = fixedMat;
                else if (colors != null && art != null && colors.TryGetValue(role, out string hex) && hex != null) next = art.MaterialFor(role, hex);
                if (next != null && next != mats[i])
                {
                    mats[i] = next;
                    changed = true;
                }
            }
            if (changed) renderer.sharedMaterials = mats;
        }
    }
}
