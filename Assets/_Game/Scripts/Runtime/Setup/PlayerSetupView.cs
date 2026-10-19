using System;
using Puttshack.Runtime.Hud;
using Puttshack.Runtime.Shop;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.Runtime.Setup
{
    [Serializable]
    public sealed class SetupPlayerRow
    {
        public RectTransform Root;
        public Image Fill;
        public Image Frame;
        public Image Disc;
        public TMP_Text Initial;
        public RawImage Portrait;
        public TMP_Text Name;
        public TMP_Text Detail;
        public Button Button;
    }

    [Serializable]
    public sealed class SetupSection
    {
        public TMP_Text Label;
        public TMP_Text Value;
        [Tooltip("Outline around the whole section, shown while it has keyboard / gamepad focus.")]
        public Image Frame;
    }

    [Serializable]
    public sealed class SetupSwatch
    {
        public RectTransform Root;
        public Image Fill;
        public Image Ring;
        public Button Button;
    }

    public struct SetupRowModel
    {
        public string Name;
        public string Detail;
        public Color Identity;
        public Texture Portrait;
        public bool Selected;
        public bool Focused;
    }

    /// <summary>
    /// Player Setup overlay in the Pro Shop's brand style: the roster on the left, the selected player's name, body, skin
    /// tone and hair on the right, and the golfer centre-stage on the shop turntable. The hierarchy is baked by
    /// <c>Puttshack > Player Setup > Bake Player Setup UI</c> into PlayerSetupUI.prefab; this component only fills text,
    /// colours and visual states.
    /// </summary>
    public sealed class PlayerSetupView : MonoBehaviour
    {
        public Canvas Canvas;
        public CanvasGroup Group;
        public TMP_Text HeaderCount;
        public TMP_Text HeaderTitle;
        public ShopButton Back;

        public SetupSection Count;
        public TMP_Text CountValue;
        public ShopButton Fewer;
        public ShopButton More;
        public SetupPlayerRow[] Rows = new SetupPlayerRow[0];

        public SetupSection Name;
        public TMP_InputField NameField;
        public SetupSection Body;
        public ShopButton[] Bodies = new ShopButton[0];
        public SetupSection Skin;
        public SetupSwatch[] SkinSwatches = new SetupSwatch[0];
        public SetupSection Hair;
        public SetupSwatch[] HairSwatches = new SetupSwatch[0];
        public SetupSection Style;
        public ShopButton[] Styles = new ShopButton[0];

        public ShopButton Shop;
        public ShopButton Start;
        public TMP_Text Hints;
        public GameObject Toast;
        public TMP_Text ToastText;

        private static readonly Color White = HudBrand.White;
        private static readonly Color Midnight = HudBrand.Midnight;
        private static readonly Color Pink = HudBrand.Pink;
        private static readonly Color FocusFill = HudBrand.White.WithAlpha(0.12f);
        private static readonly Color ChoiceFill = HudBrand.White.WithAlpha(0.08f);

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

        public void SetHeader(int players) => HeaderCount.text = players == 1 ? "1 PLAYER" : $"{players} PLAYERS";

        public void SetBack(bool focused) => Back.Frame.enabled = focused;

        public void SetCount(int count, int max, bool focused)
        {
            SetSection(Count, $"{count} OF {max}", focused);
            CountValue.text = count.ToString();
            SetStepper(Fewer, count > 1);
            SetStepper(More, count < max);
        }

        private static void SetStepper(ShopButton b, bool enabled)
        {
            b.Fill.color = enabled ? ChoiceFill : ChoiceFill.WithAlpha(0.03f);
            b.Label.color = enabled ? White : White.WithAlpha(0.3f);
            if (b.Button != null) b.Button.interactable = enabled;
        }

        public void SetRow(int index, SetupRowModel? model)
        {
            SetupPlayerRow row = Rows[index];
            row.Root.gameObject.SetActive(model.HasValue);
            if (!model.HasValue) return;
            SetupRowModel m = model.Value;
            string name = m.Name.ToUpperInvariant();
            row.Name.text = name;
            row.Detail.text = m.Detail;
            row.Detail.color = m.Selected ? White : HudBrand.Muted;
            row.Fill.color = m.Selected ? FocusFill : Color.clear;
            row.Frame.enabled = m.Selected;
            row.Frame.color = m.Focused ? Pink : Pink.WithAlpha(0.45f);
            row.Disc.color = m.Identity;
            row.Initial.text = name.Length > 0 ? name.Substring(0, 1) : "";
            row.Initial.color = HudBrand.TextOn(m.Identity);
            row.Portrait.texture = m.Portrait;
            row.Portrait.enabled = m.Portrait != null;
        }

        public void SetSection(SetupSection section, string value, bool focused)
        {
            if (section.Value != null) section.Value.text = value;
            if (section.Frame != null) section.Frame.enabled = focused;
        }

        /// <summary>A body or hair-style choice: Pink when it is the player's current one.</summary>
        public void SetChoice(ShopButton b, string label, bool selected)
        {
            b.Root.gameObject.SetActive(true);
            b.Label.text = label;
            b.Fill.color = selected ? Pink : ChoiceFill;
            b.Label.color = selected ? Midnight : White;
        }

        public void SetSwatch(SetupSwatch s, Color color, bool selected)
        {
            s.Root.gameObject.SetActive(true);
            s.Fill.color = color;
            s.Ring.enabled = selected;
        }

        public void HideSwatch(SetupSwatch s) => s.Root.gameObject.SetActive(false);

        /// <summary>Shows the player's name unless it is being typed into.</summary>
        public void SetName(string name)
        {
            if (NameField != null && !NameField.isFocused && NameField.text != name) NameField.SetTextWithoutNotify(name);
        }

        public void SetAction(ShopButton b, string label, bool focused)
        {
            b.Label.text = label;
            b.Frame.enabled = focused;
        }

        public void SetHints(string text) => Hints.text = text;

        public void ShowToast(string text, float seconds)
        {
            if (Toast == null) return;
            ToastText.text = text;
            Toast.SetActive(true);
            _toastUntil = UnityEngine.Time.unscaledTime + seconds;
        }

        private void Update()
        {
            if (Visible && Toast != null && Toast.activeSelf && UnityEngine.Time.unscaledTime > _toastUntil) Toast.SetActive(false);
        }
    }
}
