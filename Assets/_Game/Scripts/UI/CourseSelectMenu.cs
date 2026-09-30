using System;
using System.Collections.Generic;
using RealGolf.Core;
using RealGolf.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace RealGolf.UI
{
    /// <summary>What the player chose before the round starts.</summary>
    public readonly struct CourseSelection
    {
        public readonly int Seed;
        public readonly string CourseName;
        public readonly int PlayerCount;

        public CourseSelection(int seed, string courseName, int playerCount)
        {
            Seed = seed;
            CourseName = courseName;
            PlayerCount = playerCount;
        }
    }

    /// <summary>
    /// The front end: pick a course from <see cref="CourseLibrary"/>, pick how many
    /// play, then start the round. Built in code over the live sky, the same way the
    /// HUD is, so there is no scene wiring to keep in sync.
    ///
    /// The layout is written in 1920x1080 reference pixels from the top-left, which
    /// is what the canvas scaler is configured for, so the numbers below read as
    /// positions on a 1080p screen regardless of the window's real size.
    /// </summary>
    public sealed class CourseSelectMenu : MonoBehaviour
    {
        private const float CardHeight = 86f;
        private const float CardGap = 12f;
        private const float CardWidth = 560f;
        private const float ListTop = 250f;
        private const float Margin = 120f;
        private const int MaxPlayers = 4;

        /// <summary>Fired once, when the player commits. The menu tears itself down first.</summary>
        public event Action<CourseSelection> Confirmed;

        private Canvas _canvas;
        private int _selected;
        private int _playerCount = 2;

        /// <summary>
        /// Destroy only takes effect at the end of the frame, so a click on Tee Off
        /// in the same frame as an Enter press would otherwise fire Confirmed twice
        /// and build two GameManagers over one course.
        /// </summary>
        private bool _committed;

        private readonly List<Image> _cardImages = new();
        private readonly List<Text> _cardNames = new();
        private readonly List<Image> _playerButtons = new();
        private readonly List<Text> _playerLabels = new();
        private readonly Image[] _stars = new Image[5];

        private Text _detailName;
        private Text _detailLocation;
        private Text _detailBlurb;
        private Text _detailPar;
        private Text _detailLength;
        private Text _detailHazards;

        public void Build()
        {
            EnsureEventSystem();
            FrameCameraOnSky();

            _canvas = UIFactory.CreateCanvas("CourseSelectCanvas", 500);
            Transform root = _canvas.transform;

            Image scrim = UIFactory.CreatePanel("Scrim", root, UITheme.Scrim);
            UIFactory.Stretch(scrim.rectTransform);

            BuildHeader(root);
            BuildCourseList(root);
            BuildDetailPanel(root);
            BuildFooter(root);

            Select(0);
            SetPlayerCount(_playerCount);
        }

        /// <summary>
        /// Buttons do nothing without one, and nothing else in this project has ever
        /// needed it -- the HUD is display-only -- so the menu creates it.
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        /// <summary>
        /// No course exists yet, so the only thing to look at is the sky and the
        /// backdrop rings the bootstrap already built. Tilting the camera just above
        /// the horizon puts the haze band behind the menu instead of empty zenith.
        /// </summary>
        private static void FrameCameraOnSky()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            cam.transform.position = new Vector3(0f, 12f, -40f);
            cam.transform.rotation = Quaternion.Euler(4f, 18f, 0f);
        }

        private void BuildHeader(Transform root)
        {
            Text title = UIFactory.CreateText(
                "Title", root, "REAL GOLF", 88, TextAnchor.UpperLeft, FontStyle.Normal, UITheme.TitleFont);
            title.color = UITheme.Heading;
            title.WithShadow();
            Place(title.rectTransform, Margin, 64f, 1000f, 110f);

            Text subtitle = UIFactory.CreateText(
                "Subtitle", root, "CHOOSE YOUR COURSE", 26, TextAnchor.UpperLeft, FontStyle.Normal, UITheme.BodyFont);
            subtitle.color = UITheme.Muted;
            Place(subtitle.rectTransform, Margin + 4f, 178f, 800f, 36f);
        }

        private void BuildCourseList(Transform root)
        {
            IReadOnlyList<CoursePreset> presets = CourseLibrary.Presets;

            for (int i = 0; i < presets.Count; i++)
            {
                CoursePreset preset = presets[i];
                int index = i;

                Image card = UIFactory.CreatePanel($"Course_{preset.Name}", root, Color.white);
                Place(card.rectTransform, Margin, ListTop + i * (CardHeight + CardGap), CardWidth, CardHeight);
                Chrome(card, UITheme.CardIdle, new Color(0.10f, 0.14f, 0.11f, 0.9f));
                MakeButton(card, UITheme.CardIdle, UITheme.CardHover, UITheme.CardSelected)
                    .onClick.AddListener(() => Select(index));

                Text label = UIFactory.CreateText(
                    "Name", card.transform, preset.Name.ToUpperInvariant(), 28,
                    TextAnchor.MiddleLeft, FontStyle.Normal, UITheme.BodyFont);
                label.color = UITheme.OnButton;
                Place(label.rectTransform, 28f, 18f, CardWidth - 56f, 30f);

                Text sub = UIFactory.CreateText(
                    "Location", card.transform, preset.Location, 18,
                    TextAnchor.MiddleLeft, FontStyle.Normal, UITheme.BodyFont);
                sub.color = new Color(UITheme.OnButton.r, UITheme.OnButton.g, UITheme.OnButton.b, 0.72f);
                Place(sub.rectTransform, 28f, 50f, CardWidth - 56f, 22f);

                _cardImages.Add(card);
                _cardNames.Add(label);
            }

            Text hint = UIFactory.CreateText(
                "Hint", root,
                "Up / Down to browse     1-4 sets players     Enter to tee off",
                20, TextAnchor.UpperLeft, FontStyle.Normal, UITheme.BodyFont);
            hint.color = UITheme.Muted;
            Place(hint.rectTransform, Margin, ListTop + presets.Count * (CardHeight + CardGap) + 16f, 900f, 28f);
        }

        private void BuildDetailPanel(Transform root)
        {
            const float x = 760f;
            const float width = 1040f;

            Image panel = UIFactory.CreatePanel("Detail", root, new Color(0.04f, 0.08f, 0.05f, 0.72f));
            Place(panel.rectTransform, x, ListTop, width, 430f);
            Transform p = panel.transform;

            _detailName = UIFactory.CreateText(
                "DetailName", p, string.Empty, 52, TextAnchor.UpperLeft, FontStyle.Normal, UITheme.TitleFont);
            _detailName.color = UITheme.Heading;
            Place(_detailName.rectTransform, 40f, 32f, width - 80f, 62f);

            _detailLocation = UIFactory.CreateText(
                "DetailLocation", p, string.Empty, 24, TextAnchor.UpperLeft, FontStyle.Normal, UITheme.BodyFont);
            _detailLocation.color = UITheme.Muted;
            Place(_detailLocation.rectTransform, 40f, 100f, width - 80f, 32f);

            Image divider = UIFactory.CreatePanel("Divider", p, new Color(1f, 1f, 1f, 0.18f));
            Chrome(divider, UITheme.Divider, new Color(1f, 1f, 1f, 0.18f));
            Place(divider.rectTransform, 40f, 146f, width - 80f, 8f);

            _detailBlurb = UIFactory.CreateText(
                "DetailBlurb", p, string.Empty, 26, TextAnchor.UpperLeft, FontStyle.Normal, UITheme.BodyFont);
            _detailBlurb.color = UITheme.Body;
            _detailBlurb.horizontalOverflow = HorizontalWrapMode.Wrap;
            Place(_detailBlurb.rectTransform, 40f, 172f, width - 80f, 70f);

            _detailPar = StatRow(p, "StatPar", 40f, 258f);
            _detailLength = StatRow(p, "StatLength", 40f, 300f);
            _detailHazards = StatRow(p, "StatHazards", 40f, 342f);

            Text starLabel = UIFactory.CreateText(
                "StarLabel", p, "DIFFICULTY", 20, TextAnchor.UpperLeft, FontStyle.Normal, UITheme.BodyFont);
            starLabel.color = UITheme.Muted;
            Place(starLabel.rectTransform, 620f, 262f, 240f, 26f);

            for (int i = 0; i < _stars.Length; i++)
            {
                Image star = UIFactory.CreatePanel($"Star_{i}", p, Color.white);
                Place(star.rectTransform, 620f + i * 48f, 296f, 40f, 40f);
                _stars[i] = star;
            }
        }

        private static Text StatRow(Transform parent, string name, float x, float y)
        {
            Text text = UIFactory.CreateText(
                name, parent, string.Empty, 24, TextAnchor.UpperLeft, FontStyle.Normal, UITheme.BodyFont);
            text.color = UITheme.Body;
            Place(text.rectTransform, x, y, 540f, 30f);
            return text;
        }

        private void BuildFooter(Transform root)
        {
            const float y = 760f;

            Text label = UIFactory.CreateText(
                "PlayersLabel", root, "PLAYERS", 22, TextAnchor.UpperLeft, FontStyle.Normal, UITheme.BodyFont);
            label.color = UITheme.Muted;
            Place(label.rectTransform, Margin, y, 240f, 28f);

            for (int i = 1; i <= MaxPlayers; i++)
            {
                int count = i;
                Image button = UIFactory.CreatePanel($"Players_{i}", root, Color.white);
                Place(button.rectTransform, Margin + (i - 1) * 86f, y + 36f, 74f, 68f);
                Chrome(button, UITheme.SmallIdle, new Color(0.10f, 0.14f, 0.11f, 0.9f));
                MakeButton(button, UITheme.SmallIdle, UITheme.CardHover, UITheme.SmallSelected)
                    .onClick.AddListener(() => SetPlayerCount(count));

                Text number = UIFactory.CreateText(
                    "Count", button.transform, i.ToString(), 30,
                    TextAnchor.MiddleCenter, FontStyle.Normal, UITheme.BodyFont);
                number.color = UITheme.OnButton;
                UIFactory.Stretch(number.rectTransform);

                _playerButtons.Add(button);
                _playerLabels.Add(number);
            }

            Image random = UIFactory.CreatePanel("RandomCourse", root, Color.white);
            Place(random.rectTransform, 760f, y + 36f, 320f, 68f);
            Chrome(random, UITheme.SmallIdle, new Color(0.10f, 0.14f, 0.11f, 0.9f));
            MakeButton(random, UITheme.SmallIdle, UITheme.CardHover, UITheme.CardSelected)
                .onClick.AddListener(StartRandomCourse);

            Text randomLabel = UIFactory.CreateText(
                "RandomLabel", random.transform, "RANDOM COURSE", 22,
                TextAnchor.MiddleCenter, FontStyle.Normal, UITheme.BodyFont);
            randomLabel.color = UITheme.OnButton;
            UIFactory.Stretch(randomLabel.rectTransform);

            Image play = UIFactory.CreatePanel("Play", root, Color.white);
            Place(play.rectTransform, 1440f, y + 20f, 360f, 100f);
            Chrome(play, UITheme.PlayIdle, new Color(0.18f, 0.46f, 0.22f, 0.95f));
            MakeButton(play, UITheme.PlayIdle, UITheme.PlayHover, UITheme.PlayPressed)
                .onClick.AddListener(StartSelectedCourse);

            Text playLabel = UIFactory.CreateText(
                "PlayLabel", play.transform, "TEE OFF", 36,
                TextAnchor.MiddleCenter, FontStyle.Normal, UITheme.TitleFont);
            playLabel.color = UITheme.OnButton;
            UIFactory.Stretch(playLabel.rectTransform);
        }

        private void Update()
        {
            if (_committed)
            {
                return;
            }

            if (GolfInput.MenuDownPressed)
            {
                Select(_selected + 1);
            }

            if (GolfInput.MenuUpPressed)
            {
                Select(_selected - 1);
            }

            for (int i = 1; i <= MaxPlayers; i++)
            {
                if (GolfInput.DigitPressed(i))
                {
                    SetPlayerCount(i);
                }
            }

            if (GolfInput.MenuSubmitPressed)
            {
                StartSelectedCourse();
            }
        }

        private void Select(int index)
        {
            int count = CourseLibrary.Presets.Count;
            if (count == 0)
            {
                return;
            }

            // Wraps, so holding down past the end returns to the top rather than
            // sticking, which is what a six-item list should do.
            _selected = (index % count + count) % count;

            for (int i = 0; i < _cardImages.Count; i++)
            {
                bool active = i == _selected;
                Sprite sprite = active ? UITheme.CardSelected : UITheme.CardIdle;
                if (sprite != null)
                {
                    _cardImages[i].sprite = sprite;
                }
                else
                {
                    _cardImages[i].color = active
                        ? new Color(0.16f, 0.34f, 0.18f, 0.95f)
                        : new Color(0.10f, 0.14f, 0.11f, 0.9f);
                }

                _cardNames[i].fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
            }

            ShowDetail(CourseLibrary.Presets[_selected]);
        }

        private void ShowDetail(CoursePreset preset)
        {
            CourseStats stats = CourseLibrary.StatsFor(preset);

            _detailName.text = preset.Name.ToUpperInvariant();
            _detailLocation.text = preset.Location;
            _detailBlurb.text = preset.Blurb;
            _detailPar.text = $"{stats.Holes} holes  ·  par {stats.Par}";
            _detailLength.text = $"{stats.TotalMetres:N0} m  ·  longest hole {stats.LongestHoleMetres:N0} m";
            _detailHazards.text = $"{stats.Bunkers} bunkers  ·  {stats.WaterHazards} water";

            for (int i = 0; i < _stars.Length; i++)
            {
                bool filled = i < stats.Difficulty;
                Sprite sprite = filled ? UITheme.StarFilled : UITheme.StarEmpty;
                if (sprite != null)
                {
                    _stars[i].sprite = sprite;
                    _stars[i].color = Color.white;
                }
                else
                {
                    _stars[i].sprite = null;
                    _stars[i].color = filled ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 1f, 1f, 0.2f);
                }
            }
        }

        private void SetPlayerCount(int count)
        {
            _playerCount = Mathf.Clamp(count, 1, MaxPlayers);

            for (int i = 0; i < _playerButtons.Count; i++)
            {
                bool active = i + 1 == _playerCount;
                Sprite sprite = active ? UITheme.SmallSelected : UITheme.SmallIdle;
                if (sprite != null)
                {
                    _playerButtons[i].sprite = sprite;
                }
                else
                {
                    _playerButtons[i].color = active
                        ? new Color(0.16f, 0.34f, 0.18f, 0.95f)
                        : new Color(0.10f, 0.14f, 0.11f, 0.9f);
                }

                _playerLabels[i].fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        private void StartSelectedCourse()
        {
            CoursePreset preset = CourseLibrary.Presets[_selected];
            Commit(new CourseSelection(preset.Seed, preset.Name, _playerCount));
        }

        /// <summary>
        /// Keeps the old RandomiseSeed behaviour reachable. No name is passed, so
        /// the round runs under CourseDefinition's default title -- there is nothing
        /// truthful to call a layout nobody has seen yet.
        /// </summary>
        private void StartRandomCourse()
        {
            Commit(new CourseSelection(UnityEngine.Random.Range(1, int.MaxValue), null, _playerCount));
        }

        private void Commit(CourseSelection selection)
        {
            if (_committed)
            {
                return;
            }

            _committed = true;

            // Closing first means a handler that rebuilds the world is never racing
            // a menu that is still alive and still reading input.
            Close();
            Confirmed?.Invoke(selection);
        }

        private void Close()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Points an Image at a Kenney sprite, or leaves it a flat colour when the
        /// art has not been imported. Sliced, because every piece of chrome here is
        /// stretched well past its authored 192x64 and the postprocessor gave them
        /// the borders to survive it.
        /// </summary>
        private static void Chrome(Image image, Sprite sprite, Color fallback)
        {
            if (sprite == null)
            {
                image.sprite = null;
                image.color = fallback;
                return;
            }

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }

        /// <summary>
        /// Sprite swap when the art is present, colour tint when it is not. Without
        /// the fallback an un-imported menu would give no feedback on hover at all.
        /// </summary>
        private static Button MakeButton(Image target, Sprite idle, Sprite hover, Sprite pressed)
        {
            var button = target.gameObject.AddComponent<Button>();
            button.targetGraphic = target;

            if (idle == null)
            {
                button.transition = Selectable.Transition.ColorTint;
                return button;
            }

            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = pressed,
                selectedSprite = hover
            };

            return button;
        }

        /// <summary>
        /// Positions in reference pixels from the top-left corner, which is how the
        /// layout above is written. Anchoring every element to the same corner keeps
        /// the menu's shape fixed as the window changes aspect.
        /// </summary>
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }
    }
}
