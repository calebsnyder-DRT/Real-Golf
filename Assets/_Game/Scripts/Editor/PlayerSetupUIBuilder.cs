using System.IO;
using System.Linq;
using Puttshack.Content;
using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using Puttshack.Runtime.Setup;
using Puttshack.Runtime.Shop;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Puttshack.EditorTools.HudUIBuilder;
using Object = UnityEngine.Object;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Builds PlayerSetupUI.prefab, the Player Setup overlay (<see cref="PlayerSetupView"/> and its controller), in the
    /// Pro Shop's brand style on the 1920x1080 reference grid: roster and player count on the left, the selected player's
    /// name, body, skin tone, hair colour and hair style on the right, the golfer centre-stage on the shop turntable. It
    /// shares the HUD's flat shapes and Ringside fonts (<see cref="HudUIBuilder"/>); swatch and choice counts follow
    /// <see cref="ProfileCatalog"/>. Once baked it is an ordinary prefab (re-baking overwrites edits).
    /// </summary>
    public static class PlayerSetupUIBuilder
    {
        public const string PrefabDir = "Assets/_Game/Prefabs/PlayerSetup";
        public const string PrefabPath = PrefabDir + "/PlayerSetupUI.prefab";
        public const string ObjectName = "Player Setup";

        private const float Edge = 40f, PanelTop = 160f, PanelRadius = 32f;
        private const float RosterW = 460f, GolferW = 520f, GolferH = 574f;
        private static readonly Color PanelFill = HudBrand.Midnight.WithAlpha(0.96f);
        private static readonly Color ChoiceFill = White.WithAlpha(0.08f);

        [MenuItem("Puttshack/Player Setup/Bake Player Setup UI", priority = 70)]
        public static void BakeMenu() => Bake();

        [MenuItem("Puttshack/Player Setup/Place Player Setup in Main Scene", priority = 71)]
        public static void PlaceMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Bake();
            PlaceInMainScene();
        }

        public static GameObject Bake()
        {
            ProShopBaker.EnsureFolder(PrefabDir);
            GameObject root = Build();
            try
            {
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok || prefab == null) throw new IOException($"Could not save {PrefabPath}.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        public static void PlaceInMainScene()
        {
            Scene scene = EditorSceneManager.OpenScene(CourseBaker.ScenePath, OpenSceneMode.Single);
            PlaceInScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException($"Could not save {CourseBaker.ScenePath}.");
        }

        /// <summary>Replaces the Player Setup instance in the open scene; GameHost finds it by type.</summary>
        public static GameObject PlaceInScene(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = Bake();
            foreach (GameObject old in scene.GetRootGameObjects().Where(g => g.GetComponentInChildren<PlayerSetupController>(true) != null).ToList())
                Object.DestroyImmediate(old);
            var setup = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            setup.name = ObjectName;
            return setup;
        }

        public static GameObject Build()
        {
            LoadBrandArt();
            var root = new GameObject("Player Setup UI", typeof(RectTransform));
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
            var view = root.AddComponent<PlayerSetupView>();
            view.Canvas = canvas;
            view.Group = group;
            root.AddComponent<PlayerSetupController>().View = view;
            Transform t = root.transform;

            BuildHeader(t, view);
            BuildRoster(t, view);
            BuildGolfer(t, view);
            BuildActions(t, view);
            FinishPills(root);
            return root;
        }

        // ---------------------------------------------------------------- Regions

        private static void BuildHeader(Transform root, PlayerSetupView view)
        {
            Image pill = Round(root, "Header", PanelFill, Pill);
            Anchor(pill.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Edge, -Edge), new Vector2(420, 88));
            HStack(pill.gameObject, 20, new RectOffset(32, 32, 12, 12), TextAnchor.MiddleLeft);
            Fit(pill.gameObject);
            RectTransform names = Child(pill.transform, "Names", 0, 0, 260, 64);
            Size(names, 260, 64);
            view.HeaderTitle = TextAt(names, "Title", CondensedFont, 18, Muted, "PLAYER SETUP", 0, 2, 260, 22, 0.12f);
            view.HeaderCount = TextAt(names, "Count", BoldFont, 34, White, "2 PLAYERS", 0, 24, 260, 40);
            AutoSize(view.HeaderCount, 22, 34);
            Image word = Img(pill.transform, "Wordmark", Wordmark, White.WithAlpha(0.85f), false);
            word.preserveAspect = true;
            Size(word, 150, 26);

            Image back = Round(root, "Back", PanelFill, Pill);
            Anchor(back.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Edge, -Edge - 12), new Vector2(260, 64));
            HStack(back.gameObject, 12, new RectOffset(12, 28, 0, 0), TextAnchor.MiddleLeft);
            Fit(back.gameObject);
            view.Back = new ShopButton { Root = back.rectTransform, Fill = back };
            view.Back.Icon = Outline(back.transform, "Key", LinePill, White.WithAlpha(0.5f));
            Size(view.Back.Icon, 68, 34);
            TMP_Text esc = Text(view.Back.Icon.transform, "Legend", CondensedBoldFont, 18, White, "ESC", 0.1f, TextAlignmentOptions.Midline);
            Stretch(esc.rectTransform);
            view.Back.Label = Text(back.transform, "Label", BoldFont, 26, White, "LOBBY");
            view.Back.Frame = FocusFrame(back.transform, LinePill);
            view.Back.Button = AddButton(back.gameObject, back);
        }

        private static void BuildRoster(Transform root, PlayerSetupView view)
        {
            const int rows = 6;
            const float pad = 12, countH = 76, rowH = 84, gap = 8, inner = RosterW - pad * 2;
            float h = pad + countH + gap + rows * rowH + (rows - 1) * gap + pad;
            Image panel = Round(root, "Roster", PanelFill, PanelRadius);
            RectTransform p = panel.rectTransform;
            Place(p, Edge, PanelTop, RosterW, h);

            RectTransform count = Child(p, "Count", pad, pad, inner, countH);
            view.Count = new SetupSection
            {
                Label = TextAt(count, "Label", CondensedFont, 22, Muted, "PLAYERS", 20, 10, 200, 28, 0.12f),
                Value = TextAt(count, "Value", CondensedFont, 18, Muted, "2 OF 6", 20, 40, 200, 24, 0.1f),
                Frame = Img(count, "Focus", LineR24, White, true),
            };
            view.Count.Frame.pixelsPerUnitMultiplier = 2f;
            Stretch(view.Count.Frame.rectTransform);
            view.Count.Frame.enabled = false;
            const float step = 56;
            view.Fewer = Stepper(count, "Fewer", "\u2212", inner - 16 - step * 2 - 64, (countH - step) / 2, step);
            view.CountValue = TextAt(count, "Number", WideFont, 34, White, "2", inner - 16 - step - 64, 0, 64, countH, 0f, TextAlignmentOptions.Midline);
            view.More = Stepper(count, "More", "+", inner - 16 - step, (countH - step) / 2, step);

            view.Rows = new SetupPlayerRow[rows];
            for (int i = 0; i < rows; i++)
            {
                RectTransform rt = Child(p, $"Player {i + 1}", pad, pad + countH + gap + i * (rowH + gap), inner, rowH);
                var row = new SetupPlayerRow { Root = rt };
                Color identity = Palette.Player(i);
                row.Fill = Round(rt, "Fill", i == 0 ? White.WithAlpha(0.12f) : Color.clear, 24);
                Stretch(row.Fill.rectTransform);
                row.Frame = Img(rt, "Focus", LineR24, HudBrand.Pink, true);
                row.Frame.pixelsPerUnitMultiplier = 2f;
                Stretch(row.Frame.rectTransform);
                row.Frame.enabled = i == 0;
                row.Disc = Round(rt, "Disc", identity, Pill);
                Place(row.Disc.rectTransform, 12, 12, 60, 60);
                row.Initial = Text(row.Disc.transform, "Initial", WideFont, 26, HudBrand.TextOn(identity), SampleNames[i].Substring(0, 1), 0f, TextAlignmentOptions.Midline);
                Stretch(row.Initial.rectTransform);
                Image mask = Round(rt, "Portrait Mask", Color.white, Pill);
                Place(mask.rectTransform, 12, 12, 60, 60);
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                row.Portrait = new GameObject("Portrait", typeof(RectTransform)).AddComponent<RawImage>();
                row.Portrait.rectTransform.SetParent(mask.rectTransform, false);
                Stretch(row.Portrait.rectTransform);
                row.Portrait.raycastTarget = false;
                row.Portrait.enabled = false;
                row.Name = TextAt(rt, "Name", BoldFont, 26, White, SampleNames[i], 90, 10, inner - 110, 34);
                AutoSize(row.Name, 16, 26);
                row.Detail = TextAt(rt, "Detail", CondensedFont, 18, Muted, "MALE \u00b7 LIGHT \u00b7 DARK BROWN SHORT", 90, 46, inner - 110, 26, 0.08f);
                AutoSize(row.Detail, 12, 18);
                row.Button = AddButton(rt.gameObject, row.Fill);
                view.Rows[i] = row;
            }
        }

        private static void BuildGolfer(Transform root, PlayerSetupView view)
        {
            const float pad = 24, inner = GolferW - pad * 2;
            Image panel = Round(root, "Golfer", PanelFill, PanelRadius);
            RectTransform p = panel.rectTransform;
            Anchor(p, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Edge, -PanelTop), new Vector2(GolferW, GolferH));

            view.Name = Section(p, "Name", "NAME", "PLAYER 1", 20, 96);
            view.NameField = NameField(p, pad, 52, inner, 64);

            view.Body = Section(p, "Body", "BODY", "MALE", 136, 92);
            view.Bodies = Choices(p, "Body", ProfileCatalog.Genders.Select(ProfileCatalog.GenderLabel).ToArray(), pad, 168, inner, 60);

            view.Skin = Section(p, "Skin", "SKIN TONE", "LIGHT", 248, 88);
            view.SkinSwatches = Swatches(p, "Skin", ProfileCatalog.SkinTones.Select(o => o.Hex).ToArray(), pad, 280, inner, 56);

            view.Hair = Section(p, "Hair", "HAIR COLOUR", "DARK BROWN", 356, 82);
            view.HairSwatches = Swatches(p, "Hair", ProfileCatalog.HairColors.Select(o => o.Hex).ToArray(), pad, 388, inner, 50);

            view.Style = Section(p, "Style", "HAIR STYLE", "SHORT", 458, 92);
            view.Styles = Choices(p, "Style", ProfileCatalog.HairStyles.Select(o => o.Name.ToUpperInvariant()).ToArray(), pad, 490, inner, 60);
        }

        private static void BuildActions(Transform root, PlayerSetupView view)
        {
            var actions = new GameObject("Actions", typeof(RectTransform)).GetComponent<RectTransform>();
            actions.SetParent(root, false);
            Anchor(actions, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Edge, -(PanelTop + GolferH + 16)), new Vector2(GolferW, 156));
            view.Shop = ActionButton(actions, "Pro Shop", 0, 0, GolferW, 64, PanelFill, White, "PRO SHOP \u00b7 OUTFIT");
            view.Start = ActionButton(actions, "Start", 0, 80, GolferW, 76, HudBrand.Pink, HudBrand.Midnight, "START ROUND");

            var hints = new GameObject("Hints", typeof(RectTransform)).GetComponent<RectTransform>();
            hints.SetParent(root, false);
            Anchor(hints, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Edge - 4, -(PanelTop + GolferH + 16 + 156 + 18)), new Vector2(GolferW, 110));
            view.Hints = Text(hints, "Text", CondensedFont, 22, White, "", 0.06f, TextAlignmentOptions.TopRight);
            Stretch(view.Hints.rectTransform);
            view.Hints.lineSpacing = 10f;
            view.Hints.fontSharedMaterial = ProShopFonts.Shadowed(CondensedFont);

            Image toast = Round(root, "Toast", HudBrand.Midnight.WithAlpha(0.92f), Pill);
            Anchor(toast.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, Edge + 20), new Vector2(680, 56));
            Image toastFrame = Outline(toast.transform, "Frame", LinePill, HudBrand.Pink);
            Stretch(toastFrame.rectTransform);
            view.ToastText = Text(toast.transform, "Text", BoldFont, 24, White, "NOT SAVED", 0f, TextAlignmentOptions.Midline);
            Stretch(view.ToastText.rectTransform);
            AutoSize(view.ToastText, 16, 24);
            view.Toast = toast.gameObject;
            toast.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- Widgets

        /// <summary>A labelled block of the golfer panel: label left, current value right, focus outline around it.</summary>
        private static SetupSection Section(RectTransform panel, string name, string label, string value, float y, float h)
        {
            float w = panel.sizeDelta.x;
            RectTransform rt = Child(panel, name, 0, y, w, 28);
            var section = new SetupSection
            {
                Label = TextAt(rt, "Label", CondensedFont, 20, Muted, label, 24, 0, 220, 28, 0.12f),
                Value = TextAt(rt, "Value", CondensedBoldFont, 20, White, value, w - 24 - 240, 0, 240, 28, 0.08f, TextAlignmentOptions.MidlineRight),
                Frame = Img(panel, name + " Focus", LineR24, White, true),
            };
            section.Frame.pixelsPerUnitMultiplier = 2f;
            Place(section.Frame.rectTransform, 10, y - 10, w - 20, h + 20);
            section.Frame.enabled = false;
            return section;
        }

        private static TMP_InputField NameField(RectTransform panel, float x, float y, float w, float h)
        {
            Image fill = Round(panel, "Name Field", ChoiceFill, Pill);
            Place(fill.rectTransform, x, y, w, h);
            fill.raycastTarget = true;
            RectTransform area = Child(fill.rectTransform, "Text Area", 28, 0, w - 56, h);
            area.gameObject.AddComponent<RectMask2D>();
            TMP_Text text = TextAt(area, "Text", BoldFont, 30, White, "", 0, 0, w - 56, h);
            TMP_Text placeholder = TextAt(area, "Placeholder", BoldFont, 30, Muted, "TYPE A NAME", 0, 0, w - 56, h);
            var input = fill.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = fill;
            input.transition = Selectable.Transition.None;
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.fontAsset = BoldFont;
            input.pointSize = 30;
            input.characterLimit = Puttshack.Session.SessionCoordinator.MaxNameLength;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.restoreOriginalTextOnEscape = true;
            input.onFocusSelectAll = true;
            input.resetOnDeActivation = true;
            input.customCaretColor = true;
            input.caretColor = HudBrand.Pink;
            input.caretWidth = 3;
            input.selectionColor = HudBrand.Pink.WithAlpha(0.35f);
            return input;
        }

        private static ShopButton[] Choices(RectTransform panel, string name, string[] labels, float x, float y, float w, float h)
        {
            const float gap = 12;
            float each = (w - gap * (labels.Length - 1)) / labels.Length;
            var buttons = new ShopButton[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                buttons[i] = ActionButton(panel, $"{name} {labels[i]}", x + i * (each + gap), y, each, h, i == 0 ? HudBrand.Pink : ChoiceFill, i == 0 ? HudBrand.Midnight : White, labels[i]);
                buttons[i].Label.fontSize = 24;
                AutoSize(buttons[i].Label, 16, 24);
            }
            return buttons;
        }

        private static SetupSwatch[] Swatches(RectTransform panel, string name, string[] hexes, float x, float y, float w, float size)
        {
            float gap = hexes.Length > 1 ? (w - size * hexes.Length) / (hexes.Length - 1) : 0;
            var swatches = new SetupSwatch[hexes.Length];
            for (int i = 0; i < hexes.Length; i++)
            {
                RectTransform rt = Child(panel, $"{name} Swatch {i + 1}", x + i * (size + gap), y, size, size);
                ColorUtility.TryParseHtmlString(hexes[i], out Color c);
                var s = new SetupSwatch { Root = rt };
                s.Fill = Round(rt, "Fill", c, Pill);
                Inset(s.Fill.rectTransform, 6);
                s.Ring = Outline(rt, "Ring", LinePill, White);
                Stretch(s.Ring.rectTransform);
                s.Ring.enabled = i == 1;
                s.Button = AddButton(rt.gameObject, s.Fill);
                swatches[i] = s;
            }
            return swatches;
        }

        private static ShopButton Stepper(RectTransform parent, string name, string label, float x, float y, float size)
        {
            ShopButton b = ActionButton(parent, name, x, y, size, size, ChoiceFill, White, label);
            b.Label.fontSize = 34;
            b.Label.enableAutoSizing = false;
            return b;
        }

        private static ShopButton ActionButton(RectTransform parent, string name, float x, float y, float w, float h, Color fill, Color text, string label)
        {
            RectTransform rt = Child(parent, name, x, y, w, h);
            var b = new ShopButton { Root = rt };
            b.Fill = Round(rt, "Fill", fill, Pill);
            Stretch(b.Fill.rectTransform);
            b.Frame = FocusFrame(rt, LinePill);
            b.Label = Text(rt, "Label", BoldFont, 26, text, label, 0f, TextAlignmentOptions.Midline);
            Stretch(b.Label.rectTransform);
            AutoSize(b.Label, 16, 26);
            b.Button = AddButton(rt.gameObject, b.Fill);
            return b;
        }

        /// <summary>White outline shown on the focused control; stays out of any layout group.</summary>
        private static Image FocusFrame(Transform parent, Sprite sprite)
        {
            Image frame = Outline(parent, "Focus", sprite, White);
            Stretch(frame.rectTransform);
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            frame.enabled = false;
            return frame;
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
