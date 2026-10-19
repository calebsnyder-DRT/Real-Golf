using System;
using System.Collections.Generic;
using Puttshack.Runtime.Hud;
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
        public Image Disc;
        public TMP_Text Initial;
        public Image Frame;
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
    /// Pro Shop / Player Loadout overlay in the brand HUD style (Claude Design "Puttshack Pro HUD" 1o): Midnight panels,
    /// Pink for the selection, White or Midnight type, and the golfer centre-stage. The hierarchy is baked by
    /// <c>Puttshack > Pro Shop > Rebuild Shop UI</c> (or the full Pro Shop bake) into ProShopUI.prefab; this component only
    /// fills text, sprites and visual states.
    /// </summary>
    public sealed class ProShopView : MonoBehaviour
    {
        public Canvas Canvas;
        public CanvasGroup Group;
        public TMP_Text Title;
        public Image HeaderDisc;
        public TMP_Text HeaderInitial;
        public TMP_Text HeaderName;
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

        private static readonly Color White = HudBrand.White;
        private static readonly Color Midnight = HudBrand.Midnight;
        private static readonly Color Pink = HudBrand.Pink;
        private static readonly Color FocusFill = HudBrand.White.WithAlpha(0.12f);
        private static readonly Color SecondaryFill = HudBrand.White.WithAlpha(0.1f);

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

        public void BeginFrame() { }

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
                c.Icon.color = selected ? Midnight : White;
            }
            c.Fill.color = selected ? Pink : Color.clear;
            c.Label.color = selected ? Midnight : White;
            c.Frame.enabled = focused;
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
            row.Name.color = White;
            row.Detail.text = m.Detail;
            row.Detail.color = m.Equipped || m.Focused ? White : HudBrand.Muted;
            row.Icon.sprite = m.Icon;
            row.Icon.enabled = m.Icon != null;
            row.Fill.color = m.Focused ? FocusFill : Color.clear;
            row.Frame.enabled = m.Focused;
            row.Frame.color = hot ? Pink : Pink.WithAlpha(0.45f);
            if (row.Check != null) row.Check.enabled = m.Equipped;
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
                Stats[i].Fill.sizeDelta = new Vector2(Mathf.Max(10f, StatBarWidth * v), Stats[i].Fill.sizeDelta.y);
            }
        }

        public void SetNote(string text) => Note.text = text;

        public void SetButton(ShopButton b, string label, bool enabled, bool focused, bool primary)
        {
            b.Label.text = label;
            Color fill = primary ? Pink : SecondaryFill;
            b.Fill.color = enabled ? fill : fill.WithAlpha(primary ? 0.38f : 0.05f);
            Color text = primary ? Midnight : White;
            b.Label.color = enabled ? text : text.WithAlpha(0.5f);
            b.Frame.enabled = focused;
            if (b.Icon != null) b.Icon.color = b.Label.color;
            if (b.Button != null) b.Button.interactable = enabled;
        }

        public void SetBack(string label, bool focused)
        {
            Back.Label.text = label;
            Back.Frame.enabled = focused;
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
            string name = m.Name.ToUpperInvariant();
            string initial = name.Length > 0 ? name.Substring(0, 1) : "";
            tab.Name.text = name;
            tab.Name.color = m.Edited ? HudBrand.TextOn(m.Identity) : White;
            tab.NameBar.color = m.Edited ? m.Identity : Color.clear;
            tab.Frame.enabled = m.Focused;
            tab.Disc.color = m.Edited ? Midnight : m.Identity;
            tab.Initial.text = initial;
            tab.Initial.color = m.Edited ? White : HudBrand.TextOn(m.Identity);
            tab.Portrait.texture = m.Portrait;
            tab.Portrait.enabled = m.Portrait != null;
            if (tab.TurnBadge != null)
            {
                tab.TurnBadge.SetActive(m.Turn);
                if (tab.TurnFill != null) tab.TurnFill.color = m.Identity;
                if (tab.TurnText != null) tab.TurnText.color = HudBrand.TextOn(m.Identity);
            }
            if (tab.Unsaved != null) tab.Unsaved.SetActive(m.Unsaved);
            if (!m.Edited) return;
            HeaderName.text = name;
            HeaderDisc.color = m.Identity;
            HeaderInitial.text = initial;
            HeaderInitial.color = HudBrand.TextOn(m.Identity);
        }

        public void SetPlayerHints(bool visible)
        {
            if (PlayerPrevHint != null) PlayerPrevHint.transform.parent.gameObject.SetActive(visible);
            if (PlayerNextHint != null) PlayerNextHint.transform.parent.gameObject.SetActive(visible);
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

        private void Update()
        {
            if (Visible && Toast != null && Toast.activeSelf && UnityEngine.Time.unscaledTime > _toastUntil) Toast.SetActive(false);
        }
    }
}
