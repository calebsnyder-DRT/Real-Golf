using System;

namespace Puttshack.Domain
{
    /// <summary>Which golfer body a player is shown on: male is Caleb's model, female is Maya's.</summary>
    public enum Gender
    {
        Male,
        Female,
    }

    /// <summary>
    /// Who a player's golfer is (body, skin tone, hair colour, hair style), chosen in Player Setup and stored on
    /// <see cref="PlayerState"/> next to the name. Appearance only, like <see cref="Loadout"/>: nothing in the rules,
    /// solver, meter or scoring reads it. Ids index <c>Content.ProfileCatalog</c>.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public Gender Gender;
        public string Skin;
        public string HairColor;
        public string HairStyle;

        public PlayerProfile Clone() => new PlayerProfile
        {
            Gender = Gender,
            Skin = Skin,
            HairColor = HairColor,
            HairStyle = HairStyle,
        };

        public bool Same(PlayerProfile other) =>
            other != null && Gender == other.Gender && Skin == other.Skin && HairColor == other.HairColor && HairStyle == other.HairStyle;

        public override string ToString() => $"{Gender}/{Skin}/{HairColor}/{HairStyle}";
    }
}
