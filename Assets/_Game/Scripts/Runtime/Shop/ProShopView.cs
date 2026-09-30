using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.Runtime.Shop
{
    [Serializable]
    public sealed class ShopButton
    {
        public RectTransform Root;
        public Image Fill;
        public Image Frame;
        public Image Glow;
        public Image Icon;
        public TMP_Text Label;
        public Button Button;
    }

    [Serializable]
    public sealed class ShopRow
    {
        public RectTransform Root;
        public Image Fill;
        public Image Frame;
        public Image Glow;
        public Image Icon;
        public TMP_Text Name;
        public TMP_Text Detail;
        public Image Check;
        public Button Button;
    }

    [Serializable]
    public sealed class ShopSummaryRow
    {
        public GameObject Root;
        public Image Icon;
        public TMP_Text Label;
        public TMP_Text Value;
        public Image Changed;
    }

    [Serializable]
    public sealed class ShopStatRow
    {
        public TMP_Text Label;
        public RectTransform Fill;
    }

    [Serializable]
    public sealed class ShopPlayerTab
    {
        public RectTransform Root;
        public RawImage Portrait;
        public Image Frame;
        public Image Glow;
        public Image NameBar;
        public TMP_Text Name;
        public GameObject TurnBadge;
        public Image TurnFill;
        public TMP_Text TurnText;
        public GameObject Unsaved;
        public Button Button;
    }

    public struct ShopRowModel
    {
        public string Name;
        public string Detail;
        public Sprite Icon;
        public bool Focused;
        public bool ZoneActive;
        public bool Equipped;
        public bool ForSale;
    }

    public struct ShopPlayerModel
    {
        public string Name;
        public Color Identity;
        public Texture Portrait;
        public bool Edited;
        public bool Focused;
        public bool Turn;
        public bool Unsaved;
    }

    /// <summary>
    /// Pro Shop / Player Loadout screen (brief p.21 and p.33 references). The hierarchy is baked by
    /// <c>Puttshack > Pro Shop > Bake Pro Shop</c> into ProShopUI.prefab and is editable there; this component only fills
    /// text, sprites and visual states. Layout regions match the references: categories left, inventory middle-left,
    /// details card right, player tabs along the bottom.
    /// </summary>
    public sealed class ProShopView : MonoBehaviour
    {
        public Canvas Canvas;
        public CanvasGroup Group;
        public TMP_Text Title;
        public ShopButton Back;
        public ShopButton[] Categories = new ShopButton[0];
        public ShopRow[] Rows = new ShopRow[0];
        public Image ScrollUp;
        public Image ScrollDown;
        public TMP_Text Owned;

        public GameObject LoadoutDetails;
        public TMP_Text EquippedTitle;
        public ShopSummaryRow[] Summary = new ShopSummaryRow[0];
        public GameObject ClubDetails;
        public TMP_Text ClubName;
        public TMP_Text ClubRange;
        public TMP_Text ClubSummary;
        public ShopStatRow[] Stats = new ShopStatRow[0];
        public float StatBarWidth = 150f;
        public TMP_Text Note;
        public ShopButton Save;
        public ShopButton Reset;

        public RectTransform PlayerStrip;
        public ShopPlayerTab[] Players = new ShopPlayerTab[0];
        public float PlayerTabWidth = 200f;
        public float PlayerTabGap = 12f;
        public float PlayerStripPadding = 10f;
        public TMP_Text PlayerPrevHint;
        public TMP_Text PlayerNextHint;
        public TMP_Text Hints;
        public GameObject Toast;
        public TMP_Text ToastText;
        public GameObject RotateHint;

        public bool ReducedMotion;

        private static readonly Color Cream = Palette.Cream;
        private static readonly Color Cyan = Palette.Cyan;
        private static readonly Color FocusFill = Palette.Hex(0x0C3B41).WithAlpha(0.92f);
        private static readonly Color SelectedFill = Palette.Hex(0x0B4A52).WithAlpha(0.9f);
        private static readonly Color RowIdleName = Palette.Cream;
        private static readonly Color PrimaryFill = Palette.Hex(0x1CCBE3);
        private static readonly Color SecondaryFill = Palette.Hex(0x222A2A);

        private readonly List<(Image, float)> _glows = new List<(Image, float)>();
        private float _toastUntil = -1f;

        public bool Visible => Canvas != null && Canvas.enabled;

        public void Show(bool visible)
        {
            if (Canvas != null) Canvas.enabled = visible;
            if (Group != null)
            {
                Group.interactable = visible;
                Group.blocksRaycasts = visible;
            }
            if (!visible && Toast != null) Toast.SetActive(false);
        }

        public void BeginFrame() => _glows.Clear();

        public void SetTitle(string title) => Title.text = title;

        public void SetCategory(int index, string label, Sprite icon, bool selected, bool focused)
        {
            ShopButton c = Categories[index];
            c.Root.gameObject.SetActive(true);
            c.Label.text = label;
            if (c.Icon != null)
            {
                c.Icon.sprite = icon;
                c.Icon.enabled = icon != null;
                c.Icon.color = selected ? Cyan : Cream;
            }
            c.Fill.color = selected ? SelectedFill : Color.clear;
            c.Frame.enabled = selected || focused;
            c.Frame.color = focused ? Cyan : Cyan.WithAlpha(0.55f);
            c.Label.color = selected ? Palette.Hex(0xBDF6FF) : Cream;
            Glow(c.Glow, focused);
        }

        public void HideCategory(int index) => Categories[index].Root.gameObject.SetActive(false);

        public void SetRow(int index, ShopRowModel? model)
        {
            ShopRow row = Rows[index];
            row.Root.gameObject.SetActive(model.HasValue);
            if (!model.HasValue) return;
            ShopRowModel m = model.Value;
            bool hot = m.Focused && m.ZoneActive;
            row.Name.text = m.Name;
            row.Name.color = m.Focused ? Cyan : RowIdleName;
            row.Detail.text = m.Detail;
            row.Detail.color = m.Equipped ? Cyan : m.ForSale ? Palette.Hex(0xE9C46A) : Cream.WithAlpha(0.62f);
            row.Icon.sprite = m.Icon;
            row.Icon.enabled = m.Icon != null;
            row.Fill.color = m.Focused ? FocusFill : Color.clear;
            row.Frame.enabled = m.Focused;
            row.Frame.color = hot ? Cyan : Cyan.WithAlpha(0.45f);
            if (row.Check != null) row.Check.enabled = m.Equipped;
            Glow(row.Glow, hot);
        }

        public void SetScroll(bool canUp, bool canDown, string owned)
        {
            ScrollUp.enabled = canUp;
            ScrollDown.enabled = canDown;
            Owned.text = owned;
        }

        public void ShowLoadoutDetails(string title)
        {
            LoadoutDetails.SetActive(true);
            ClubDetails.SetActive(false);
            EquippedTitle.text = title;
        }

        public void SetSummary(int index, Sprite icon, string label, string value, bool changed)
        {
            ShopSummaryRow row = Summary[index];
            row.Root.SetActive(true);
            row.Icon.sprite = icon;
            row.Icon.enabled = icon != null;
            row.Label.text = label;
            row.Value.text = value;
            if (row.Changed != null) row.Changed.enabled = changed;
        }

        public void ShowClubDetails(string name, string range, IReadOnlyList<double> stats, string summary)
        {
            LoadoutDetails.SetActive(false);
            ClubDetails.SetActive(true);
            ClubName.text = name;
            ClubRange.text = range;
            ClubSummary.text = summary;
            for (int i = 0; i < Stats.Length; i++)
            {
                float v = i < stats.Count ? Mathf.Clamp01((float)stats[i]) : 0f;
                Stats[i].Fill.sizeDelta = new Vector2(Mathf.Max(6f, StatBarWidth * v), Stats[i].Fill.sizeDelta.y);
            }
        }

        public void SetNote(string text) => Note.text = text;

        public void SetButton(ShopButton b, string label, bool enabled, bool focused, bool primary)
        {
            b.Label.text = label;
            Color fill = primary ? PrimaryFill : SecondaryFill;
            b.Fill.color = enabled ? fill : fill.WithAlpha(primary ? 0.38f : 0.55f);
            Color text = primary ? Palette.Ink : Cream;
            b.Label.color = enabled ? text : text.WithAlpha(0.5f);
            b.Frame.color = focused ? Cyan : primary ? Palette.Hex(0x7FE9F7).WithAlpha(0.8f) : Cream.WithAlpha(0.28f);
            if (b.Icon != null) b.Icon.color = b.Label.color;
            if (b.Button != null) b.Button.interactable = enabled;
            Glow(b.Glow, focused);
        }

        public void SetBack(string label, bool focused)
        {
            Back.Label.text = label;
            Back.Frame.color = focused ? Cyan : Cream.WithAlpha(0.32f);
            Back.Label.color = focused ? Cyan : Cream;
            if (Back.Icon != null) Back.Icon.color = Back.Label.color;
            Glow(Back.Glow, focused);
        }

        /// <summary>Centres the visible tabs; the strip shrinks to the roster size.</summary>
        public void LayoutPlayers(int count)
        {
            count = Mathf.Clamp(count, 1, Players.Length);
            float width = count * PlayerTabWidth + (count - 1) * PlayerTabGap + PlayerStripPadding * 2f;
            PlayerStrip.sizeDelta = new Vector2(width, PlayerStrip.sizeDelta.y);
            for (int i = 0; i < Players.Length; i++)
            {
                ShopPlayerTab tab = Players[i];
                bool on = i < count;
                tab.Root.gameObject.SetActive(on);
                if (on) tab.Root.anchoredPosition = new Vector2(PlayerStripPadding + i * (PlayerTabWidth + PlayerTabGap), tab.Root.anchoredPosition.y);
            }
        }

        public void SetPlayer(int index, ShopPlayerModel m)
        {
            ShopPlayerTab tab = Players[index];
            tab.Name.text = m.Name.ToUpperInvariant();
            tab.Name.color = m.Edited ? Color.white : m.Identity;
            tab.NameBar.color = m.Edited ? Palette.Hex(0x0B5963) : Palette.Hex(0x121818).WithAlpha(0.94f);
            tab.Frame.color = m.Edited ? Cyan : m.Identity.WithAlpha(0.8f);
            tab.Portrait.texture = m.Portrait;
            tab.Portrait.color = m.Portrait != null ? Color.white : Palette.Hex(0xE8DFCB);
            if (tab.TurnBadge != null)
            {
                tab.TurnBadge.SetActive(m.Turn);
                if (tab.TurnFill != null) tab.TurnFill.color = m.Identity;
            }
            if (tab.Unsaved != null) tab.Unsaved.SetActive(m.Unsaved);
            Glow(tab.Glow, m.Edited, m.Focused ? 1f : 0.55f);
        }

        public void SetPlayerHints(bool visible)
        {
            if (PlayerPrevHint != null) PlayerPrevHint.enabled = visible;
            if (PlayerNextHint != null) PlayerNextHint.enabled = visible;
        }

        public void SetHints(string text) => Hints.text = text;

        public void ShowToast(string text, float seconds)
        {
            if (Toast == null) return;
            ToastText.text = text;
            Toast.SetActive(true);
            _toastUntil = UnityEngine.Time.unscaledTime + seconds;
        }

        public void SetRotateHint(bool visible)
        {
            if (RotateHint != null) RotateHint.SetActive(visible);
        }

        private void Glow(Image glow, bool on, float strength = 1f)
        {
            if (glow == null) return;
            glow.enabled = on;
            glow.color = Cyan.WithAlpha(strength);
            if (on) _glows.Add((glow, strength));
        }

        private void Update()
        {
            if (!Visible) return;
            float pulse = ReducedMotion ? 0.9f : 0.72f + 0.28f * (0.5f + 0.5f * Mathf.Sin(UnityEngine.Time.unscaledTime * 3.2f));
            foreach ((Image glow, float strength) in _glows)
            {
                if (glow != null && glow.enabled) glow.color = Cyan.WithAlpha(strength * pulse);
            }
            if (Toast != null && Toast.activeSelf && UnityEngine.Time.unscaledTime > _toastUntil) Toast.SetActive(false);
        }
    }
}
