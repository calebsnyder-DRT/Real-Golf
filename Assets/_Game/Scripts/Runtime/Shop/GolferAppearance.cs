using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>
    /// Dresses the modular golfers (one shared skeleton layout, one SkinnedMeshRenderer per part) from a
    /// <see cref="Loadout"/> and a <see cref="PlayerProfile"/>: shows the profile's body, enables exactly the parts the
    /// items name and swaps their materials by role, then shows the chosen hair part and tints skin and hair toward the
    /// chosen tones. Purely visual. Body 0 is the model this component sits on (Caleb, the male body); further characters
    /// live under child roots with their own Animator (<see cref="Extra"/>, index 1 = Maya, the female body). If a body has
    /// not been baked the player is shown as a stand-in: body 0 with the display mannequin head and mannequin skin.
    /// </summary>
    public sealed class GolferAppearance : MonoBehaviour
    {
        public const string HeadPart = "Head_Caleb";
        public const string ClubPrefix = "Club_";
        public const string HairPrefix = "Hair_";
        /// <summary>A character's own head, built in pieces: <c>Head_&lt;Name&gt;</c> (face skin), eyes, brows, lashes and hair.</summary>
        public static readonly string[] HeadPieces = { "Head_", "Eyes_", "Brows_", "Lashes_", HairPrefix };
        /// <summary>Character index for roster players shown on the mannequin.</summary>
        public const int StandIn = -1;
        /// <summary>Character index by <see cref="Gender"/>: male is Caleb's body, female is Maya's.</summary>
        public static readonly string[] Characters = { "Caleb", "Maya" };
        private static readonly string[] AlwaysOn = { "Body_Arms", CosmeticCatalog.PartShoes };
        private const string MannequinSkinHex = "#E6DED0";
        /// <summary>Largest per-channel tint: lifting black hair to blonde needs about 100x its texture.</summary>
        private const float MaxTint = 160f;
        private const float MaxHairSpec = 0.5f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int HairSpecId = Shader.PropertyToID("_SpecColorB");
        private static MaterialPropertyBlock _block;

        /// <summary>Average albedo of a skin or hair material, baked by GolferLookBaker; tints are ratios against it.</summary>
        [Serializable]
        public sealed class Tone
        {
            public Material Material;
            [Tooltip("Linear average albedo: the texture at the mesh's UVs (alpha-weighted) times the base colour.")]
            public Color Average;
            [Tooltip("Profile option this material renders untouched (its own body's skin tone or hair colour).")]
            public string NativeId;
        }

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
        [Tooltip("Skin and hair materials' average albedo (Puttshack > Characters > Bake Hair Styles).")]
        public List<Tone> Tones = new List<Tone>();

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

        /// <summary>Dresses the profile's body with its skin, hair colour and hair style.</summary>
        public void Apply(Loadout look, PlayerProfile profile) => Apply(look, CharacterOf(profile), profile);

        /// <summary>A null <paramref name="profile"/> shows the character as itself (its own skin, hair and cut).</summary>
        public void Apply(Loadout look, int character, PlayerProfile profile = null)
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
            List<Renderer> bodyParts = shown == 0 ? Parts : Extra[shown - 1].Parts;
            if (!standIn)
            {
                foreach (string piece in HeadPieces)
                {
                    if (piece != HairPrefix) parts.Add(piece + bodyName);
                }
                string hair = HairPartOf(profile, bodyName, bodyParts);
                if (hair.Length > 0) parts.Add(hair);
            }
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
            Tint(bodyParts, standIn ? null : profile, bodyName, ownSkin);
            if (MannequinHead != null && MannequinHead.activeSelf != standIn) MannequinHead.SetActive(standIn);
            Shown = standIn ? StandIn : shown;
            AppliedKey = Key(resolved, Shown, standIn ? null : profile);
        }

        /// <summary>The profile's hair part, or the body's own hair if that cut has not been fitted to this body.</summary>
        private static string HairPartOf(PlayerProfile profile, string bodyName, List<Renderer> bodyParts)
        {
            string own = HairPrefix + bodyName;
            if (profile == null) return own;
            string part = ProfileCatalog.HairPart(profile);
            if (part.Length == 0) return part;
            return bodyParts.Exists(r => r != null && r.name == part) ? part : own;
        }

        /// <summary>
        /// Per material slot (so a garment sharing a renderer with skin keeps its own colour): skin slots and the face take
        /// the skin tone, hair the hair colour, brows a softened version of it. Each tint is the target over the material's
        /// baked <see cref="Tone"/>, so the texture's detail is kept; an option a material already renders, or a null
        /// profile, clears the slot back to the material as baked.
        /// </summary>
        private void Tint(List<Renderer> renderers, PlayerProfile profile, string bodyName, Material ownSkin)
        {
            ProfileOption skin = profile != null ? ProfileCatalog.Find(ProfileCatalog.SkinTones, profile.Skin) : null;
            ProfileOption hair = profile != null ? ProfileCatalog.Find(ProfileCatalog.HairColors, profile.HairColor) : null;
            Vector3? skinTint = TintOf(ToneOf(ownSkin), skin);
            Renderer ownHair = renderers.Find(r => r != null && r.name == HairPrefix + bodyName);
            Vector3? browTint = TintOf(ToneOf(ownHair != null ? ownHair.sharedMaterial : null), hair);
            if (browTint is Vector3 b) browTint = new Vector3(Mathf.Sqrt(b.x), Mathf.Sqrt(b.y), Mathf.Sqrt(b.z));

            foreach (Renderer r in renderers)
            {
                if (r == null || !r.gameObject.activeSelf) continue;
                bool isHair = r.name.StartsWith(HairPrefix, StringComparison.Ordinal);
                bool isBrow = r.name.StartsWith("Brows_", StringComparison.Ordinal);
                bool isFace = r.name.StartsWith("Head_", StringComparison.Ordinal);
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (isHair)
                    {
                        Tone tone = ToneOf(m);
                        SetTint(r, i, m, TintOf(tone, hair), HairSpec(m, tone, hair));
                    }
                    else if (isBrow) SetTint(r, i, m, browTint, null);
                    else if (isFace || CosmeticPaint.RoleOf(m) == "Skin") SetTint(r, i, m, skinTint, null);
                }
            }
        }

        private Tone ToneOf(Material m) => m != null ? Tones.Find(t => t.Material == m) : null;

        /// <summary>Linear per-channel multiplier taking the tone's average to the option's colour; null leaves it as baked.</summary>
        private static Vector3? TintOf(Tone tone, ProfileOption option)
        {
            if (tone == null || option?.Hex == null || option.Id == tone.NativeId) return null;
            if (!ColorUtility.TryParseHtmlString(option.Hex, out Color target)) return null;
            Color t = target.linear;
            return new Vector3(Ratio(t.r, tone.Average.r), Ratio(t.g, tone.Average.g), Ratio(t.b, tone.Average.b));
        }

        private static float Ratio(float target, float average) => Mathf.Clamp(target / Mathf.Max(average, 1e-4f), 0.02f, MaxTint);

        /// <summary>The hair's secondary (coloured) highlight follows the new colour at the material's highlight strength.</summary>
        private static Vector4? HairSpec(Material m, Tone tone, ProfileOption option)
        {
            if (m == null || !m.HasProperty(HairSpecId) || TintOf(tone, option) == null) return null;
            ColorUtility.TryParseHtmlString(option.Hex, out Color target);
            Color t = target.linear, spec = m.GetColor(HairSpecId);
            Color own = spec.linear;
            float gain = Luma(own) / Mathf.Max(Luma(tone.Average), 1e-4f);
            Vector3 c = new Vector3(t.r, t.g, t.b) * gain;
            float peak = Mathf.Max(c.x, Mathf.Max(c.y, c.z));
            if (peak > MaxHairSpec) c *= MaxHairSpec / peak;
            return new Vector4(c.x, c.y, c.z, spec.a);
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static void SetTint(Renderer r, int index, Material m, Vector3? tint, Vector4? spec)
        {
            if (tint == null || m == null || !m.HasProperty(BaseColorId))
            {
                r.SetPropertyBlock(null, index);
                return;
            }
            _block ??= new MaterialPropertyBlock();
            _block.Clear();
            Color own = m.GetColor(BaseColorId).linear;
            Vector3 k = tint.Value;
            // Raw linear values: SetColor would gamma-convert, and the tint may need the base colour above 1.
            _block.SetVector(BaseColorId, new Vector4(own.r * k.x, own.g * k.y, own.b * k.z, own.a));
            if (spec != null) _block.SetVector(HairSpecId, spec.Value);
            r.SetPropertyBlock(_block, index);
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

        /// <summary>Which character a roster player is shown as: the body of their profile's gender.</summary>
        public static int CharacterOf(PlayerState player) => player != null ? CharacterOf(ProfileCatalog.ProfileOf(player)) : StandIn;

        public static int CharacterOf(PlayerProfile profile) => profile != null && profile.Gender == Gender.Female ? 1 : 0;

        public static bool IsStandIn(PlayerState player) => CharacterOf(player) == StandIn;

        public static string Key(Loadout look, bool standIn) => Key(look, standIn ? StandIn : 0);

        public static string Key(Loadout look, int character, PlayerProfile profile = null) =>
            $"{look.Top}|{look.Bottom}|{look.Socks}|{look.Shoes}|{look.Belt}|{(character == StandIn ? "stand-in" : Characters[Mathf.Clamp(character, 0, Characters.Length - 1)])}|{profile?.ToString() ?? "own"}";

        private static Loadout Resolve(Loadout look)
        {
            if (look != null && CosmeticCatalog.Validate(look) == null) return look;
            return CosmeticCatalog.DefaultFor(0);
        }
    }
}
