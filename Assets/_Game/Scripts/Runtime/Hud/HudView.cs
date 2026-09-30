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
    /// Shot HUD in the reference hierarchy (spec section 29): hole/stroke/distance top-left, roster top-center, wind
    /// top-right, club/lie bottom-left, lanes 1-20 bottom-center, power bottom-right. Built from native components.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        private const int RosterSlots = SessionCoordinator.MaxPlayers;

        private Text _hole, _stroke, _distance, _assist;
        private readonly Image[] _chipBg = new Image[RosterSlots];
        private readonly Image[] _chipBar = new Image[RosterSlots];
        private readonly Text[] _chipName = new Text[RosterSlots];
        private readonly Text[] _chipInfo = new Text[RosterSlots];
        private Text _windValue;
        private RectTransform _windArrow;
        private Text _clubName, _clubRange, _lie;
        private readonly Image[] _laneCells = new Image[LaneMapper.LaneCount];
        private readonly Text[] _laneText = new Text[LaneMapper.LaneCount];
        private Text _laneCaption;
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
        private GameObject _playHud;

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

        private void Build(Transform root)
        {
            Color cream = Palette.Cream.WithAlpha(0.94f);
            Color ink = Palette.Ink;
            Color charcoal = Palette.Ink.WithAlpha(0.86f);

            _playHud = new GameObject("Play HUD", typeof(RectTransform));
            var playRt = (RectTransform)_playHud.transform;
            playRt.SetParent(root, false);
            playRt.anchorMin = Vector2.zero;
            playRt.anchorMax = Vector2.one;
            playRt.offsetMin = playRt.offsetMax = Vector2.zero;
            Transform play = _playHud.transform;

            Image tl = HudWidgets.Panel(play, "Hole Panel", new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -24), new Vector2(360, 170), cream);
            HudWidgets.Border(tl, Palette.PaleBorder);
            _hole = HudWidgets.Label(tl.transform, "Hole", 34, ink, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -58), new Vector2(-12, -10), FontStyle.Bold);
            _stroke = HudWidgets.Label(tl.transform, "Stroke", 24, ink, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -92), new Vector2(-12, -60));
            _distance = HudWidgets.Label(tl.transform, "Distance", 24, ink, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -124), new Vector2(-12, -92));
            _assist = HudWidgets.Label(tl.transform, "Assist", 18, Palette.CyanOnLight, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -158), new Vector2(-12, -128), FontStyle.Bold);

            RectTransform roster = HudWidgets.Rect(play, "Roster", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(RosterSlots * 176, 86));
            for (int i = 0; i < RosterSlots; i++)
            {
                Image chip = HudWidgets.Panel(roster, $"Chip {i + 1}", new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * 176, 0), new Vector2(168, 86), cream);
                _chipBg[i] = chip;
                _chipBar[i] = HudWidgets.Panel(chip.transform, "Identity", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(8, 86), Color.gray);
                _chipName[i] = HudWidgets.Label(chip.transform, "Name", 24, ink, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(18, 38), new Vector2(-6, -8), FontStyle.Bold);
                _chipInfo[i] = HudWidgets.Label(chip.transform, "Info", 18, Palette.MutedGreenGray, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(18, 8), new Vector2(-6, -44));
            }

            Image wind = HudWidgets.Panel(play, "Wind", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -24), new Vector2(210, 120), cream);
            HudWidgets.Border(wind, Palette.PaleBorder);
            HudWidgets.Label(wind.transform, "Wind Label", 18, Palette.MutedGreenGray, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-8, -10), FontStyle.Bold).text = "WIND";
            _windValue = HudWidgets.Label(wind.transform, "Wind Value", 30, ink, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(16, 14), new Vector2(-70, -40), FontStyle.Bold);
            Text arrow = HudWidgets.Label(wind.transform, "Wind Arrow", 56, Palette.CyanOnLight, TextAnchor.MiddleCenter, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-70, -34), new Vector2(-6, 30), FontStyle.Bold);
            arrow.text = "\u2191";
            _windArrow = arrow.rectTransform;

            Image club = HudWidgets.Panel(play, "Club Panel", new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 28), new Vector2(330, 150), charcoal);
            _clubName = HudWidgets.Label(club.transform, "Club", 34, Palette.Cream, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(18, 70), new Vector2(-10, -12), FontStyle.Bold);
            _clubRange = HudWidgets.Label(club.transform, "Range", 20, Palette.Cyan, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(18, 40), new Vector2(-10, -60));
            _lie = HudWidgets.Label(club.transform, "Lie", 22, Palette.Cream, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(18, 12), new Vector2(-10, -96));

            float stripWidth = 1920 * 0.52f;
            RectTransform lanes = HudWidgets.Rect(play, "Lanes", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-40, 64), new Vector2(stripWidth, 64));
            float cell = stripWidth / LaneMapper.LaneCount;
            for (int i = 0; i < LaneMapper.LaneCount; i++)
            {
                Image c = HudWidgets.Panel(lanes, $"Lane {i + 1}", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * cell + 2, 0), new Vector2(cell - 4, 60), charcoal);
                _laneCells[i] = c;
                _laneText[i] = HudWidgets.FullLabel(c.transform, "Number", 24, Palette.Cream, TextAnchor.MiddleCenter, 0, FontStyle.Bold);
                _laneText[i].text = (i + 1).ToString();
            }
            _laneCaption = HudWidgets.Label(play, "Lane Caption", 20, Palette.Cream, TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-stripWidth / 2 - 40, 132), new Vector2(stripWidth / 2 - 40, 170), FontStyle.Bold);
            _laneCaption.gameObject.AddComponent<Shadow>().effectColor = Palette.Ink.WithAlpha(0.8f);

            Image power = HudWidgets.Panel(play, "Power Panel", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 28), new Vector2(300, 190), charcoal);
            HudWidgets.Label(power.transform, "Power Label", 18, Palette.PaleBorder, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(18, 0), new Vector2(-10, -12), FontStyle.Bold).text = "POWER";
            _powerValue = HudWidgets.Label(power.transform, "Power Value", 44, Palette.Cream, TextAnchor.UpperRight, Vector2.zero, Vector2.one, new Vector2(18, 0), new Vector2(-18, -6), FontStyle.Bold);
            Image track = HudWidgets.Panel(power.transform, "Meter Track", new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -70), new Vector2(264, 26), Palette.MutedGreenGray);
            Image fill = HudWidgets.Panel(track.transform, "Meter Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, 26), Palette.Cyan);
            _powerFill = fill.rectTransform;
            _statusBg = HudWidgets.Panel(power.transform, "Status", new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 16), new Vector2(264, 70), Palette.MutedGreenGray);
            _status = HudWidgets.FullLabel(_statusBg.transform, "Status Text", 30, Palette.Cream, TextAnchor.MiddleCenter, 4, FontStyle.Bold);

            _banner = HudWidgets.Panel(root, "Banner", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -124), new Vector2(900, 54), Palette.Warning).gameObject;
            _bannerText = HudWidgets.FullLabel(_banner.transform, "Banner Text", 24, Palette.Ink, TextAnchor.MiddleCenter, 6, FontStyle.Bold);

            Image card = HudWidgets.Panel(root, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(860, 480), cream);
            HudWidgets.Border(card, Palette.PaleBorder, 3);
            _card = card.gameObject;
            _cardTitle = HudWidgets.Label(card.transform, "Title", 54, ink, TextAnchor.UpperCenter, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -110), new Vector2(-30, -24), FontStyle.Bold);
            _cardBody = HudWidgets.Label(card.transform, "Body", 26, ink, TextAnchor.UpperCenter, Vector2.zero, Vector2.one, new Vector2(40, 80), new Vector2(-40, -120));
            _cardFooter = HudWidgets.Label(card.transform, "Footer", 24, Palette.CyanOnLight, TextAnchor.LowerCenter, Vector2.zero, new Vector2(1, 0), new Vector2(30, 20), new Vector2(-30, 70), FontStyle.Bold);

            Image sc = HudWidgets.Panel(root, "Scorecard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500, 520), cream);
            HudWidgets.Border(sc, Palette.PaleBorder, 3);
            _scorecard = sc.gameObject;
            HudWidgets.Label(sc.transform, "Title", 40, ink, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -70), new Vector2(-30, -16), FontStyle.Bold).text = "SCORECARD";
            _scoreGrid = HudWidgets.Rect(sc.transform, "Grid", Vector2.zero, Vector2.one, new Vector2(0, 1), Vector2.zero, Vector2.zero);
            ((RectTransform)_scoreGrid).offsetMin = new Vector2(30, 30);
            ((RectTransform)_scoreGrid).offsetMax = new Vector2(-30, -90);
            _scorecard.SetActive(false);

            _hints = HudWidgets.Label(root, "Hints", 18, Palette.Cream, TextAnchor.LowerCenter, new Vector2(0, 0), new Vector2(1, 0), new Vector2(360, 4), new Vector2(-360, 28));
            _hints.gameObject.AddComponent<Shadow>().effectColor = Palette.Ink.WithAlpha(0.9f);
        }

        public void Render(HudContext ctx)
        {
            SessionSnapshot snap = ctx.Snapshot;
            SessionState state = snap.EffectiveState;
            bool inPlay = state >= SessionState.BRIEFING && state <= SessionState.HOLE_COMPLETE && state != SessionState.TUTORIAL;
            _playHud.SetActive(inPlay);
            _hints.text = ctx.Hints ?? string.Empty;

            string banner = snap.State == SessionState.FAULT ? snap.Data.FaultReason
                : !string.IsNullOrEmpty(snap.Data.FaultReason) ? snap.Data.FaultReason
                : ctx.Notice;
            if (string.IsNullOrEmpty(banner) && inPlay && !snap.CabinetReady) banner = snap.CabinetStatus;
            _banner.SetActive(!string.IsNullOrEmpty(banner));
            _bannerText.text = banner ?? string.Empty;

            if (inPlay) RenderPlay(ctx, snap, state);
            RenderCard(ctx, snap);
            RenderScorecard(ctx, snap);
        }

        private void RenderPlay(HudContext ctx, SessionSnapshot snap, SessionState state)
        {
            HoleDefinition hole = snap.Hole;
            HoleProgress progress = snap.CurrentProgress;
            PlayerState player = snap.CurrentPlayer;
            ShotRecord last = snap.LastShot;
            bool showingResult = state == SessionState.SHOT_RESOLVED || state == SessionState.SHOT_PRESENTING || state == SessionState.SHOT_RESULT;

            _hole.text = $"HOLE {hole.Number}  \u00B7  PAR {hole.Par}  \u00B7  {hole.YardageYd} YD";
            if (progress != null)
            {
                int stroke = showingResult && last != null ? last.StrokeNumber : ScoreEngine.NextStrokeNumber(progress);
                _stroke.text = progress.Penalties > 0 ? $"STROKE {stroke}  (incl. {progress.Penalties} penalty)" : $"STROKE {stroke}";
                double toPin = Vec3d.HorizontalDistance(progress.Ball.Position, hole.CupM);
                double rise = hole.CupM.y - progress.Ball.Position.y;
                string elevation = System.Math.Abs(rise) >= 1.0 ? $"  ({(rise > 0 ? "+" : "")}{rise:0} m)" : string.Empty;
                _distance.text = $"TO PIN  {Units.FormatDistance(toPin)}{elevation}";
            }
            _assist.text = $"{AssistanceProfiles.Get(snap.Data.Settings.Assistance).Label} ASSIST  \u00B7  {(snap.Data.Settings.PaceCapEnabled ? $"MAX PAR+{snap.Data.Settings.PaceCapOverPar}" : "NO CAP")}";

            for (int i = 0; i < RosterSlots; i++)
            {
                bool present = i < snap.Data.Players.Count;
                _chipBg[i].gameObject.SetActive(present);
                if (!present) continue;
                PlayerState p = snap.Data.Players[i];
                HoleProgress hp = snap.ProgressFor(i);
                bool current = player != null && p.Id == player.Id && state != SessionState.HOLE_COMPLETE;
                _chipBar[i].color = Palette.Player(p.ColorIndex);
                _chipBg[i].color = current ? Palette.Player(p.ColorIndex).WithAlpha(0.95f) : Palette.Cream.WithAlpha(0.9f);
                _chipName[i].color = current ? Color.white : Palette.Ink;
                _chipInfo[i].color = current ? Color.white : Palette.MutedGreenGray;
                _chipName[i].text = (current ? "\u25B6 " : "") + p.Name;
                _chipInfo[i].text = ChipInfo(hp, snap.Scorecard.Count > i ? snap.Scorecard[i] : null);
            }

            Vec3d aim = snap.Data.Arm?.AimForward ?? AimForward(snap);
            Vec3d windH = hole.WindMps.Horizontal;
            _windValue.text = windH.Length < 0.1 ? "CALM" : $"{Units.MpsToMph(windH.Length):0} MPH";
            float relative = SignedYawDeg(aim, windH);
            _windArrow.localRotation = Quaternion.Euler(0, 0, -relative);

            string clubId = snap.Data.Arm?.ClubId ?? snap.Data.Setup.ClubId;
            if (showingResult && last != null) clubId = last.Intent.ClubId;
            if (clubId != null && ClubCatalog.TryGet(clubId, out ClubDefinition club))
            {
                _clubName.text = club.DisplayName.ToUpperInvariant();
                _clubRange.text = club.ShotType == ShotType.Putt
                    ? $"PUTT SCALE: {snap.Data.Setup.PuttScale.ToString().ToUpperInvariant()} ({Units.MetersToFeet(ClubCatalog.PuttScaleMaxMeters(snap.Data.Setup.PuttScale)):0} ft max)"
                    : $"{club.RangeLabel}{(club.RangeFromReference ? "" : " (proposed)")}";
            }
            if (progress != null) _lie.text = $"LIE  {SurfaceCatalog.Get(progress.Ball.Lie).DisplayName.ToUpperInvariant()}";

            int detected = 0;
            if (showingResult && last != null) detected = last.Intent.Lane;
            else if (ctx.LastAck != null && ctx.LastAck.Accepted && state == SessionState.SHOT_RESERVED) detected = ctx.LastAck.Lane;
            for (int i = 0; i < LaneMapper.LaneCount; i++)
            {
                int lane = i + 1;
                bool isDetected = lane == detected;
                bool centerBand = lane == 10 || lane == 11;
                _laneCells[i].color = isDetected ? Palette.Cyan : centerBand ? Palette.MutedGreenGray.WithAlpha(0.95f) : Palette.Ink.WithAlpha(0.8f);
                _laneText[i].color = isDetected ? Palette.Ink : Palette.Cream;
            }
            _laneCaption.text = detected > 0
                ? $"DETECTED LANE {detected}"
                : state == SessionState.SHOT_ARMED ? "PUTT INTO A TARGET HOLE 1-20: THE HOLE SETS DIRECTION, ARRIVAL LOCKS POWER"
                : "LANES 1-20: 10 AND 11 ARE STRAIGHT DOWN THE AIM LINE";

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
                    status = "WAITING...";
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
                    status = snap.CabinetReady ? "SET UP" : "RECONNECTING";
                    statusColor = Palette.MutedGreenGray;
                    break;
            }
            if (!snap.CabinetReady && (state == SessionState.SHOT_ARMED || state == SessionState.ARMING))
            {
                status = "RECONNECTING";
                statusColor = Palette.Danger;
            }
            _powerFill.sizeDelta = new Vector2(264f * (float)power, 26);
            _powerValue.text = state == SessionState.SHOT_SETUP ? $"{MeterWaveform.DisplayPercent(power)}% preview" : $"{MeterWaveform.DisplayPercent(power)}%";
            _powerValue.fontSize = state == SessionState.SHOT_SETUP ? 30 : 44;
            _status.text = status;
            _statusBg.color = statusColor;
        }

        private static string ChipInfo(HoleProgress hp, ScorecardRow row)
        {
            string total = row != null && row.Thru > 0 ? $"  \u00B7  {row.ToParDisplay}" : string.Empty;
            if (hp == null) return total;
            switch (hp.Status)
            {
                case HoleStatus.HOLED: return $"\u2713 HOLED {hp.CompletionScore}{total}";
                case HoleStatus.CAPPED: return $"MAX {hp.CompletionScore}{total}";
                case HoleStatus.WITHDRAWN: return "WITHDRAWN";
                default: return $"{hp.StrokesSoFar} so far{total}";
            }
        }

        private void RenderCard(HudContext ctx, SessionSnapshot snap)
        {
            string title = null, body = null, footer = null;
            SessionState state = snap.State;
            HoleDefinition hole = snap.Hole;
            ShotRecord last = snap.LastShot;

            switch (state)
            {
                case SessionState.ATTRACT:
                    title = "PUTTSHACK PRO";
                    body = $"18-hole golf for 1-6 players.\n\n{snap.Course.Name}\n\nPutt into a numbered target hole to choose direction.\nThe moment your ball arrives locks the power meter.";
                    footer = "PRESS ENTER TO START";
                    break;
                case SessionState.LOBBY:
                {
                    title = "PLAYERS";
                    var sb = new StringBuilder();
                    foreach (PlayerState p in snap.Data.Players) sb.Append($"{p.RosterIndex + 1}.  {p.Name}\n");
                    sb.Append($"\nAssistance: {AssistanceProfiles.Get(ctx.LobbyAssistance).Label}");
                    sb.Append($"\nFormat: modified stroke play, lowest total wins.");
                    sb.Append($"\nPenalties: water +1 (drop), out of bounds +1 (replay).");
                    sb.Append(ctx.LobbyPaceCap ? $"\nPace cap: par + {snap.Data.Settings.PaceCapOverPar}, shown as MAX." : "\nPace cap: off.");
                    body = sb.ToString();
                    footer = "UP/DOWN PLAYERS  \u00B7  LEFT/RIGHT ASSIST  \u00B7  C CAP  \u00B7  ENTER CONFIRM";
                    break;
                }
                case SessionState.HOLE_LOAD:
                    title = $"HOLE {hole.Number}";
                    body = "Loading...";
                    break;
                case SessionState.BRIEFING:
                    title = $"HOLE {hole.Number}  \u00B7  {hole.Name.ToUpperInvariant()}";
                    body = $"PAR {hole.Par}  \u00B7  {hole.YardageYd} YARDS\n\n{hole.Briefing.Hazard}\n\n{hole.Briefing.SafePlan}\n\nSuggested club: {ClubCatalog.Get(hole.Briefing.SuggestedClubId ?? "iron7").DisplayName}";
                    footer = "PRESS ENTER WHEN READY";
                    break;
                case SessionState.SHOT_RESULT:
                    if (last?.Result != null) ResultCard(snap, last, out title, out body, out footer);
                    break;
                case SessionState.HANDOFF:
                {
                    PlayerState p = snap.CurrentPlayer;
                    HoleProgress hp = snap.CurrentProgress;
                    title = $"{p.Name.ToUpperInvariant()}, YOU'RE UP";
                    body = $"Stroke {ScoreEngine.NextStrokeNumber(hp)}  \u00B7  {Units.FormatDistance(Vec3d.HorizontalDistance(hp.Ball.Position, hole.CupM))} to the pin\nLie: {SurfaceCatalog.Get(hp.Ball.Lie).DisplayName}";
                    footer = "PRESS ENTER WHEN READY";
                    break;
                }
                case SessionState.HOLE_COMPLETE:
                {
                    title = $"HOLE {hole.Number} COMPLETE";
                    var sb = new StringBuilder();
                    for (int i = 0; i < snap.Data.Players.Count; i++)
                    {
                        PlayerState p = snap.Data.Players[i];
                        HoleProgress hp = snap.ProgressFor(i);
                        sb.Append($"{p.Name}:  {HoleSummary(hp, hole.Par)}\n");
                    }
                    body = sb.ToString();
                    footer = snap.Data.HoleIndex + 1 < snap.Data.HoleCount ? "PRESS ENTER FOR THE NEXT HOLE" : "PRESS ENTER FOR RESULTS";
                    break;
                }
                case SessionState.ROUND_COMPLETE:
                    title = snap.Data.Completion == RoundCompletion.Incomplete ? "ROUND ENDED (INCOMPLETE)" : "ROUND RESULTS";
                    body = Standings(snap);
                    footer = "PRESS ENTER TO RETURN";
                    break;
                case SessionState.PAUSED:
                    title = "PAUSED";
                    body = "Your round is saved.";
                    footer = "ENTER RESUME  \u00B7  E END ROUND";
                    break;
                case SessionState.FAULT:
                    title = "PLEASE WAIT";
                    body = snap.Data.FaultReason;
                    footer = "STAFF: ENTER TO RETRY";
                    break;
            }

            bool show = title != null && !ctx.ShowScorecard;
            _card.SetActive(show);
            if (!show) return;
            _cardTitle.text = title;
            _cardBody.text = body ?? string.Empty;
            _cardFooter.text = footer ?? string.Empty;
        }

        private static void ResultCard(SessionSnapshot snap, ShotRecord last, out string title, out string body, out string footer)
        {
            ShotResult r = last.Result;
            HoleDefinition hole = snap.Hole;
            PlayerState shooter = snap.Data.Players.Find(p => p.Id == last.PlayerId);
            switch (r.Outcome)
            {
                case ShotOutcome.Holed: title = last.StrokeNumber == 1 ? "HOLE IN ONE!" : "IN THE HOLE"; break;
                case ShotOutcome.Water: title = "WATER"; break;
                case ShotOutcome.OutOfBounds: title = "OUT OF BOUNDS"; break;
                case ShotOutcome.SolverFault: title = "MACHINE RETRY NEEDED"; break;
                default: title = SurfaceCatalog.Get(r.FinalLie).DisplayName.ToUpperInvariant(); break;
            }

            var sb = new StringBuilder();
            sb.Append($"{shooter?.Name}  \u00B7  stroke {last.StrokeNumber}  \u00B7  {ClubCatalog.Get(last.Intent.ClubId).DisplayName}\n");
            sb.Append($"Input: lane {last.Intent.Lane}  \u00B7  power {MeterWaveform.DisplayPercent(last.Intent.Power01)}%\n\n");
            if (last.Intent.ShotType == ShotType.Putt)
                sb.Append($"Roll {Units.FormatDistance(r.RollM)}\n");
            else
                sb.Append($"Carry {Units.MetersToYards(r.CarryM):0} yd  \u00B7  Roll {Units.MetersToYards(r.RollM):0} yd  \u00B7  Total {Units.MetersToYards(r.TotalM):0} yd\n");

            if (last.Penalty != null) sb.Append($"\n{last.Penalty.Explanation}\n");
            if (last.HoleStatusAfter == HoleStatus.HOLED) sb.Append($"\nHoled in {last.ScoreSoFarAfter} ({ToParWord(last.ScoreSoFarAfter - hole.Par)}).");
            else if (last.HoleStatusAfter == HoleStatus.CAPPED) sb.Append($"\nPace cap reached: scored MAX {last.ScoreSoFarAfter}.");
            else
            {
                HoleProgress hp = snap.Data.ProgressFor(snap.Data.Players.FindIndex(p => p.Id == last.PlayerId), snap.Data.HoleIndex);
                sb.Append($"\n{Units.FormatDistance(Vec3d.HorizontalDistance(hp.Ball.Position, hole.CupM))} to the pin  \u00B7  score so far {hp.StrokesSoFar}");
            }
            body = sb.ToString();
            footer = "PRESS ENTER TO CONTINUE";
        }

        private static string HoleSummary(HoleProgress hp, int par)
        {
            switch (hp.Status)
            {
                case HoleStatus.HOLED: return $"{hp.CompletionScore}  ({ToParWord(hp.CompletionScore.Value - par)})";
                case HoleStatus.CAPPED: return $"MAX {hp.CompletionScore}  (raw {hp.StrokesSoFar})";
                case HoleStatus.WITHDRAWN: return "withdrawn";
                default: return $"in progress ({hp.StrokesSoFar})";
            }
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
                string complete = r.Thru < snap.Data.HoleCount ? $"  (thru {r.Thru}, incomplete)" : string.Empty;
                sb.Append($"{(tied ? "T" : "")}{rank}.  {r.Name}   {r.CompletedTotal}   {r.ToParDisplay}{complete}\n");
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
            for (int h = 0; h < pars.Count; h++) columns.Add((snap.Course.Holes[h].Number).ToString());
            columns.Add("OUT");
            columns.Add("IN");
            columns.Add("TOTAL");
            columns.Add("TO PAR");
            columns.Add("THRU");

            float nameWidth = 260;
            float colWidth = Mathf.Min(120, (1440 - nameWidth) / (columns.Count - 1));
            float rowHeight = 52;

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
            Text legend = HudWidgets.Label(_scoreGrid, "Legend", 18, Palette.MutedGreenGray, TextAnchor.LowerLeft, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 26));
            legend.text = "* in progress (not in totals)   M = pace cap (MAX)   totals count completed holes only";
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
                Text t = HudWidgets.Label(_scoreGrid, $"R{index}C{c}", 26, color, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
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
            double aimAngle = System.Math.Atan2(aim.x, aim.z);
            double windAngle = System.Math.Atan2(wind.x, wind.z);
            double d = windAngle - aimAngle;
            while (d > System.Math.PI) d -= 2 * System.Math.PI;
            while (d < -System.Math.PI) d += 2 * System.Math.PI;
            return (float)(d * 180.0 / System.Math.PI);
        }
    }
}
