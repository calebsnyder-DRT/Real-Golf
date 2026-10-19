using System;
using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    /// <summary>One skin tone, hair colour or hair style in Player Setup.</summary>
    public sealed class ProfileOption
    {
        public string Id;
        public string Name;
        /// <summary>sRGB albedo the option renders at (skin and hair colours); null for styles.</summary>
        public string Hex;
        /// <summary>Hair styles only: the hair part shown on either body, empty for none.</summary>
        public string Part;
    }

    /// <summary>
    /// Player Setup choices: body, skin tone, hair colour and hair style. Each body has native options (its own skin,
    /// hair colour and cut) that render with its untouched materials; any other skin or hair colour tints the textures
    /// toward <see cref="ProfileOption.Hex"/>. The native swatches are those textures' average albedo, so the palette is
    /// continuous across both bodies. Hair styles are the two hair meshes (each fitted to both heads) and shaved.
    /// </summary>
    public static class ProfileCatalog
    {
        public const string Version = "profiles-1.0";
        public const string PartShort = "Hair_Caleb";
        public const string PartLong = "Hair_Maya";

        public static readonly IReadOnlyList<ProfileOption> SkinTones = new List<ProfileOption>
        {
            Color("skin.porcelain", "Porcelain", "#F0C8B0"),
            Color("skin.light", "Light", "#DC9E7C"),
            Color("skin.warm", "Warm Beige", "#C98D68"),
            Color("skin.golden", "Golden", "#B07A55"),
            Color("skin.medium", "Medium", "#976449"),
            Color("skin.brown", "Brown", "#6B4231"),
            Color("skin.deep", "Deep", "#47291D"),
        };

        public static readonly IReadOnlyList<ProfileOption> HairColors = new List<ProfileOption>
        {
            Color("hair.black", "Black", "#0C0402"),
            Color("hair.darkbrown", "Dark Brown", "#23150C"),
            Color("hair.chestnut", "Chestnut", "#3E2214"),
            Color("hair.auburn", "Auburn", "#5C2010"),
            Color("hair.copper", "Copper", "#8A3C18"),
            Color("hair.honey", "Honey", "#8A6236"),
            Color("hair.blonde", "Blonde", "#B8955C"),
            Color("hair.silver", "Silver", "#9C9893"),
        };

        public static readonly IReadOnlyList<ProfileOption> HairStyles = new List<ProfileOption>
        {
            new ProfileOption { Id = "style.short", Name = "Short", Part = PartShort },
            new ProfileOption { Id = "style.long", Name = "Long", Part = PartLong },
            new ProfileOption { Id = "style.shaved", Name = "Shaved", Part = string.Empty },
        };

        public static readonly Gender[] Genders = { Gender.Male, Gender.Female };

        public static string GenderLabel(Gender gender) => gender == Gender.Female ? "FEMALE" : "MALE";

        /// <summary>The body's own skin, hair colour and cut: drawn with its materials exactly as baked.</summary>
        public static PlayerProfile Native(Gender gender) => gender == Gender.Female
            ? Profile(Gender.Female, "skin.medium", "hair.black", "style.long")
            : Profile(Gender.Male, "skin.light", "hair.darkbrown", "style.short");

        /// <summary>Starting golfer per roster slot: Caleb and Maya as themselves, then a mix of bodies, skin and hair.</summary>
        public static PlayerProfile DefaultFor(int rosterIndex)
        {
            switch (((rosterIndex % 6) + 6) % 6)
            {
                case 1: return Native(Gender.Female);
                case 2: return Profile(Gender.Male, "skin.brown", "hair.black", "style.short");
                case 3: return Profile(Gender.Female, "skin.light", "hair.blonde", "style.long");
                case 4: return Profile(Gender.Female, "skin.deep", "hair.black", "style.short");
                case 5: return Profile(Gender.Male, "skin.porcelain", "hair.copper", "style.short");
                default: return Native(Gender.Male);
            }
        }

        /// <summary>
        /// The player's profile, with the roster-slot default filling a missing profile (older journals) or any choice that
        /// has since left the catalog.
        /// </summary>
        public static PlayerProfile ProfileOf(PlayerState player)
        {
            PlayerProfile fallback = DefaultFor(player?.ColorIndex ?? 0);
            PlayerProfile p = player?.Profile;
            if (p == null) return fallback;
            return new PlayerProfile
            {
                Gender = Enum.IsDefined(typeof(Gender), p.Gender) ? p.Gender : fallback.Gender,
                Skin = Find(SkinTones, p.Skin) != null ? p.Skin : fallback.Skin,
                HairColor = Find(HairColors, p.HairColor) != null ? p.HairColor : fallback.HairColor,
                HairStyle = Find(HairStyles, p.HairStyle) != null ? p.HairStyle : fallback.HairStyle,
            };
        }

        /// <summary>Null when every choice is a known catalog option.</summary>
        public static string Validate(PlayerProfile profile)
        {
            if (profile == null) return "Missing profile.";
            if (!Enum.IsDefined(typeof(Gender), profile.Gender)) return $"Unknown gender '{profile.Gender}'.";
            if (Find(SkinTones, profile.Skin) == null) return $"Unknown skin tone '{profile.Skin}'.";
            if (Find(HairColors, profile.HairColor) == null) return $"Unknown hair colour '{profile.HairColor}'.";
            if (Find(HairStyles, profile.HairStyle) == null) return $"Unknown hair style '{profile.HairStyle}'.";
            return null;
        }

        public static ProfileOption Find(IReadOnlyList<ProfileOption> options, string id)
        {
            if (id == null) return null;
            foreach (ProfileOption o in options)
            {
                if (o.Id == id) return o;
            }
            return null;
        }

        public static int IndexOf(IReadOnlyList<ProfileOption> options, string id)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Id == id) return i;
            }
            return -1;
        }

        /// <summary>The option <paramref name="delta"/> steps from <paramref name="id"/>, wrapping at either end.</summary>
        public static string Step(IReadOnlyList<ProfileOption> options, string id, int delta)
        {
            int n = options.Count;
            int i = Math.Max(0, IndexOf(options, id));
            return options[((i + delta) % n + n) % n].Id;
        }

        public static bool IsNativeSkin(PlayerProfile p) => p != null && p.Skin == Native(p.Gender).Skin;

        public static bool IsNativeHairColor(PlayerProfile p) => p != null && p.HairColor == Native(p.Gender).HairColor;

        /// <summary>The hair part shown for a profile ("" for shaved).</summary>
        public static string HairPart(PlayerProfile p) => Find(HairStyles, p?.HairStyle)?.Part ?? Find(HairStyles, Native(p?.Gender ?? Gender.Male).HairStyle).Part;

        private static PlayerProfile Profile(Gender gender, string skin, string hairColor, string hairStyle) => new PlayerProfile
        {
            Gender = gender,
            Skin = skin,
            HairColor = hairColor,
            HairStyle = hairStyle,
        };

        private static ProfileOption Color(string id, string name, string hex) => new ProfileOption { Id = id, Name = name, Hex = hex };
    }
}
