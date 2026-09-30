using System.Collections.Generic;
using RealGolf.Data;
using RealGolf.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace RealGolf.UI
{
    /// <summary>Runtime-built HUD: shot info, the swing meter, scorecard and results.</summary>
    public sealed class GameHUD : MonoBehaviour
    {
        private const float MeterMin = -0.35f;
        private const float MeterMax = 1f;
        private const float MeterWidth = 660f;
        private const float MeterHeight = 40f;
        private const float MetresToYards = 1.09361f;

        private Text _holeText;
        private Text _playerText;
        private Text _lieText;
        private Text _distanceText;
        private Text _windText;
        private Text _shapeText;
        private Text _clubText;
        private Text _meterHint;
        private Text _messageText;
        private Text _controlsText;
        private Text _leaderboardText;

        private RectTransform _meterRoot;
        private RectTransform _powerFill;
        private RectTransform _indicator;
        private Image _powerFillImage;

        private GameObject _scorecardPanel;
        private GameObject _resultsPanel;
        private Text _resultsText;
        private readonly List<Text> _scorecardCells = new();

        private float _messageTimer;

        public bool ScorecardVisible => _scorecardPanel != null && _scorecardPanel.activeSelf;

        public void Build()
        {
            Canvas canvas = UIFactory.CreateCanvas("GolfHUD");
            canvas.transform.SetParent(transform, false);

            BuildInfoPanel(canvas.transform);
            BuildLeaderboard(canvas.transform);
            BuildMeter(canvas.transform);
            BuildMessage(canvas.transform);
            BuildScorecard(canvas.transform);
            BuildResults(canvas.transform);
        }

        private void BuildInfoPanel(Transform parent)
        {
            Image panel = UIFactory.CreatePanel("InfoPanel", parent, new Color(0f, 0f, 0f, 0.42f));
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(28f, -28f);
            rect.sizeDelta = new Vector2(430f, 252f);

            _holeText = UIFactory.CreateText("Hole", rect, "Hole 1  ·  Par 4", 30, TextAnchor.UpperLeft, FontStyle.Bold).WithShadow();
            Place(_holeText, 18f, -14f, 400f, 38f);

            _playerText = UIFactory.CreateText("Player", rect, "Player 1", 26).WithShadow();
            Place(_playerText, 18f, -54f, 400f, 34f);

            _distanceText = UIFactory.CreateText("Distance", rect, "To pin: 0 yds", 24).WithShadow();
            Place(_distanceText, 18f, -92f, 400f, 32f);

            _lieText = UIFactory.CreateText("Lie", rect, "Lie: Tee Box", 22).WithShadow();
            Place(_lieText, 18f, -126f, 400f, 30f);

            _windText = UIFactory.CreateText("Wind", rect, "Wind: Calm", 22).WithShadow();
            Place(_windText, 18f, -158f, 400f, 30f);
            _windText.color = new Color(0.72f, 0.88f, 1f);

            _shapeText = UIFactory.CreateText("Shape", rect, "Shape: Stock", 22).WithShadow();
            Place(_shapeText, 18f, -190f, 400f, 30f);
            _shapeText.color = new Color(1f, 0.9f, 0.62f);

            _controlsText = UIFactory.CreateText(
                "Controls", parent,
                "A / D  aim      W / S  spin      Q / E  club      SPACE  swing (3 clicks)      TAB  card      C  overhead",
                19, TextAnchor.LowerCenter).WithShadow();
            var controlsRect = (RectTransform)_controlsText.transform;
            controlsRect.anchorMin = new Vector2(0.5f, 0f);
            controlsRect.anchorMax = new Vector2(0.5f, 0f);
            controlsRect.pivot = new Vector2(0.5f, 0f);
            controlsRect.anchoredPosition = new Vector2(0f, 16f);
            controlsRect.sizeDelta = new Vector2(1400f, 26f);
            _controlsText.color = new Color(1f, 1f, 1f, 0.75f);
        }

        private void BuildLeaderboard(Transform parent)
        {
            Image panel = UIFactory.CreatePanel("Leaderboard", parent, new Color(0f, 0f, 0f, 0.42f));
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-28f, -28f);
            rect.sizeDelta = new Vector2(330f, 190f);

            Text title = UIFactory.CreateText("Title", rect, "LEADERBOARD", 20, TextAnchor.UpperLeft, FontStyle.Bold).WithShadow();
            Place(title, 18f, -12f, 300f, 26f);
            title.color = new Color(1f, 0.93f, 0.55f);

            _leaderboardText = UIFactory.CreateText("Entries", rect, string.Empty, 22).WithShadow();
            Place(_leaderboardText, 18f, -44f, 300f, 130f);
        }

        private void BuildMeter(Transform parent)
        {
            Image background = UIFactory.CreatePanel("MeterBackground", parent, new Color(0f, 0f, 0f, 0.62f));
            _meterRoot = (RectTransform)background.transform;
            _meterRoot.anchorMin = new Vector2(0.5f, 0f);
            _meterRoot.anchorMax = new Vector2(0.5f, 0f);
            _meterRoot.pivot = new Vector2(0.5f, 0f);
            _meterRoot.anchoredPosition = new Vector2(0f, 56f);
            _meterRoot.sizeDelta = new Vector2(MeterWidth + 24f, MeterHeight + 74f);

            _clubText = UIFactory.CreateText("Club", _meterRoot, "Driver  ·  230 yds", 24, TextAnchor.UpperCenter, FontStyle.Bold).WithShadow();
            var clubRect = (RectTransform)_clubText.transform;
            clubRect.anchorMin = new Vector2(0.5f, 1f);
            clubRect.anchorMax = new Vector2(0.5f, 1f);
            clubRect.pivot = new Vector2(0.5f, 1f);
            clubRect.anchoredPosition = new Vector2(0f, -8f);
            clubRect.sizeDelta = new Vector2(MeterWidth, 30f);

            Image track = UIFactory.CreatePanel("Track", _meterRoot, new Color(0.12f, 0.12f, 0.12f, 0.95f));
            var trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(0.5f, 0f);
            trackRect.anchorMax = new Vector2(0.5f, 0f);
            trackRect.pivot = new Vector2(0.5f, 0f);
            trackRect.anchoredPosition = new Vector2(0f, 30f);
            trackRect.sizeDelta = new Vector2(MeterWidth, MeterHeight);

            // The accuracy target sits where the meter reads exactly zero.
            Image zoneMarker = UIFactory.CreatePanel("AccuracyZone", trackRect, new Color(0.25f, 0.85f, 0.35f, 0.35f));
            var zoneRect = (RectTransform)zoneMarker.transform;
            zoneRect.anchorMin = new Vector2(0f, 0f);
            zoneRect.anchorMax = new Vector2(0f, 1f);
            zoneRect.pivot = new Vector2(0.5f, 0.5f);
            zoneRect.sizeDelta = new Vector2(26f, 0f);
            zoneRect.anchoredPosition = new Vector2(ValueToX(0f), 0f);

            Image zeroLine = UIFactory.CreatePanel("ZeroLine", trackRect, new Color(0.4f, 1f, 0.5f, 0.95f));
            var zeroRect = (RectTransform)zeroLine.transform;
            zeroRect.anchorMin = new Vector2(0f, 0f);
            zeroRect.anchorMax = new Vector2(0f, 1f);
            zeroRect.pivot = new Vector2(0.5f, 0.5f);
            zeroRect.sizeDelta = new Vector2(3f, 0f);
            zeroRect.anchoredPosition = new Vector2(ValueToX(0f), 0f);

            Image fill = UIFactory.CreatePanel("PowerFill", trackRect, new Color(0.35f, 0.8f, 0.35f, 0.85f));
            _powerFill = (RectTransform)fill.transform;
            _powerFillImage = fill;
            _powerFill.anchorMin = new Vector2(0f, 0f);
            _powerFill.anchorMax = new Vector2(0f, 1f);
            _powerFill.pivot = new Vector2(0f, 0.5f);
            _powerFill.anchoredPosition = new Vector2(ValueToX(0f), 0f);
            _powerFill.sizeDelta = new Vector2(0f, -8f);

            Image indicator = UIFactory.CreatePanel("Indicator", trackRect, Color.white);
            _indicator = (RectTransform)indicator.transform;
            _indicator.anchorMin = new Vector2(0f, 0f);
            _indicator.anchorMax = new Vector2(0f, 1f);
            _indicator.pivot = new Vector2(0.5f, 0.5f);
            _indicator.sizeDelta = new Vector2(5f, 8f);
            _indicator.anchoredPosition = new Vector2(ValueToX(0f), 0f);

            _meterHint = UIFactory.CreateText("Hint", _meterRoot, "SPACE to start swing", 20, TextAnchor.LowerCenter).WithShadow();
            var hintRect = (RectTransform)_meterHint.transform;
            hintRect.anchorMin = new Vector2(0.5f, 0f);
            hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 4f);
            hintRect.sizeDelta = new Vector2(MeterWidth, 24f);
        }

        private void BuildMessage(Transform parent)
        {
            _messageText = UIFactory.CreateText("Message", parent, string.Empty, 58, TextAnchor.MiddleCenter, FontStyle.Bold).WithShadow();
            var rect = (RectTransform)_messageText.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 150f);
            rect.sizeDelta = new Vector2(1500f, 120f);
            _messageText.enabled = false;
        }

        private void BuildScorecard(Transform parent)
        {
            Image panel = UIFactory.CreatePanel("Scorecard", parent, new Color(0.03f, 0.09f, 0.05f, 0.94f));
            _scorecardPanel = panel.gameObject;
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1560f, 460f);

            Text title = UIFactory.CreateText("Title", rect, "SCORECARD", 34, TextAnchor.UpperCenter, FontStyle.Bold).WithShadow();
            Place(title, 0f, -18f, 1560f, 40f);
            ((RectTransform)title.transform).anchorMin = new Vector2(0.5f, 1f);
            ((RectTransform)title.transform).anchorMax = new Vector2(0.5f, 1f);
            ((RectTransform)title.transform).pivot = new Vector2(0.5f, 1f);
            ((RectTransform)title.transform).anchoredPosition = new Vector2(0f, -18f);

            _scorecardPanel.SetActive(false);
        }

        private void BuildResults(Transform parent)
        {
            Image panel = UIFactory.CreatePanel("Results", parent, new Color(0.02f, 0.06f, 0.03f, 0.96f));
            _resultsPanel = panel.gameObject;
            UIFactory.Stretch((RectTransform)panel.transform);

            _resultsText = UIFactory.CreateText("ResultsText", panel.transform, string.Empty, 40, TextAnchor.MiddleCenter, FontStyle.Bold).WithShadow();
            UIFactory.Stretch((RectTransform)_resultsText.transform, 60f);

            _resultsPanel.SetActive(false);
        }

        private static void Place(Text text, float x, float y, float width, float height)
        {
            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static float ValueToX(float value)
        {
            float t = Mathf.InverseLerp(MeterMin, MeterMax, value);
            return t * MeterWidth;
        }

        private void Update()
        {
            if (_messageTimer > 0f)
            {
                _messageTimer -= Time.deltaTime;
                if (_messageTimer <= 0f)
                {
                    _messageText.enabled = false;
                }
            }
        }

        public void SetHole(HoleDefinition hole)
        {
            int yards = Mathf.RoundToInt(hole.LengthMetres * MetresToYards);
            _holeText.text = $"Hole {hole.Number}  ·  {hole.ParLabel}  ·  {yards} yds";
        }

        public void SetPlayer(string playerName, Color color)
        {
            _playerText.text = playerName;
            _playerText.color = color;
        }

        public void SetLie(SurfaceType surface) => _lieText.text = $"Lie: {surface.DisplayName()}";

        /// <summary>Wind is described relative to the shot, not as a compass bearing.</summary>
        public void SetWind(Wind wind, Vector3 shotDirection) =>
            _windText.text = wind == null ? "Wind: Calm" : $"Wind: {wind.Describe(shotDirection)}";

        public void SetShotShape(string label) => _shapeText.text = $"Shape: {label}";

        /// <summary>On the green the shape row becomes the slope read instead.</summary>
        public void SetPuttRead(string label) => _shapeText.text = $"Read: {label}";

        public void SetDistanceToPin(float metres)
        {
            _distanceText.text = metres < 2.2f
                ? $"To pin: {metres * 100f:0} cm"
                : $"To pin: {metres * MetresToYards:0} yds";
        }

        public void SetClub(Club club, float estimatedMetres)
        {
            string distance = club.IsPutter
                ? $"{estimatedMetres * MetresToYards:0} yds roll"
                : $"{estimatedMetres * MetresToYards:0} yds";
            _clubText.text = $"{club.Name}  ·  {distance}";
        }

        public void SetMeterVisible(bool visible) => _meterRoot.gameObject.SetActive(visible);

        public void UpdateMeter(SwingStage stage, float meterValue, float lockedPower)
        {
            float zeroX = ValueToX(0f);
            float indicatorX = ValueToX(Mathf.Clamp(meterValue, MeterMin, MeterMax));
            _indicator.anchoredPosition = new Vector2(indicatorX, 0f);

            float powerValue = stage == SwingStage.Power ? Mathf.Max(0f, meterValue) : lockedPower;
            float powerX = ValueToX(Mathf.Clamp(powerValue, 0f, MeterMax));
            _powerFill.anchoredPosition = new Vector2(zeroX, 0f);
            _powerFill.sizeDelta = new Vector2(Mathf.Max(0f, powerX - zeroX), -8f);

            _powerFillImage.color = Color.Lerp(
                new Color(0.35f, 0.82f, 0.35f, 0.85f),
                new Color(0.92f, 0.28f, 0.22f, 0.9f),
                Mathf.Clamp01(powerValue));

            _meterHint.text = stage switch
            {
                SwingStage.Ready => "SPACE to start swing",
                SwingStage.Power => "SPACE to set power",
                SwingStage.Accuracy => "SPACE on the green line for a pure strike",
                _ => string.Empty
            };
        }

        public void ShowMessage(string message, float duration = 2.2f)
        {
            _messageText.text = message;
            _messageText.enabled = true;
            _messageTimer = duration;
        }

        public void HideMessage()
        {
            _messageTimer = 0f;
            _messageText.enabled = false;
        }

        public void UpdateLeaderboard(IReadOnlyList<GolfPlayer> players, CourseDefinition course)
        {
            var sorted = new List<GolfPlayer>(players);
            sorted.Sort((a, b) => a.RelativeToPar(course).CompareTo(b.RelativeToPar(course)));

            var builder = new System.Text.StringBuilder();
            foreach (GolfPlayer player in sorted)
            {
                string relative = GolfPlayer.FormatRelative(player.RelativeToPar(course));
                builder.AppendLine($"{player.Name,-10} {relative,4}   thru {player.HolesPlayed}");
            }

            _leaderboardText.text = builder.ToString();
        }

        public void ToggleScorecard(IReadOnlyList<GolfPlayer> players, CourseDefinition course)
        {
            bool show = !_scorecardPanel.activeSelf;
            _scorecardPanel.SetActive(show);
            if (show)
            {
                RefreshScorecard(players, course);
            }
        }

        public void RefreshScorecard(IReadOnlyList<GolfPlayer> players, CourseDefinition course)
        {
            foreach (Text cell in _scorecardCells)
            {
                if (cell != null)
                {
                    Destroy(cell.gameObject);
                }
            }

            _scorecardCells.Clear();

            var grid = (RectTransform)_scorecardPanel.transform;
            const float cellWidth = 66f;
            const float cellHeight = 40f;
            const float startX = 40f;
            const float startY = -78f;
            const float labelWidth = 150f;

            void Cell(string content, int column, int row, Color color, bool bold = false)
            {
                float x = column < 0 ? startX : startX + labelWidth + column * cellWidth;
                float width = column < 0 ? labelWidth : cellWidth;

                Text text = UIFactory.CreateText(
                    $"Cell_{column}_{row}", grid, content, 21,
                    column < 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
                    bold ? FontStyle.Bold : FontStyle.Normal);

                var rect = (RectTransform)text.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(x, startY - row * cellHeight);
                rect.sizeDelta = new Vector2(width, cellHeight);
                text.color = color;

                _scorecardCells.Add(text);
            }

            var headerColor = new Color(1f, 0.92f, 0.55f);
            Cell("HOLE", -1, 0, headerColor, true);
            for (int i = 0; i < course.Holes.Count; i++)
            {
                Cell((i + 1).ToString(), i, 0, headerColor, true);
            }

            Cell("TOT", course.Holes.Count, 0, headerColor, true);

            var parColor = new Color(0.72f, 0.85f, 0.72f);
            Cell("PAR", -1, 1, parColor, true);
            for (int i = 0; i < course.Holes.Count; i++)
            {
                Cell(course.Holes[i].Par.ToString(), i, 1, parColor);
            }

            Cell(course.TotalPar.ToString(), course.Holes.Count, 1, parColor, true);

            for (int p = 0; p < players.Count; p++)
            {
                GolfPlayer player = players[p];
                int row = 2 + p;
                Color color = PlayerUIColor(p);

                Cell(player.Name, -1, row, color, true);
                for (int i = 0; i < course.Holes.Count; i++)
                {
                    int strokes = player.Strokes[i];
                    Cell(strokes == GolfPlayer.NotPlayed ? "-" : strokes.ToString(), i, row, ScoreColor(strokes, course.Holes[i].Par, color));
                }

                string total = player.HolesPlayed == 0
                    ? "-"
                    : $"{player.TotalStrokes} ({GolfPlayer.FormatRelative(player.RelativeToPar(course))})";
                Cell(total, course.Holes.Count, row, color, true);
            }
        }

        private static Color ScoreColor(int strokes, int par, Color fallback)
        {
            if (strokes == GolfPlayer.NotPlayed)
            {
                return new Color(0.5f, 0.5f, 0.5f);
            }

            int diff = strokes - par;
            return diff switch
            {
                < 0 => new Color(1f, 0.55f, 0.55f),
                0 => fallback,
                _ => new Color(0.62f, 0.78f, 1f)
            };
        }

        public static Color PlayerUIColor(int index)
        {
            Color[] palette =
            {
                new(0.96f, 0.96f, 0.96f),
                new(0.98f, 0.48f, 0.42f),
                new(0.45f, 0.72f, 1f),
                new(1f, 0.88f, 0.35f)
            };

            return palette[index % palette.Length];
        }

        public void ShowResults(IReadOnlyList<GolfPlayer> players, CourseDefinition course)
        {
            var sorted = new List<GolfPlayer>(players);
            sorted.Sort((a, b) => a.TotalStrokes.CompareTo(b.TotalStrokes));

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("ROUND COMPLETE");
            builder.AppendLine();
            builder.AppendLine($"{course.Name}  ·  Par {course.TotalPar}");
            builder.AppendLine();

            for (int i = 0; i < sorted.Count; i++)
            {
                GolfPlayer player = sorted[i];
                string medal = i == 0 ? "WINNER" : $"{i + 1}.";
                builder.AppendLine($"{medal}   {player.Name}   {player.TotalStrokes}  ({GolfPlayer.FormatRelative(player.RelativeToPar(course))})");
            }

            builder.AppendLine();

            // R regenerates from the same seed, so it is a replay of this layout,
            // not a new one. Esc is the only way back to a different course.
            builder.AppendLine("Press R to replay  ·  Esc for the course list");

            _resultsText.text = builder.ToString();
            _resultsPanel.SetActive(true);
        }
    }
}
