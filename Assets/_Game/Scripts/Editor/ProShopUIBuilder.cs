using Puttshack.Content;
using Puttshack.Runtime;
using Puttshack.Runtime.Shop;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Builds ProShopUI.prefab: the Pro Shop / Player Loadout canvas laid out on the 1920x1080 reference grid (the
    /// supplied 1024x576 concepts scaled by 1.875). Rebuilt by the baker; once baked it is an ordinary prefab, so
    /// spacing, fonts and colours can be adjusted in the editor (re-baking overwrites those edits).
    /// </summary>
    public static class ProShopUIBuilder
    {
        public const string PrefabPath = "Assets/_Game/Prefabs/ProShop/ProShopUI.prefab";

        private static TMP_FontAsset _display, _bold, _semi, _extra;
        private static Material _shadowed;
        private static Sprite _fill, _line, _lineThin, _glow, _paper, _soft, _wash, _chevron, _check, _dot, _triUp, _triDown, _rotL, _rotR;

        private static readonly Color Ink = Palette.Ink;
        private static readonly Color Cream = Palette.Cream;
        private static readonly Color Cyan = Palette.Cyan;
        private static readonly Color PanelFill = Palette.Hex(0x141A1A).WithAlpha(0.86f);
        private static readonly Color PanelLine = Palette.Cream.WithAlpha(0.34f);
        private static readonly Color Separator = Palette.Cream.WithAlpha(0.12f);
        private static readonly Color RowFill = Palette.Hex(0xE8E1D0);
        private static readonly Color RowLine = Palette.Hex(0xCFC7B5);

        public static GameObject Build()
        {
            LoadArt();
            var root = new GameObject("ProShop UI", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            canvas.enabled = false;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            var group = root.AddComponent<CanvasGroup>();
            var view = root.AddComponent<ProShopView>();
            view.Canvas = canvas;
            view.Group = group;
            Transform t = root.transform;

            BuildHeader(t, view);
            BuildCategories(t, view);
            BuildItems(t, view);
            BuildDetails(t, view);
            BuildPlayers(t, view);
            BuildHintsAndToast(t, view);
            return root;
        }

        // ---------------------------------------------------------------- Regions

        private static void BuildHeader(Transform root, ProShopView view)
        {
            Image wash = Img(root, "Header Wash", _wash, Cream.WithAlpha(0.82f), false);
            wash.rectTransform.anchorMin = wash.rectTransform.anchorMax = wash.rectTransform.pivot = new Vector2(0f, 1f);
            wash.rectTransform.anchoredPosition = Vector2.zero;
            wash.rectTransform.sizeDelta = new Vector2(1180f, 200f);

            RectTransform logo = TopLeft(root, "Logo", 36, 20, 200, 78);
            TMP_Text word = Label(logo, "Wordmark", _extra, 50, Ink, TextAnchor.MiddleCenter);
            Place(word.rectTransform, 0, 0, 200, 50);
            word.text = "PUTTSHACK";
            word.characterSpacing = 1f;
            RectTransform pro = Child(logo, "Pro", 0, 50, 200, 26);
            Image ruleL = Img(pro, "Rule L", null, Ink, false);
            Place(ruleL.rectTransform, 22, 12, 52, 2.5f);
            Image ruleR = Img(pro, "Rule R", null, Ink, false);
            Place(ruleR.rectTransform, 126, 12, 52, 2.5f);
            TMP_Text proText = Label(pro, "Label", _bold, 24, Ink, TextAnchor.MiddleCenter);
            proText.characterSpacing = 8f;
            Place(proText.rectTransform, 74, 0, 52, 26);
            proText.text = "PRO";

            RectTransform titleRt = TopLeft(root, "Title", 256, 8, 900, 110);
            view.Title = Label(titleRt, "Text", _display, 104, Ink, TextAnchor.MiddleLeft);
            Stretch(view.Title.rectTransform);
            view.Title.text = "PRO SHOP";
            view.Title.characterSpacing = 2f;

            RectTransform back = TopRight(root, "Back", 34, 26, 300, 62);
            view.Back = ButtonParts(back, false);
            view.Back.Fill.color = Palette.Hex(0x121818).WithAlpha(0.9f);
            view.Back.Icon = Img(back, "Chevron", _chevron, Cream, false);
            Place(view.Back.Icon.rectTransform, 16, 15, 32, 32);
            view.Back.Label = Label(back, "Label", _semi, 28, Cream, TextAnchor.MiddleLeft);
            Place(view.Back.Label.rectTransform, 56, 0, 236, 62);
            view.Back.Label.text = "BACK TO LOBBY";
        }

        private static void BuildCategories(Transform root, ProShopView view)
        {
            const int count = 6;
            const float entryH = 90f, pad = 8f, width = 204f;
            float height = pad * 2 + count * entryH;
            RectTransform rail = TopLeft(root, "Categories", 28, 172, width, height);
            Panel(rail, PanelFill, PanelLine);
            view.Categories = new ShopButton[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform entry = Child(rail, $"Category {i + 1}", pad, pad + i * entryH, width - pad * 2, entryH - 4);
                ShopButton b = ButtonParts(entry, true);
                b.Fill.color = Color.clear;
                b.Icon = Img(entry, "Icon", null, Cream, false);
                b.Icon.preserveAspect = true;
                Place(b.Icon.rectTransform, 12, 17, 52, 52);
                b.Label = Label(entry, "Label", _semi, 30, Cream, TextAnchor.MiddleLeft);
                Place(b.Label.rectTransform, 76, 0, 112, entryH - 4);
                b.Label.text = "CATEGORY";
                view.Categories[i] = b;
                if (i > 0)
                {
                    Image sep = Img(rail, $"Separator {i}", null, Separator, false);
                    Place(sep.rectTransform, pad + 8, pad + i * entryH - 3, width - pad * 2 - 16, 1);
                }
            }
        }

        private static void BuildItems(Transform root, ProShopView view)
        {
            const int rows = 5;
            const float width = 330f, rowH = 114f, top = 12f, footer = 48f;
            float height = top + rows * rowH + footer;
            RectTransform panel = TopLeft(root, "Items", 254, 130, width, height);
            Panel(panel, PanelFill, PanelLine);
            view.Rows = new ShopRow[rows];
            for (int i = 0; i < rows; i++)
            {
                RectTransform rt = Child(panel, $"Row {i + 1}", 10, top + i * rowH, width - 20, rowH - 6);
                var row = new ShopRow { Root = rt };
                row.Glow = GlowImage(rt);
                row.Fill = Img(rt, "Fill", _fill, Color.clear, true);
                Stretch(row.Fill.rectTransform);
                row.Frame = Img(rt, "Frame", _line, Cyan, true);
                Stretch(row.Frame.rectTransform);
                row.Icon = Img(rt, "Icon", null, Color.white, false);
                row.Icon.preserveAspect = true;
                Place(row.Icon.rectTransform, 8, 6, 96, 96);
                row.Name = Label(rt, "Name", _bold, 28, Cream, TextAnchor.LowerLeft);
                Place(row.Name.rectTransform, 116, 8, 176, 52);
                row.Name.textWrappingMode = TextWrappingModes.Normal;
                row.Name.lineSpacing = -14f;
                AutoSize(row.Name, 20, 28);
                row.Detail = Label(rt, "Detail", _semi, 19, Cream.WithAlpha(0.62f), TextAnchor.UpperLeft);
                Place(row.Detail.rectTransform, 116, 64, 180, 30);
                AutoSize(row.Detail, 15, 19);
                row.Check = Img(rt, "Check", _check, Cyan, false);
                Place(row.Check.rectTransform, width - 20 - 34, 8, 26, 26);
                row.Button = AddButton(rt.gameObject, row.Fill);
                view.Rows[i] = row;
                if (i > 0)
                {
                    Image sep = Img(panel, $"Separator {i}", null, Separator, false);
                    Place(sep.rectTransform, 20, top + i * rowH - 3.5f, width - 40, 1);
                }
            }
            view.ScrollUp = Img(panel, "Scroll Up", _triUp, Cream.WithAlpha(0.7f), false);
            Place(view.ScrollUp.rectTransform, width / 2 - 12, -10, 24, 24);
            view.ScrollDown = Img(panel, "Scroll Down", _triDown, Cream.WithAlpha(0.7f), false);
            Place(view.ScrollDown.rectTransform, width / 2 - 12, top + rows * rowH - 8, 24, 24);
            view.Owned = Label(panel, "Owned", _semi, 23, Cream, TextAnchor.MiddleCenter);
            Place(view.Owned.rectTransform, 0, height - footer + 6, width, footer - 12);
            view.Owned.richText = true;
            view.Owned.text = "OWNED  <color=#00CBE7>5/9</color>";
        }

        private static void BuildDetails(Transform root, ProShopView view)
        {
            const float width = 400f, height = 690f;
            RectTransform card = TopRight(root, "Details", 34, 150, width, height);
            Image shadow = Img(card, "Shadow", _fill, Ink.WithAlpha(0.35f), true);
            Place(shadow.rectTransform, 5, 7, width, height);
            Image paper = Img(card, "Paper", _paper, Color.white, true);
            Stretch(paper.rectTransform);
            Image outer = Img(card, "Frame", _line, Ink.WithAlpha(0.55f), true);
            Stretch(outer.rectTransform);
            Image inner = Img(card, "Inner Frame", _lineThin, Palette.PaleBorder, true);
            Inset(inner.rectTransform, 7);

            // Loadout: equipped summary
            RectTransform loadout = Child(card, "Loadout", 0, 0, width, 480);
            view.LoadoutDetails = loadout.gameObject;
            view.EquippedTitle = Label(loadout, "Title", _bold, 34, Ink, TextAnchor.MiddleCenter);
            Place(view.EquippedTitle.rectTransform, 0, 20, width, 48);
            view.EquippedTitle.text = "EQUIPPED";
            view.Summary = new ShopSummaryRow[6];
            for (int i = 0; i < view.Summary.Length; i++)
            {
                RectTransform rt = Child(loadout, $"Slot {i + 1}", 20, 76 + i * 66, width - 40, 58);
                var bg = Img(rt, "Fill", _soft, RowFill, true);
                Stretch(bg.rectTransform);
                var line = Img(rt, "Line", _lineThin, RowLine, true);
                Stretch(line.rectTransform);
                var row = new ShopSummaryRow { Root = rt.gameObject };
                row.Icon = Img(rt, "Icon", null, Color.white, false);
                row.Icon.preserveAspect = true;
                Place(row.Icon.rectTransform, 8, 4, 50, 50);
                row.Label = Label(rt, "Label", _semi, 24, Ink, TextAnchor.MiddleLeft);
                Place(row.Label.rectTransform, 68, 0, 120, 58);
                row.Value = Label(rt, "Value", _semi, 20, Palette.MutedGreenGray, TextAnchor.MiddleRight);
                Place(row.Value.rectTransform, 170, 0, width - 40 - 170 - 14, 58);
                AutoSize(row.Value, 15, 20);
                row.Changed = Img(rt, "Changed", _dot, Palette.CyanOnLight, false);
                Place(row.Changed.rectTransform, 4, 4, 12, 12);
                view.Summary[i] = row;
            }

            // Clubs: read-only stats in gameplay terms
            RectTransform club = Child(card, "Club", 0, 0, width, 480);
            view.ClubDetails = club.gameObject;
            view.ClubName = Label(club, "Name", _extra, 48, Ink, TextAnchor.MiddleLeft);
            Place(view.ClubName.rectTransform, 28, 18, width - 56, 58);
            view.ClubName.text = "5 IRON";
            view.ClubRange = Label(club, "Range", _bold, 27, Palette.CyanOnLight, TextAnchor.MiddleLeft);
            Place(view.ClubRange.rectTransform, 28, 72, width - 56, 34);
            view.ClubRange.text = "150\u2013200 YD";
            view.Stats = new ShopStatRow[5];
            const float barX = 214f, barW = 156f;
            view.StatBarWidth = barW;
            for (int i = 0; i < view.Stats.Length; i++)
            {
                float y = 128 + i * 50;
                TMP_Text label = Label(club, $"Stat {i + 1}", _semi, 20, Ink, TextAnchor.MiddleLeft);
                Place(label.rectTransform, 28, y, 184, 30);
                label.text = ClubTraits.Labels[i];
                Image track = Img(club, $"Track {i + 1}", _soft, Palette.Hex(0xD6CFBE), true);
                Place(track.rectTransform, barX, y + 9, barW, 12);
                Image fill = Img(track.rectTransform, "Fill", _soft, Palette.Hex(0x19B9D2), true);
                RectTransform f = fill.rectTransform;
                f.anchorMin = new Vector2(0, 0);
                f.anchorMax = new Vector2(0, 1);
                f.pivot = new Vector2(0, 0.5f);
                f.anchoredPosition = Vector2.zero;
                f.sizeDelta = new Vector2(barW * 0.5f, 0);
                view.Stats[i] = new ShopStatRow { Label = label, Fill = f };
            }
            Image divider = Img(club, "Divider", null, RowLine, false);
            Place(divider.rectTransform, 28, 390, width - 56, 1.5f);
            view.ClubSummary = Label(club, "Summary", _semi, 23, Ink, TextAnchor.MiddleCenter);
            Place(view.ClubSummary.rectTransform, 20, 402, width - 40, 40);
            view.ClubSummary.text = "BALANCED \u00b7 MEDIUM RISK";
            club.gameObject.SetActive(false);

            view.Note = Label(card, "Note", _semi, 18, Palette.MutedGreenGray, TextAnchor.MiddleCenter);
            Place(view.Note.rectTransform, 16, 482, width - 32, 30);
            view.Note.text = "COSMETIC ONLY \u00b7 NO GAMEPLAY EFFECT";

            RectTransform save = Child(card, "Save", 20, 522, width - 40, 66);
            view.Save = ButtonParts(save, false);
            view.Save.Label = Label(save, "Label", _bold, 30, Ink, TextAnchor.MiddleCenter);
            Stretch(view.Save.Label.rectTransform);
            view.Save.Label.text = "SAVE LOADOUT";
            RectTransform reset = Child(card, "Reset", 20, 602, width - 40, 62);
            view.Reset = ButtonParts(reset, false);
            view.Reset.Label = Label(reset, "Label", _semi, 27, Cream, TextAnchor.MiddleCenter);
            Stretch(view.Reset.Label.rectTransform);
            view.Reset.Label.text = "RESET";
        }

        private static void BuildPlayers(Transform root, ProShopView view)
        {
            const int count = 6;
            const float tabW = 200f, tabH = 182f, gap = 12f, pad = 9f, barH = 44f;
            float width = count * tabW + (count - 1) * gap + pad * 2;
            var strip = new GameObject("Players", typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(root, false);
            strip.anchorMin = strip.anchorMax = new Vector2(0, 0);
            strip.pivot = new Vector2(0.5f, 0);
            strip.anchoredPosition = new Vector2(912, 20);
            strip.sizeDelta = new Vector2(width, tabH + pad * 2);
            Panel(strip, Palette.Hex(0x121818).WithAlpha(0.55f), Palette.Cream.WithAlpha(0.26f));
            view.PlayerStrip = strip;
            view.PlayerTabWidth = tabW;
            view.PlayerTabGap = gap;
            view.PlayerStripPadding = pad;
            view.Players = new ShopPlayerTab[count];
            for (int i = 0; i < count; i++)
            {
                var rt = new GameObject($"Player {i + 1}", typeof(RectTransform)).GetComponent<RectTransform>();
                rt.SetParent(strip, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
                rt.pivot = new Vector2(0, 0);
                rt.anchoredPosition = new Vector2(pad + i * (tabW + gap), pad);
                rt.sizeDelta = new Vector2(tabW, tabH);
                var tab = new ShopPlayerTab { Root = rt };
                tab.Glow = GlowImage(rt);

                Image maskImg = Img(rt, "Mask", _fill, Color.white, true);
                Stretch(maskImg.rectTransform);
                maskImg.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                tab.Portrait = new GameObject("Portrait", typeof(RectTransform)).AddComponent<RawImage>();
                tab.Portrait.rectTransform.SetParent(maskImg.rectTransform, false);
                Place(tab.Portrait.rectTransform, 0, 0, tabW, tabH - barH + 2);
                tab.Portrait.color = Palette.Hex(0xE8DFCB);
                tab.Portrait.raycastTarget = false;
                tab.NameBar = Img(maskImg.rectTransform, "Name Bar", null, Palette.Hex(0x121818).WithAlpha(0.94f), false);
                Place(tab.NameBar.rectTransform, 0, tabH - barH, tabW, barH);
                tab.Name = Label(tab.NameBar.rectTransform, "Name", _bold, 29, Palette.Player(i), TextAnchor.MiddleCenter);
                Stretch(tab.Name.rectTransform);
                tab.Name.text = "PLAYER";

                tab.Frame = Img(rt, "Frame", _line, Palette.Player(i), true);
                Stretch(tab.Frame.rectTransform);

                RectTransform badge = Child(rt, "Your Shot", tabW / 2 - 64, -20, 128, 30);
                Image badgeFill = Img(badge, "Fill", _fill, Palette.Player(i), true);
                Stretch(badgeFill.rectTransform);
                TMP_Text badgeText = Label(badge, "Label", _bold, 18, Ink, TextAnchor.MiddleCenter);
                Stretch(badgeText.rectTransform);
                badgeText.text = "YOUR SHOT";
                tab.TurnBadge = badge.gameObject;
                tab.TurnFill = badgeFill;
                tab.TurnText = badgeText;
                badge.gameObject.SetActive(false);

                RectTransform unsaved = Child(rt, "Unsaved", tabW - 96, 8, 88, 24);
                Image unsavedFill = Img(unsaved, "Fill", _fill, Palette.Hex(0x121818).WithAlpha(0.85f), true);
                Stretch(unsavedFill.rectTransform);
                TMP_Text unsavedText = Label(unsaved, "Label", _bold, 15, Cyan, TextAnchor.MiddleCenter);
                Stretch(unsavedText.rectTransform);
                unsavedText.text = "UNSAVED";
                tab.Unsaved = unsaved.gameObject;
                unsaved.gameObject.SetActive(false);

                tab.Button = AddButton(rt.gameObject, maskImg);
                view.Players[i] = tab;
            }

            view.PlayerPrevHint = KeyHint(strip, "Prev Hint", "Q", -44);
            view.PlayerNextHint = KeyHint(strip, "Next Hint", "E", width + 8);
        }

        private static TMP_Text KeyHint(RectTransform strip, string name, string key, float x)
        {
            RectTransform rt = Child(strip, name, x, (strip.sizeDelta.y - 36) / 2, 36, 36);
            Image cap = Img(rt, "Cap", _fill, Palette.Hex(0x121818).WithAlpha(0.8f), true);
            Stretch(cap.rectTransform);
            Image frame = Img(rt, "Frame", _lineThin, Cream.WithAlpha(0.5f), true);
            Stretch(frame.rectTransform);
            TMP_Text text = Label(rt, "Key", _bold, 22, Cream, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            text.text = key;
            return text;
        }

        private static void BuildHintsAndToast(Transform root, ProShopView view)
        {
            var hints = new GameObject("Hints", typeof(RectTransform)).GetComponent<RectTransform>();
            hints.SetParent(root, false);
            hints.anchorMin = hints.anchorMax = hints.pivot = new Vector2(1, 0);
            hints.anchoredPosition = new Vector2(-38, 24);
            hints.sizeDelta = new Vector2(330, 190);
            view.Hints = Label(hints, "Text", _semi, 22, Cream, TextAnchor.LowerRight);
            Stretch(view.Hints.rectTransform);
            view.Hints.lineSpacing = 10f;
            view.Hints.richText = true;
            view.Hints.fontSharedMaterial = _shadowed;

            var toast = new GameObject("Toast", typeof(RectTransform)).GetComponent<RectTransform>();
            toast.SetParent(root, false);
            toast.anchorMin = toast.anchorMax = new Vector2(0, 0);
            toast.pivot = new Vector2(0.5f, 0);
            toast.anchoredPosition = new Vector2(912, 236);
            toast.sizeDelta = new Vector2(620, 52);
            Image toastFill = Img(toast, "Fill", _fill, Palette.Hex(0x121818).WithAlpha(0.92f), true);
            Stretch(toastFill.rectTransform);
            Image toastFrame = Img(toast, "Frame", _line, Cyan.WithAlpha(0.9f), true);
            Stretch(toastFrame.rectTransform);
            view.ToastText = Label(toast, "Text", _bold, 25, Cream, TextAnchor.MiddleCenter);
            Stretch(view.ToastText.rectTransform);
            view.Toast = toast.gameObject;
            toast.gameObject.SetActive(false);

            var rotate = new GameObject("Rotate Hint", typeof(RectTransform)).GetComponent<RectTransform>();
            rotate.SetParent(root, false);
            rotate.anchorMin = rotate.anchorMax = new Vector2(0, 0);
            rotate.pivot = new Vector2(0.5f, 0.5f);
            rotate.anchoredPosition = new Vector2(960, 1080 - 842);
            rotate.sizeDelta = new Vector2(520, 90);
            Image left = Img(rotate, "Left", _rotL, Cream.WithAlpha(0.6f), false);
            Place(left.rectTransform, 0, 5, 80, 80);
            Image right = Img(rotate, "Right", _rotR, Cream.WithAlpha(0.6f), false);
            Place(right.rectTransform, 440, 5, 80, 80);
            view.RotateHint = rotate.gameObject;
        }

        // ---------------------------------------------------------------- Widgets

        private static ShopButton ButtonParts(RectTransform rt, bool thinFrame)
        {
            var b = new ShopButton { Root = rt };
            b.Glow = GlowImage(rt);
            b.Fill = Img(rt, "Fill", _fill, Palette.Hex(0x1CCBE3), true);
            Stretch(b.Fill.rectTransform);
            b.Frame = Img(rt, "Frame", thinFrame ? _lineThin : _line, Cyan, true);
            Stretch(b.Frame.rectTransform);
            b.Button = AddButton(rt.gameObject, b.Fill);
            return b;
        }

        private static Image GlowImage(RectTransform parent)
        {
            Image glow = Img(parent, "Glow", _glow, Cyan, true);
            Inset(glow.rectTransform, -26);
            glow.enabled = false;
            return glow;
        }

        private static Button AddButton(GameObject go, Graphic target)
        {
            target.raycastTarget = true;
            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.targetGraphic = target;
            return button;
        }

        private static void Panel(RectTransform rt, Color fill, Color line)
        {
            Image f = Img(rt, "Panel", _fill, fill, true);
            Stretch(f.rectTransform);
            f.transform.SetAsFirstSibling();
            Image l = Img(rt, "Panel Frame", _lineThin, line, true);
            Stretch(l.rectTransform);
            l.transform.SetSiblingIndex(1);
        }

        private static Image Img(Transform parent, string name, Sprite sprite, Color color, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced && sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        private static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, float size, Color color, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = Align(align);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.characterSpacing = 1.5f;
            text.raycastTarget = false;
            text.richText = false;
            return text;
        }

        private static void AutoSize(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
        }

        /// <summary>Middle rows centre on the glyphs' midline so all-caps labels sit optically centred in their boxes.</summary>
        private static TextAlignmentOptions Align(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Midline;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                default: return TextAlignmentOptions.BottomRight;
            }
        }

        /// <summary>Top-left anchored child of the canvas, positioned in reference pixels from the top-left corner.</summary>
        private static RectTransform TopLeft(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static RectTransform TopRight(Transform parent, string name, float right, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-right, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>Child positioned from its parent's top-left corner.</summary>
        private static RectTransform Child(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Place(rt, x, y, w, h);
            return rt;
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt) => Inset(rt, 0);

        private static void Inset(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private static void LoadArt()
        {
            ProShopFonts.EnsureAll();
            _display = ProShopFonts.Display;
            _extra = ProShopFonts.ExtraBold;
            _bold = ProShopFonts.Bold;
            _semi = ProShopFonts.SemiBold;
            _shadowed = ProShopFonts.Shadowed(_semi);
            string s = ProShopImportRules.SpriteRoot;
            _fill = Sprite(s, "ps_chamfer_fill");
            _line = Sprite(s, "ps_chamfer_line");
            _lineThin = Sprite(s, "ps_chamfer_line_thin");
            _glow = Sprite(s, "ps_chamfer_glow");
            _paper = Sprite(s, "ps_paper");
            _soft = Sprite(s, "ps_soft_fill");
            _wash = Sprite(s, "ps_header_wash");
            _chevron = Sprite(s, "ps_chevron_left");
            _check = Sprite(s, "ps_check");
            _dot = Sprite(s, "ps_dot");
            _triUp = Sprite(s, "ps_tri_up");
            _triDown = Sprite(s, "ps_tri_down");
            _rotL = Sprite(s, "ps_rotate_left");
            _rotR = Sprite(s, "ps_rotate_right");
            if (_display == null || _semi == null || _fill == null)
                throw new System.InvalidOperationException("Pro Shop fonts or sprites are missing under " + ProShopImportRules.ProShopRoot + "UI/.");
        }

        private static Sprite Sprite(string folder, string name) => AssetDatabase.LoadAssetAtPath<Sprite>(folder + name + ".png");
    }
}
