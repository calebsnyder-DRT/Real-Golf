using Puttshack.Content;
using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using Puttshack.Runtime.Shop;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Puttshack.EditorTools.HudUIBuilder;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Builds ProShopUI.prefab, the Pro Shop / Player Loadout overlay, in the brand HUD style (Claude Design "Puttshack
    /// Pro HUD" 1o) on the 1920x1080 reference grid, keeping the golfer centre-stage. It shares the HUD's flat shapes and
    /// Ringside fonts (<see cref="HudUIBuilder"/>). Once baked it is an ordinary prefab, so spacing, fonts and colours can
    /// be adjusted in the editor (re-baking overwrites those edits).
    /// </summary>
    public static class ProShopUIBuilder
    {
        public const string PrefabPath = "Assets/_Game/Prefabs/ProShop/ProShopUI.prefab";

        private const float Edge = 40f, PanelTop = 160f, PanelRadius = 32f;
        /// <summary>The design's 0.9 Midnight; UGUI blends in linear space, so it needs more alpha to read as dark.</summary>
        private static readonly Color PanelFill = HudBrand.Midnight.WithAlpha(0.96f);
        private static Sprite _triUp, _triDown, _rotL, _rotR;
        private static Material _shadowed;

        public static GameObject Build()
        {
            LoadBrandArt();
            string s = ProShopImportRules.SpriteRoot;
            _triUp = AssetDatabase.LoadAssetAtPath<Sprite>(s + "ps_tri_up.png");
            _triDown = AssetDatabase.LoadAssetAtPath<Sprite>(s + "ps_tri_down.png");
            _rotL = AssetDatabase.LoadAssetAtPath<Sprite>(s + "ps_rotate_left.png");
            _rotR = AssetDatabase.LoadAssetAtPath<Sprite>(s + "ps_rotate_right.png");
            _shadowed = ProShopFonts.Shadowed(CondensedFont);

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
            FinishPills(root);
            return root;
        }

        // ---------------------------------------------------------------- Regions

        private static void BuildHeader(Transform root, ProShopView view)
        {
            Image pill = Round(root, "Header", PanelFill, Pill);
            Anchor(pill.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Edge, -Edge), new Vector2(420, 88));
            HStack(pill.gameObject, 20, new RectOffset(12, 32, 12, 12), TextAnchor.MiddleLeft);
            Fit(pill.gameObject);
            view.HeaderDisc = Round(pill.transform, "Disc", HudBrand.Aqua, Pill);
            Size(view.HeaderDisc, 64, 64);
            view.HeaderInitial = Text(view.HeaderDisc.transform, "Initial", WideFont, 30, HudBrand.Midnight, "C", 0f, TextAlignmentOptions.Midline);
            Stretch(view.HeaderInitial.rectTransform);
            RectTransform names = Child(pill.transform, "Names", 0, 0, 280, 64);
            Size(names, 280, 64);
            view.Title = TextAt(names, "Title", CondensedFont, 18, Muted, "PLAYER LOADOUT", 0, 2, 280, 22, 0.12f);
            view.HeaderName = TextAt(names, "Name", BoldFont, 34, White, "CALEB", 0, 24, 280, 40);
            AutoSize(view.HeaderName, 22, 34);
            Image word = Img(pill.transform, "Wordmark", Wordmark, White.WithAlpha(0.85f), false);
            word.preserveAspect = true;
            Size(word, 150, 26);

            Image back = Round(root, "Back", PanelFill, Pill);
            Anchor(back.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Edge, -Edge - 12), new Vector2(360, 64));
            HStack(back.gameObject, 12, new RectOffset(12, 28, 0, 0), TextAnchor.MiddleLeft);
            Fit(back.gameObject);
            view.Back = new ShopButton { Root = back.rectTransform, Fill = back };
            view.Back.Icon = Outline(back.transform, "Key", LinePill, White.WithAlpha(0.5f));
            Size(view.Back.Icon, 68, 34);
            TMP_Text esc = Text(view.Back.Icon.transform, "Legend", CondensedBoldFont, 18, White, "ESC", 0.1f, TextAlignmentOptions.Midline);
            Stretch(esc.rectTransform);
            view.Back.Label = Text(back.transform, "Label", BoldFont, 26, White, "BACK TO SETUP");
            view.Back.Frame = FocusFrame(back.transform, LinePill, White);
            view.Back.Button = AddButton(back.gameObject, back);
        }

        private static void BuildCategories(Transform root, ProShopView view)
        {
            const int count = 6;
            const float w = 216, entryH = 64, gap = 8, pad = 12;
            Image panel = Round(root, "Categories", PanelFill, PanelRadius);
            Place(panel.rectTransform, Edge, PanelTop, w, pad * 2 + count * entryH + (count - 1) * gap);
            view.Categories = new ShopButton[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform entry = Child(panel.rectTransform, $"Category {i + 1}", pad, pad + i * (entryH + gap), w - pad * 2, entryH);
                var b = new ShopButton { Root = entry };
                b.Fill = Round(entry, "Fill", i == 0 ? HudBrand.Pink : Color.clear, Pill);
                Stretch(b.Fill.rectTransform);
                b.Frame = Outline(entry, "Focus", LinePill, White);
                Stretch(b.Frame.rectTransform);
                b.Frame.enabled = false;
                b.Label = TextAt(entry, "Label", BoldFont, 26, i == 0 ? HudBrand.Midnight : White, "CATEGORY", 28, 0, w - pad * 2 - 40, entryH);
                AutoSize(b.Label, 18, 26);
                b.Button = AddButton(entry.gameObject, b.Fill);
                view.Categories[i] = b;
            }
        }

        private static void BuildItems(Transform root, ProShopView view)
        {
            const int rows = 7;
            const float w = 440, rowH = 84, gap = 8, pad = 12, footer = 52;
            float h = pad * 2 + rows * rowH + (rows - 1) * gap + footer;
            Image panel = Round(root, "Items", PanelFill, PanelRadius);
            RectTransform p = panel.rectTransform;
            Place(p, Edge + 216 + 16, PanelTop, w, h);
            view.Rows = new ShopRow[rows];
            for (int i = 0; i < rows; i++)
            {
                RectTransform rt = Child(p, $"Row {i + 1}", pad, pad + i * (rowH + gap), w - pad * 2, rowH);
                var row = new ShopRow { Root = rt };
                row.Fill = Round(rt, "Fill", i == 1 ? White.WithAlpha(0.12f) : Color.clear, 24);
                Stretch(row.Fill.rectTransform);
                row.Frame = Img(rt, "Focus", LineR24, HudBrand.Pink, true);
                row.Frame.pixelsPerUnitMultiplier = 2f;
                Stretch(row.Frame.rectTransform);
                row.Frame.enabled = i == 1;
                Image well = Round(rt, "Swatch", White.WithAlpha(0.08f), Pill);
                Place(well.rectTransform, 12, 10, 64, 64);
                row.Icon = Img(well.rectTransform, "Icon", null, Color.white, false);
                row.Icon.preserveAspect = true;
                Inset(row.Icon.rectTransform, 4);
                row.Name = TextAt(rt, "Name", BoldFont, 24, White, "ROYAL POLO", 90, 12, w - pad * 2 - 110, 32);
                AutoSize(row.Name, 16, 24);
                row.Detail = TextAt(rt, "Detail", CondensedFont, 20, Muted, "POLO · 250 CR", 90, 46, w - pad * 2 - 110, 26, 0.08f);
                AutoSize(row.Detail, 13, 20);
                row.Button = AddButton(rt.gameObject, row.Fill);
                view.Rows[i] = row;
            }
            float footY = h - footer;
            Image rule = Solid(p, "Footer Rule", LineColor);
            Place(rule.rectTransform, 24, footY, w - 48, 1);
            view.Owned = TextAt(p, "Owned", CondensedFont, 22, White, "OWNED  5/9", 24, footY + 8, 300, 36, 0.1f);
            view.Owned.richText = true;
            view.ScrollUp = Img(p, "Scroll Up", _triUp, White.WithAlpha(0.7f), false);
            Place(view.ScrollUp.rectTransform, w - 84, footY + 16, 22, 20);
            view.ScrollDown = Img(p, "Scroll Down", _triDown, White.WithAlpha(0.7f), false);
            Place(view.ScrollDown.rectTransform, w - 50, footY + 16, 22, 20);
        }

        private static void BuildDetails(Transform root, ProShopView view)
        {
            const float w = 400, h = 600, pad = 28, inner = w - pad * 2;
            Image card = Round(root, "Details", PanelFill, PanelRadius);
            RectTransform c = card.rectTransform;
            Anchor(c, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Edge, -PanelTop), new Vector2(w, h));

            RectTransform loadout = Child(c, "Loadout", 0, 0, w, 400);
            view.LoadoutDetails = loadout.gameObject;
            view.EquippedTitle = TextAt(loadout, "Title", CondensedFont, 22, Muted, "EQUIPPED", pad, pad, inner, 28, 0.12f);
            view.Summary = new ShopSummaryRow[6];
            string[] slots = { "TOP", "BOTTOM", "SOCKS", "SHOES", "BELT", "BAG" };
            for (int i = 0; i < view.Summary.Length; i++)
            {
                RectTransform rt = Child(loadout, $"Slot {i + 1}", pad, 70 + i * 52, inner, 52);
                var row = new ShopSummaryRow { Root = rt.gameObject };
                row.Icon = Img(rt, "Icon", null, Color.white, false);
                row.Icon.preserveAspect = true;
                Place(row.Icon.rectTransform, 0, 4, 34, 34);
                row.Label = TextAt(rt, "Label", CondensedFont, 20, Muted, slots[i], 44, 0, 110, 42, 0.12f);
                row.Value = TextAt(rt, "Value", BoldFont, 22, White, "ROYAL POLO", 150, 0, inner - 150, 42, 0f, TextAlignmentOptions.MidlineRight);
                AutoSize(row.Value, 14, 22);
                row.Changed = Round(rt, "Changed", HudBrand.Pink, Pill);
                Place(row.Changed.rectTransform, 28, 2, 10, 10);
                row.Changed.enabled = false;
                Image rule = Solid(rt, "Rule", LineColor);
                Place(rule.rectTransform, 0, 46, inner, 1);
                view.Summary[i] = row;
            }

            RectTransform club = Child(c, "Club", 0, 0, w, 400);
            view.ClubDetails = club.gameObject;
            view.ClubName = TextAt(club, "Name", WideFont, 40, White, "5 IRON", pad, pad, inner, 48);
            AutoSize(view.ClubName, 24, 40);
            view.ClubRange = TextAt(club, "Range", CondensedFont, 24, Muted, "150–200 YD", pad, 80, inner, 30, 0.1f);
            view.Stats = new ShopStatRow[5];
            const float barX = 200, barW = w - pad - barX;
            view.StatBarWidth = barW;
            for (int i = 0; i < view.Stats.Length; i++)
            {
                float y = 132 + i * 46;
                TMP_Text label = TextAt(club, $"Stat {i + 1}", CondensedFont, 20, White, ClubTraits.Labels[i], pad, y, barX - pad - 8, 30, 0.08f);
                AutoSize(label, 14, 20);
                Image track = Round(club, $"Track {i + 1}", White.WithAlpha(0.18f), Pill);
                Place(track.rectTransform, barX, y + 10, barW, 10);
                Image fill = Round(track.rectTransform, "Fill", HudBrand.Aqua, Pill);
                RectTransform f = fill.rectTransform;
                f.anchorMin = new Vector2(0, 0);
                f.anchorMax = new Vector2(0, 1);
                f.pivot = new Vector2(0, 0.5f);
                f.anchoredPosition = Vector2.zero;
                f.sizeDelta = new Vector2(barW * 0.5f, 0);
                view.Stats[i] = new ShopStatRow { Label = label, Fill = f };
            }
            view.ClubSummary = TextAt(club, "Summary", CondensedBoldFont, 22, White, "BALANCED · MEDIUM RISK", pad, 366, inner, 30, 0.1f, TextAlignmentOptions.Midline);
            club.gameObject.SetActive(false);

            view.Note = TextAt(c, "Note", CondensedFont, 18, Muted, "COSMETIC ONLY · NO GAMEPLAY EFFECT", pad, 404, inner, 28, 0.08f, TextAlignmentOptions.Midline);
            AutoSize(view.Note, 12, 18);
            view.Save = ActionButton(c, "Save", pad, 444, inner, 64, HudBrand.Pink, HudBrand.Midnight, "SAVE LOADOUT");
            view.Reset = ActionButton(c, "Reset", pad, 518, inner, 54, White.WithAlpha(0.1f), White, "RESET");
        }

        private static void BuildPlayers(Transform root, ProShopView view)
        {
            const int count = 6;
            const float tabW = 220, tabH = 64, gap = 12, pad = 12;
            float width = count * tabW + (count - 1) * gap + pad * 2;
            Image strip = Round(root, "Players", PanelFill, Pill);
            RectTransform s = strip.rectTransform;
            Anchor(s, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, Edge), new Vector2(width, tabH + pad * 2));
            view.PlayerStrip = s;
            view.PlayerTabWidth = tabW;
            view.PlayerTabGap = gap;
            view.PlayerStripPadding = pad;
            view.Players = new ShopPlayerTab[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform rt = new GameObject($"Player {i + 1}", typeof(RectTransform)).GetComponent<RectTransform>();
                rt.SetParent(s, false);
                Anchor(rt, new Vector2(0, 0), new Vector2(0, 0), new Vector2(pad + i * (tabW + gap), pad), new Vector2(tabW, tabH));
                var tab = new ShopPlayerTab { Root = rt };
                Color identity = Palette.Player(i);
                bool edited = i == 0;
                tab.NameBar = Round(rt, "Fill", edited ? identity : Color.clear, Pill);
                Stretch(tab.NameBar.rectTransform);
                tab.Frame = Outline(rt, "Focus", LinePill, White);
                Stretch(tab.Frame.rectTransform);
                tab.Frame.enabled = false;

                tab.Disc = Round(rt, "Disc", edited ? HudBrand.Midnight : identity, Pill);
                Place(tab.Disc.rectTransform, 6, 6, 52, 52);
                tab.Initial = Text(tab.Disc.transform, "Initial", WideFont, 24, edited ? White : HudBrand.TextOn(identity), "CMJSNL".Substring(i, 1), 0f, TextAlignmentOptions.Midline);
                Stretch(tab.Initial.rectTransform);
                Image mask = Round(rt, "Portrait Mask", Color.white, Pill);
                Place(mask.rectTransform, 6, 6, 52, 52);
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                tab.Portrait = new GameObject("Portrait", typeof(RectTransform)).AddComponent<RawImage>();
                tab.Portrait.rectTransform.SetParent(mask.rectTransform, false);
                Stretch(tab.Portrait.rectTransform);
                tab.Portrait.raycastTarget = false;
                tab.Portrait.enabled = false;
                tab.Name = TextAt(rt, "Name", BoldFont, 26, edited ? HudBrand.TextOn(identity) : White, "PLAYER", 70, 0, tabW - 84, tabH);
                AutoSize(tab.Name, 16, 26);

                HudChip turn = Chip(rt, "Your Shot", identity, false, BoldFont, 16, HudBrand.TextOn(identity), "YOUR SHOT", 0.04f, 14, 26, true);
                Anchor((RectTransform)turn.Root.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(120, 26));
                tab.TurnBadge = turn.Root;
                tab.TurnFill = turn.Fill;
                tab.TurnText = turn.Text;
                turn.Root.SetActive(false);
                HudChip unsaved = Chip(rt, "Unsaved", HudBrand.Pink, false, CondensedBoldFont, 14, HudBrand.Midnight, "UNSAVED", 0.08f, 10, 22, true);
                Anchor((RectTransform)unsaved.Root.transform, new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(80, 22));
                tab.Unsaved = unsaved.Root;
                unsaved.Root.SetActive(false);

                tab.Button = AddButton(rt.gameObject, tab.NameBar);
                view.Players[i] = tab;
            }
            view.PlayerPrevHint = KeyHint(s, "Prev Hint", "Q", new Vector2(0, 0.5f), new Vector2(1, 0.5f), -12);
            view.PlayerNextHint = KeyHint(s, "Next Hint", "E", new Vector2(1, 0.5f), new Vector2(0, 0.5f), 12);
        }

        private static void BuildHintsAndToast(Transform root, ProShopView view)
        {
            var hints = new GameObject("Hints", typeof(RectTransform)).GetComponent<RectTransform>();
            hints.SetParent(root, false);
            Anchor(hints, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Edge - 4, -(PanelTop + 600 + 20)), new Vector2(400, 130));
            view.Hints = Text(hints, "Text", CondensedFont, 22, White, "", 0.06f, TextAlignmentOptions.TopRight);
            Stretch(view.Hints.rectTransform);
            view.Hints.lineSpacing = 10f;
            view.Hints.fontSharedMaterial = _shadowed;

            Image toast = Round(root, "Toast", HudBrand.Midnight.WithAlpha(0.92f), Pill);
            Anchor(toast.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, Edge + 88 + 20), new Vector2(680, 56));
            Image toastFrame = Outline(toast.transform, "Frame", LinePill, HudBrand.Pink);
            Stretch(toastFrame.rectTransform);
            view.ToastText = Text(toast.transform, "Text", BoldFont, 24, White, "SAVED · CALEB'S LOADOUT", 0f, TextAlignmentOptions.Midline);
            Stretch(view.ToastText.rectTransform);
            AutoSize(view.ToastText, 16, 24);
            view.Toast = toast.gameObject;
            toast.gameObject.SetActive(false);

            var rotate = new GameObject("Rotate Hint", typeof(RectTransform)).GetComponent<RectTransform>();
            rotate.SetParent(root, false);
            Anchor(rotate, new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(960, 1080 - 842), new Vector2(520, 90));
            Image left = Img(rotate, "Left", _rotL, White.WithAlpha(0.6f), false);
            Place(left.rectTransform, 0, 5, 80, 80);
            Image right = Img(rotate, "Right", _rotR, White.WithAlpha(0.6f), false);
            Place(right.rectTransform, 440, 5, 80, 80);
            view.RotateHint = rotate.gameObject;
        }

        // ---------------------------------------------------------------- Widgets

        private static ShopButton ActionButton(RectTransform parent, string name, float x, float y, float w, float h, Color fill, Color text, string label)
        {
            RectTransform rt = Child(parent, name, x, y, w, h);
            var b = new ShopButton { Root = rt };
            b.Fill = Round(rt, "Fill", fill, Pill);
            Stretch(b.Fill.rectTransform);
            b.Frame = FocusFrame(rt, LinePill, White);
            b.Label = Text(rt, "Label", BoldFont, 26, text, label, 0f, TextAlignmentOptions.Midline);
            Stretch(b.Label.rectTransform);
            AutoSize(b.Label, 16, 26);
            b.Button = AddButton(rt.gameObject, b.Fill);
            return b;
        }

        /// <summary>White outline shown on the focused control; stays out of any layout group.</summary>
        private static Image FocusFrame(Transform parent, Sprite sprite, Color color)
        {
            Image frame = Outline(parent, "Focus", sprite, color);
            Stretch(frame.rectTransform);
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            frame.enabled = false;
            return frame;
        }

        /// <summary>An outlined keycap hugging one side of the player strip; the view toggles its parent.</summary>
        private static TMP_Text KeyHint(RectTransform strip, string name, string key, Vector2 anchor, Vector2 pivot, float x)
        {
            Image cap = Round(strip, name, HudBrand.Midnight.WithAlpha(0.7f), 10);
            Anchor(cap.rectTransform, anchor, pivot, new Vector2(x, 0), new Vector2(40, 40));
            Image line = Outline(cap.transform, "Outline", LineR10, White.WithAlpha(0.5f));
            Stretch(line.rectTransform);
            TMP_Text text = Text(cap.transform, "Key", CondensedBoldFont, 20, White, key, 0f, TextAlignmentOptions.Midline);
            Stretch(text.rectTransform);
            return text;
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
    }
}
