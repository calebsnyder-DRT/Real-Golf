using System.Collections.Generic;
using System.Text;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Input;
using Puttshack.Protocol;
using Puttshack.Rules;
using Puttshack.Session;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.Runtime.Hud
{
    public sealed class HudContext
    {
        public SessionSnapshot Snapshot;
        public double NowMs;
        public ShotResult Preview;
        public InputAck LastAck;
        public bool ShowScorecard;
        public string Notice;
        public string Hints;
        public AssistanceProfileId LobbyAssistance;
        public bool LobbyPaceCap;
    }

    /// <summary>
    /// Minimal shot HUD (spec section 29): the course centre stays clear. Small corner chips for hole/stroke/distance,
    /// roster, wind, club/lie and power; a thin 20-lane strip bottom-centre. Short cards sit in the lower third, and
    /// only one overlay (card, scorecard or banner) is visible at a time.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        private const int RosterSlots = SessionCoordinator.MaxPlayers;
        private const float Margin = 20f;
        private const float StripWidth = 640f;
        private const float StripHeight = 30f;

        private static readonly Color Chip = Palette.Ink.WithAlpha(0.62f);

        private GameObject _top, _bottom;
        private Text _hole, _stroke;
        private readonly Image[] _chipBg = new Image[RosterSlots];
        private readonly Image[] _chipBar = new Image[RosterSlots];
        private readonly Text[] _chipText = new Text[RosterSlots];
        private Text _wind;
        private RectTransform _windArrow;
        private Text _club, _lie;
        private readonly Image[] _laneCells = new Image[LaneMapper.LaneCount];
        private readonly Text[] _laneText = new Text[LaneMapper.LaneCount];
        private RectTransform _powerFill;
        private Text _powerValue, _status;
        private Image _statusBg;
        private GameObject _card;
        private Text _cardTitle, _cardBody, _cardFooter;
        private GameObject _banner;
        private Text _bannerText;
        private GameObject _scorecard;
        private Transform _scoreGrid;
        private long _scoreRevision = -1;
        private Text _hints;

        public static HudView Create()
        {
            var go = new GameObject("HUD", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var view = go.AddComponent<HudView>();
            view.Build(go.transform);
            return view;
        }

        private static GameObject Layer(Transform root, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        private static Text Shadowed(Text text)
        {
            text.gameObject.AddComponent<Shadow>().effectColor = Palette.Ink.WithAlpha(0.8f);
            return text;
        }

        private void Build(Transform root)
        {
            _top = Layer(root, "Top Row");
            _bottom = Layer(root, "Bottom Row");
            Transform top = _top.transform, bottom = _bottom.transform;

            Image hole = HudWidgets.Panel(top, "Hole", new Vector2(0, 1), new Vector2(0, 1), new Vector2(Margin, -Margin), new Vector2(250, 62), Chip);
            _hole = HudWidgets.Label(hole.transform, "Hole", 22, Palette.Cream, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-8, -6), FontStyle.Bold);
            _stroke = HudWidgets.Label(hole.transform, "Stroke", 18, Palette.Cyan, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(12, 6), new Vector2(-8, 0), FontStyle.Bold);

            const float chipW = 118, gap = 6;
            RectTransform roster = HudWidgets.Rect(top, "Roster", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -Margin), new Vector2(RosterSlots * (chipW + gap), 34));
            for (int i = 0; i < RosterSlots; i++)
            {
                Image chip = HudWidgets.Panel(roster, $"Chip {i + 1}", new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * (chipW + gap), 0), new Vector2(chipW, 34), Chip);
                _chipBg[i] = chip;
                _chipBar[i] = HudWidgets.Panel(chip.transform, "Identity", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(5, 34), Color.gray);
                _chipText[i] = HudWidgets.FullLabel(chip.transform, "Text", 17, Palette.Cream, TextAnchor.MiddleLeft, 0);
                _chipText[i].rectTransform.offsetMin = new Vector2(12, 0);
            }

            Image wind = HudWidgets.Panel(top, "Wind", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Margin, -Margin), new Vector2(120, 34), Chip);
            _wind = HudWidgets.Label(wind.transform, "Value", 17, Palette.Cream, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(34, 0), new Vector2(-6, 0), FontStyle.Bold);
            Text arrow = HudWidgets.Label(wind.transform, "Arrow", 24, Palette.Cyan, TextAnchor.MiddleCenter, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(4, -15), new Vector2(32, 15), FontStyle.Bold);
            arrow.text = "\u2191";
            _windArrow = arrow.rectTransform;

            Image club = HudWidgets.Panel(bottom, "Club", new Vector2(0, 0), new Vector2(0, 0), new Vector2(Margin, Margin), new Vector2(210, 52), Chip);
            _club = HudWidgets.Label(club.transform, "Club", 20, Palette.Cream, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-8, -5), FontStyle.Bold);
            _lie = HudWidgets.Label(club.transform, "Lie", 15, Palette.PaleBorder, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(12, 5), new Vector2(-8, 0));

            RectTransform lanes = HudWidgets.Rect(bottom, "Lanes", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, Margin), new Vector2(StripWidth, StripHeight));
            float cell = StripWidth / LaneMapper.LaneCount;
            for (int i = 0; i < LaneMapper.LaneCount; i++)
            {
                Image c = HudWidgets.Panel(lanes, $"Lane {i + 1}", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * cell + 1, 0), new Vector2(cell - 2, StripHeight), Chip);
                _laneCells[i] = c;
                _laneText[i] = HudWidgets.FullLabel(c.transform, "Number", 14, Palette.Cream, TextAnchor.MiddleCenter, 0);
                _laneText[i].text = (i + 1).ToString();
            }

            Image power = HudWidgets.Panel(bottom, "Power", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Margin, Margin), new Vector2(210, 52), Chip);
            _powerValue = HudWidgets.Label(power.transform, "Value", 20, Palette.Cream, TextAnchor.UpperRight, Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(-10, -5), FontStyle.Bold);
            _statusBg = HudWidgets.Panel(power.transform, "Status", new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -6), new Vector2(122, 22), Palette.MutedGreenGray);
            _status = HudWidgets.FullLabel(_statusBg.transform, "Text", 13, Palette.Cream, TextAnchor.MiddleCenter, 0, FontStyle.Bold);
            Image track = HudWidgets.Panel(power.transform, "Track", new Vector2(0, 0), new Vector2(0, 0), new Vector2(8, 8), new Vector2(194, 8), Palette.MutedGreenGray);
            _powerFill = HudWidgets.Panel(track.transform, "Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, 8), Palette.Cyan).rectTransform;

            _banner = HudWidgets.Panel(root, "Banner", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -64), new Vector2(620, 32), Palette.Warning.WithAlpha(0.92f)).gameObject;
            _bannerText = HudWidgets.FullLabel(_banner.transform, "Text", 16, Palette.Ink, TextAnchor.MiddleCenter, 4, FontStyle.Bold);

            Image card = HudWidgets.Panel(root, "Card", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(560, 120), Palette.Ink.WithAlpha(0.78f));
            _card = card.gameObject;
            _cardTitle = HudWidgets.Label(card.transform, "Title", 28, Palette.Cream, TextAnchor.UpperCenter, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -46), new Vector2(-16, -10), FontStyle.Bold);
            _cardBody = HudWidgets.Label(card.transform, "Body", 17, Palette.Cream, TextAnchor.UpperCenter, Vector2.zero, Vector2.one, new Vector2(16, 26), new Vector2(-16, -50));
            _cardFooter = HudWidgets.Label(card.transform, "Footer", 13, Palette.Cyan, TextAnchor.LowerCenter, Vector2.zero, new Vector2(1, 0), new Vector2(16, 8), new Vector2(-16, 26), FontStyle.Bold);

            Image sc = HudWidgets.Panel(root, "Scorecard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500, 520), Palette.Cream.WithAlpha(0.96f));
            HudWidgets.Border(sc, Palette.PaleBorder, 3);
            _scorecard = sc.gameObject;
            HudWidgets.Label(sc.transform, "Title", 32, Palette.Ink, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -64), new Vector2(-30, -16), FontStyle.Bold).text = "SCORECARD";
            _scoreGrid = HudWidgets.Rect(sc.transform, "Grid", Vector2.zero, Vector2.one, new Vector2(0, 1), Vector2.zero, Vector2.zero);
            ((RectTransform)_scoreGrid).offsetMin = new Vector2(30, 30);
            ((RectTransform)_scoreGrid).offsetMax = new Vector2(-30, -80);
            _scorecard.SetActive(false);

            _hints = Shadowed(HudWidgets.Label(root, "Hints", 14, Palette.Cream.WithAlpha(0.7f), TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-StripWidth / 2, Margin + StripHeight + 4), new Vector2(StripWidth / 2, Margin + StripHeight + 24)));
        }

        public void Render(HudContext ctx)
        {
            SessionSnapshot snap = ctx.Snapshot;
            SessionState state = snap.EffectiveState;
            bool inPlay = state >= SessionState.BRIEFING && state <= SessionState.HOLE_COMPLETE && state != SessionState.TUTORIAL;

            bool cardShown = RenderCard(ctx, snap);
            bool overlay = cardShown || ctx.ShowScorecard;

            _top.SetActive(inPlay && !ctx.ShowScorecard);
            _bottom.SetActive(inPlay && !overlay);
            _hints.gameObject.SetActive(inPlay && !overlay && !string.IsNullOrEmpty(ctx.Hints));
            _hints.text = ctx.Hints ?? string.Empty;

            string banner = !string.IsNullOrEmpty(snap.Data.FaultReason) && snap.State != SessionState.FAULT ? snap.Data.FaultReason : ctx.Notice;
            if (string.IsNullOrEmpty(banner) && inPlay && !snap.CabinetReady) banner = snap.CabinetStatus;
            bool showBanner = !overlay && !string.IsNullOrEmpty(banner);
            _banner.SetActive(showBanner);
            _bannerText.text = banner ?? string.Empty;

            if (inPlay) RenderPlay(ctx, snap, state);
            RenderScorecard(ctx, snap);
        }

        private void RenderPlay(HudContext ctx, SessionSnapshot snap, SessionState state)
        {
            HoleDefinition hole = snap.Hole;
            HoleProgress progress = snap.CurrentProgress;
            PlayerState player = snap.CurrentPlayer;
            ShotRecord last = snap.LastShot;
            bool showingResult = state == SessionState.SHOT_RESOLVED || state == SessionState.SHOT_PRESENTING || state == SessionState.SHOT_RESULT;

            _hole.text = $"HOLE {hole.Number}  \u00B7  PAR {hole.Par}";
            if (progress != null)
            {
                int stroke = showingResult && last != null ? last.StrokeNumber : ScoreEngine.NextStrokeNumber(progress);
                double toPin = Vec3d.HorizontalDistance(progress.Ball.Position, hole.CupM);
                _stroke.text = $"STROKE {stroke}  \u00B7  {Units.FormatDistance(toPin).ToUpperInvariant()}";
            }

            for (int i = 0; i < RosterSlots; i++)
            {
                bool present = i < snap.Data.Players.Count;
                _chipBg[i].gameObject.SetActive(present);
                if (!present) continue;
                PlayerState p = snap.Data.Players[i];
                HoleProgress hp = snap.ProgressFor(i);
                bool current = player != null && p.Id == player.Id && state != SessionState.HOLE_COMPLETE;
                Color identity = Palette.Player(p.ColorIndex);
                _chipBar[i].color = identity;
                _chipBg[i].color = current ? identity.WithAlpha(0.9f) : Chip;
                _chipText[i].fontStyle = current ? FontStyle.Bold : FontStyle.Normal;
                _chipText[i].text = $"{p.Name}  {ChipScore(hp)}";
            }

            Vec3d aim = snap.Data.Arm?.AimForward ?? AimForward(snap);
            Vec3d windH = hole.WindMps.Horizontal;
            _wind.text = windH.Length < 0.1 ? "CALM" : $"{Units.MpsToMph(windH.Length):0} MPH";
            _windArrow.localRotation = Quaternion.Euler(0, 0, -SignedYawDeg(aim, windH));

            string clubId = snap.Data.Arm?.ClubId ?? snap.Data.Setup.ClubId;
            if (showingResult && last != null) clubId = last.Intent.ClubId;
            if (clubId != null && ClubCatalog.TryGet(clubId, out ClubDefinition club))
            {
                _club.text = club.ShotType == ShotType.Putt
                    ? $"PUTTER  \u00B7  {snap.Data.Setup.PuttScale.ToString().ToUpperInvariant()}"
                    : club.DisplayName.ToUpperInvariant();
            }
            if (progress != null) _lie.text = SurfaceCatalog.Get(progress.Ball.Lie).DisplayName;

            int detected = 0;
            if (showingResult && last != null) detected = last.Intent.Lane;
            else if (ctx.LastAck != null && ctx.LastAck.Accepted && state == SessionState.SHOT_RESERVED) detected = ctx.LastAck.Lane;
            for (int i = 0; i < LaneMapper.LaneCount; i++)
            {
                int lane = i + 1;
                bool isDetected = lane == detected;
                bool center = lane == 10 || lane == 11;
                _laneCells[i].color = isDetected ? Palette.Cyan : center ? Palette.MutedGreenGray.WithAlpha(0.8f) : Chip;
                _laneText[i].color = isDetected ? Palette.Ink : Palette.Cream.WithAlpha(center ? 1f : 0.75f);
            }

            double power;
            string status;
            Color statusColor;
            switch (state)
            {
                case SessionState.SHOT_ARMED:
                    power = snap.MeterEstimate(ctx.NowMs);
                    status = "READY";
                    statusColor = Palette.Player(player?.ColorIndex ?? 0);
                    break;
                case SessionState.ARMING:
                    power = 0;
                    status = "WAIT";
                    statusColor = Palette.MutedGreenGray;
                    break;
                case SessionState.SHOT_RESERVED:
                case SessionState.SHOT_RESOLVED:
                case SessionState.SHOT_PRESENTING:
                case SessionState.SHOT_RESULT:
                    power = last?.Intent.Power01 ?? ctx.LastAck?.Power01 ?? 0;
                    status = "INPUT RECEIVED";
                    statusColor = Palette.CyanOnLight;
                    break;
                case SessionState.HANDOFF:
                    power = 0;
                    status = "NEXT PLAYER";
                    statusColor = Palette.Warning;
                    break;
                default:
                    power = snap.Data.Settings.PreviewPower;
                    status = "SET UP";
                    statusColor = Palette.MutedGreenGray;
                    break;
            }
            if (!snap.CabinetReady && (state == SessionState.SHOT_ARMED || state == SessionState.ARMING || state == SessionState.SHOT_SETUP))
            {
                status = "OFFLINE";
                statusColor = Palette.Danger;
            }
            _powerFill.sizeDelta = new Vector2(194f * (float)power, 8);
            _powerValue.text = $"{MeterWaveform.DisplayPercent(power)}%";
            _status.text = status;
            _statusBg.color = statusColor;
        }

        private static string ChipScore(HoleProgress hp)
        {
            if (hp == null) return string.Empty;
            switch (hp.Status)
            {
                case HoleStatus.HOLED: return $"\u2713{hp.CompletionScore}";
                case HoleStatus.CAPPED: return $"{hp.CompletionScore}M";
                case HoleStatus.WITHDRAWN: return "WD";
                default: return hp.StrokesSoFar.ToString();
            }
        }

        /// <summary>Returns true when a card is visible. Cards are one or two short lines.</summary>
        private bool RenderCard(HudContext ctx, SessionSnapshot snap)
        {
            string title = null, body = null, footer = "ENTER";
            HoleDefinition hole = snap.Hole;
            ShotRecord last = snap.LastShot;

            switch (snap.State)
            {
                case SessionState.ATTRACT:
                    title = "PUTTSHACK PRO";
                    body = "1-6 players  \u00B7  18 holes";
                    footer = "PRESS ENTER";
                    break;
                case SessionState.LOBBY:
                {
                    var names = new List<string>();
                    foreach (PlayerState p in snap.Data.Players) names.Add(p.Name);
                    title = string.Join("  \u00B7  ", names);
                    body = $"{AssistanceProfiles.Get(ctx.LobbyAssistance).Label} assist  \u00B7  {(ctx.LobbyPaceCap ? $"max par+{snap.Data.Settings.PaceCapOverPar}" : "no cap")}";
                    footer = "\u2191\u2193 PLAYERS   \u2190\u2192 ASSIST   C CAP   ENTER START";
                    break;
                }
                case SessionState.BRIEFING:
                    title = $"HOLE {hole.Number}  \u00B7  PAR {hole.Par}  \u00B7  {hole.YardageYd} YD";
                    body = hole.Briefing.Hazard;
                    break;
                case SessionState.SHOT_RESULT:
                    if (last?.Result != null) ResultCard(snap, last, out title, out body);
                    break;
                case SessionState.HANDOFF:
                {
                    HoleProgress hp = snap.CurrentProgress;
                    title = $"{snap.CurrentPlayer.Name.ToUpperInvariant()}  \u00B7  STROKE {ScoreEngine.NextStrokeNumber(hp)}";
                    body = $"{Units.FormatDistance(Vec3d.HorizontalDistance(hp.Ball.Position, hole.CupM))}  \u00B7  {SurfaceCatalog.Get(hp.Ball.Lie).DisplayName}";
                    break;
                }
                case SessionState.HOLE_COMPLETE:
                {
                    title = $"HOLE {hole.Number} COMPLETE";
                    var parts = new List<string>();
                    for (int i = 0; i < snap.Data.Players.Count; i++)
                        parts.Add($"{snap.Data.Players[i].Name} {ChipScore(snap.ProgressFor(i)).TrimStart('\u2713')}");
                    body = string.Join("  \u00B7  ", parts);
                    break;
                }
                case SessionState.ROUND_COMPLETE:
                    title = snap.Data.Completion == RoundCompletion.Incomplete ? "ROUND ENDED" : "FINAL";
                    body = Standings(snap);
                    break;
                case SessionState.PAUSED:
                    title = "PAUSED";
                    body = "Round saved";
                    footer = "ENTER RESUME   E END ROUND";
                    break;
                case SessionState.FAULT:
                    title = "PLEASE WAIT";
                    body = snap.Data.FaultReason;
                    footer = "STAFF: ENTER";
                    break;
            }

            bool show = title != null && !ctx.ShowScorecard;
            _card.SetActive(show);
            if (!show) return false;
            _cardTitle.text = title;
            _cardBody.text = body ?? string.Empty;
            _cardFooter.text = footer ?? string.Empty;
            int lines = string.IsNullOrEmpty(body) ? 0 : body.Split('\n').Length;
            ((RectTransform)_card.transform).sizeDelta = new Vector2(560, 96 + 22 * Mathf.Max(1, lines));
            return true;
        }

        private static void ResultCard(SessionSnapshot snap, ShotRecord last, out string title, out string body)
        {
            ShotResult r = last.Result;
            HoleDefinition hole = snap.Hole;
            switch (r.Outcome)
            {
                case ShotOutcome.Holed: title = last.StrokeNumber == 1 ? "HOLE IN ONE!" : $"HOLED  \u00B7  {ToParWord(last.ScoreSoFarAfter - hole.Par).ToUpperInvariant()}"; break;
                case ShotOutcome.Water: title = "WATER  \u00B7  +1"; break;
                case ShotOutcome.OutOfBounds: title = "OUT OF BOUNDS  \u00B7  +1"; break;
                case ShotOutcome.SolverFault: title = "RETRY NEEDED"; break;
                default: title = SurfaceCatalog.Get(r.FinalLie).DisplayName.ToUpperInvariant(); break;
            }

            string distance = last.Intent.ShotType == ShotType.Putt
                ? Units.FormatDistance(r.TotalM)
                : $"{Units.MetersToYards(r.CarryM):0} carry  \u00B7  {Units.MetersToYards(r.RollM):0} roll";
            if (last.HoleStatusAfter == HoleStatus.CAPPED) body = $"{distance}  \u00B7  MAX {last.ScoreSoFarAfter}";
            else if (last.HoleStatusAfter == HoleStatus.HOLED) body = distance;
            else
            {
                int roster = snap.Data.Players.FindIndex(p => p.Id == last.PlayerId);
                HoleProgress hp = snap.Data.ProgressFor(roster, snap.Data.HoleIndex);
                body = $"{distance}  \u00B7  {Units.FormatDistance(Vec3d.HorizontalDistance(hp.Ball.Position, hole.CupM))} to pin";
            }
            if (last.Penalty != null) body += last.Penalty.Type == PenaltyType.Water && !last.Penalty.UsedFallback ? "\nDrop taken" : "\nReplay from previous spot";
        }

        private static string ToParWord(int diff)
        {
            switch (diff)
            {
                case -3: return "albatross";
                case -2: return "eagle";
                case -1: return "birdie";
                case 0: return "par";
                case 1: return "bogey";
                case 2: return "double bogey";
                default: return diff < 0 ? $"{diff}" : $"+{diff}";
            }
        }

        private static string Standings(SessionSnapshot snap)
        {
            var rows = new List<ScorecardRow>(snap.Scorecard);
            rows.Sort((a, b) =>
            {
                int byThru = b.Thru.CompareTo(a.Thru);
                return byThru != 0 ? byThru : a.CompletedTotal.CompareTo(b.CompletedTotal);
            });
            var sb = new StringBuilder();
            int rank = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                bool tied = i > 0 && rows[i].Thru == rows[i - 1].Thru && rows[i].CompletedTotal == rows[i - 1].CompletedTotal;
                if (!tied) rank = i + 1;
                ScorecardRow r = rows[i];
                if (i > 0) sb.Append('\n');
                sb.Append($"{(tied ? "T" : "")}{rank}. {r.Name}  {r.CompletedTotal} ({r.ToParDisplay}){(r.Thru < snap.Data.HoleCount ? $" thru {r.Thru}" : "")}");
            }
            return sb.ToString();
        }

        private void RenderScorecard(HudContext ctx, SessionSnapshot snap)
        {
            _scorecard.SetActive(ctx.ShowScorecard);
            if (!ctx.ShowScorecard || _scoreRevision == snap.Revision) return;
            _scoreRevision = snap.Revision;

            for (int i = _scoreGrid.childCount - 1; i >= 0; i--) Destroy(_scoreGrid.GetChild(i).gameObject);

            List<int> pars = snap.Course.Pars;
            var columns = new List<string> { "PLAYER" };
            for (int h = 0; h < pars.Count; h++) columns.Add(snap.Course.Holes[h].Number.ToString());
            columns.Add("OUT");
            columns.Add("IN");
            columns.Add("TOTAL");
            columns.Add("TO PAR");
            columns.Add("THRU");

            float nameWidth = 260;
            float colWidth = Mathf.Min(120, (1440 - nameWidth) / (columns.Count - 1));
            float rowHeight = 50;

            AddRow(0, rowHeight, nameWidth, colWidth, columns, Palette.Ink, FontStyle.Bold);
            var parRow = new List<string> { "PAR" };
            int parTotal = 0;
            foreach (int p in pars)
            {
                parRow.Add(p.ToString());
                parTotal += p;
            }
            parRow.Add(""); parRow.Add(""); parRow.Add(parTotal.ToString()); parRow.Add(""); parRow.Add("");
            AddRow(1, rowHeight, nameWidth, colWidth, parRow, Palette.MutedGreenGray, FontStyle.Normal);

            for (int r = 0; r < snap.Scorecard.Count; r++)
            {
                ScorecardRow row = snap.Scorecard[r];
                var cells = new List<string> { row.Name };
                foreach (ScorecardCell cell in row.Cells) cells.Add(cell.Display);
                cells.Add(SubtotalText(row.Out));
                cells.Add(SubtotalText(row.In));
                cells.Add(row.Thru > 0 ? row.CompletedTotal.ToString() : "-");
                cells.Add(row.ToParDisplay);
                cells.Add(row.Thru.ToString());
                AddRow(r + 2, rowHeight, nameWidth, colWidth, cells, Palette.Ink, FontStyle.Normal);
            }
            Text legend = HudWidgets.Label(_scoreGrid, "Legend", 16, Palette.MutedGreenGray, TextAnchor.LowerLeft, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 24));
            legend.text = "* in progress   M = MAX (pace cap)";
        }

        private static string SubtotalText(Subtotal s)
        {
            if (s.HoleCount == 0) return "";
            if (s.CompletedHoles == 0) return "-";
            return s.IsPartial ? $"{s.Strokes}p" : s.Strokes.ToString();
        }

        private void AddRow(int index, float height, float nameWidth, float colWidth, List<string> cells, Color color, FontStyle style)
        {
            for (int c = 0; c < cells.Count; c++)
            {
                float x = c == 0 ? 0 : nameWidth + (c - 1) * colWidth;
                float w = c == 0 ? nameWidth : colWidth;
                Text t = HudWidgets.Label(_scoreGrid, $"R{index}C{c}", 24, color, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -(index + 1) * height), new Vector2(x + w, -index * height), style);
                t.text = cells[c];
            }
        }

        private static Vec3d AimForward(SessionSnapshot snap)
        {
            HoleProgress hp = snap.CurrentProgress;
            if (hp?.Ball == null) return Vec3d.Forward;
            Vec3d f = (snap.Data.Setup.AimTargetM - hp.Ball.Position).Horizontal.Normalized;
            return f.LengthSquared > 0.5 ? f : Vec3d.Forward;
        }

        /// <summary>Degrees the wind points to the right of the aim direction.</summary>
        private static float SignedYawDeg(Vec3d aim, Vec3d wind)
        {
            if (wind.Length < 1e-6) return 0;
            double d = System.Math.Atan2(wind.x, wind.z) - System.Math.Atan2(aim.x, aim.z);
            while (d > System.Math.PI) d -= 2 * System.Math.PI;
            while (d < -System.Math.PI) d += 2 * System.Math.PI;
            return (float)(d * 180.0 / System.Math.PI);
        }
    }
}
