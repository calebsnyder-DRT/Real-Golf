using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Input;
using Puttshack.Protocol;
using Puttshack.Rules;
using Puttshack.Session;
using TMPro;
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
        public bool HomeInProShop;
    }

    [Serializable]
    public sealed class HudSprite
    {
        public string Id;
        public Sprite Sprite;
    }

    [Serializable]
    public sealed class HudMedallion
    {
        public GameObject Root;
        public Image Ring;
        public Image Glow;
        public TMP_Text Initial;
        public Image Check;
    }

    [Serializable]
    public sealed class HudRosterSlot
    {
        public GameObject Root;
        public HudMedallion Medal;
        public Image Plate;
        public Image Underline;
        public TMP_Text Name;
        public TMP_Text Score;
    }

    [Serializable]
    public sealed class HudLane
    {
        public RectTransform Root;
        public Image Key;
        public Image Frame;
        public Image Glow;
        public TMP_Text Number;
    }

    /// <summary>A row of keycaps and labels laid out by a HorizontalLayoutGroup; pooled children are ordered per hint.</summary>
    [Serializable]
    public sealed class HudHintRow
    {
        public RectTransform Root;
        public float KeyHeight = 32f;
        public Image[] Keys = new Image[0];
        public LayoutElement[] KeyLayouts = new LayoutElement[0];
        public TMP_Text[] Legends = new TMP_Text[0];
        public TMP_Text[] Labels = new TMP_Text[0];
        public GameObject[] Gaps = new GameObject[0];
        [NonSerialized] public string Current;
    }

    [Serializable]
    public sealed class HudCard
    {
        public GameObject Root;
        public Image Accent;
        public HudHintRow Footer;
    }

    [Serializable]
    public sealed class HudStat
    {
        public GameObject Root;
        public TMP_Text Label;
        public TMP_Text Value;
    }

    [Serializable]
    public sealed class HudHoleColumn
    {
        public GameObject Root;
        public TMP_Text Name;
        public TMP_Text Score;
        public TMP_Text Word;
        public Image Underline;
    }

    [Serializable]
    public sealed class HudStandingRow
    {
        public GameObject Root;
        public Image Badge;
        public TMP_Text Rank;
        public Image Bar;
        public TMP_Text Name;
        public TMP_Text Total;
        public TMP_Text ToPar;
        public TMP_Text Thru;
    }

    [Serializable]
    public sealed class HudScoreRow
    {
        public GameObject Root;
        public Image Band;
        public Image Bar;
        public TMP_Text[] Cells = new TMP_Text[0];
        public Image[] Marks = new Image[0];
    }

    /// <summary>
    /// Shot HUD (spec section 29): the course centre stays clear. Small corner chips for hole/stroke/distance, roster,
    /// wind, club/lie and power; a 20-lane keycap strip bottom-centre. Short cards sit in the lower third, and only one
    /// overlay (card, scorecard or banner) is visible at a time. The hierarchy and art are baked by
    /// <c>Puttshack > HUD > Bake HUD</c> into HUD.prefab and placed in Main.unity; this component only fills text,
    /// sprites, colours and visibility. Scenes without the baked HUD fall back to <see cref="Create"/>.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        public const int MaxPlayers = SessionCoordinator.MaxPlayers;
        public const int MaxHoles = 18;
        /// <summary>Scorecard columns: player, 18 holes, OUT, IN, TOTAL, TO PAR, THRU.</summary>
        public const int ScoreColumns = 1 + MaxHoles + 5;

        [Header("Art")]
        public HudSprite[] Keycaps = new HudSprite[0];
        public Sprite KeyBlank;
        public HudSprite[] ClubIcons = new HudSprite[0];
        public HudSprite[] LieBadges = new HudSprite[0];
        public Sprite ToggleOn, ToggleOff;
        public Sprite MarkBirdie, MarkEagle, MarkBogey, MarkDouble;
        public bool ReducedMotion;

        [Header("Play")]
        public GameObject PlayTop;
        public GameObject PlayBottom;
        public TMP_Text HoleNumber, HolePar, HoleDistance, HoleStroke, HoleDifficulty;
        public HudRosterSlot[] Roster = new HudRosterSlot[0];
        public TMP_Text WindValue;
        public RectTransform WindArrow;
        public Image ClubIcon;
        public TMP_Text ClubName, ClubRange, LieText;
        public Image LieBadge;
        public HudLane[] Lanes = new HudLane[0];
        public RectTransform LanePointer;
        public Image PowerFill;
        public RectTransform PowerPointer;
        public float PowerPointerMinY, PowerPointerMaxY;
        public TMP_Text PowerValue, StatusText;
        public Image StatusPill;
        public HudHintRow PlayHints;
        public GameObject Banner;
        public TMP_Text BannerText;

        [Header("Cards")]
        public HudCard Attract;
        public TMP_Text AttractBody;

        public HudCard Lobby;
        public HudMedallion[] LobbyPlayers = new HudMedallion[0];
        public TMP_Text[] LobbyNames = new TMP_Text[0];
        public TMP_Text LobbyCount;
        public Image[] LobbyAssistFill = new Image[0];
        public TMP_Text[] LobbyAssistLabel = new TMP_Text[0];
        public Image LobbyCapToggle;
        public TMP_Text LobbyCapText;

        public HudCard Briefing;
        public TMP_Text BriefHole, BriefPar, BriefHazard, BriefPlan, BriefWind, BriefDifficulty, BriefPlayer;
        public RectTransform BriefWindArrow;

        public HudCard Result;
        public HudMedallion ResultMedal;
        public TMP_Text ResultPlayer, ResultTitle, ResultSubtitle, ResultInput;
        public Image ResultLie;
        public HudStat[] ResultStats = new HudStat[0];
        public GameObject[] ResultDividers = new GameObject[0];

        public HudCard Handoff;
        public HudMedallion HandoffMedal;
        public TMP_Text HandoffName, HandoffStroke, HandoffLie;
        public Image HandoffLieBadge;

        public HudCard HoleComplete;
        public TMP_Text HoleTitle;
        public HudHoleColumn[] HoleColumns = new HudHoleColumn[0];

        public HudCard RoundComplete;
        public TMP_Text RoundTitle, RoundSubtitle;
        public HudStandingRow[] RoundRows = new HudStandingRow[0];
        /// <summary>Round card height without standings rows; each shown row adds <see cref="RoundRowHeight"/>.</summary>
        public float RoundBaseHeight = 258f;
        public float RoundRowHeight = 54f;

        public HudCard Paused;
        public TMP_Text PausedBody;

        public HudCard Fault;
        public TMP_Text FaultBody;

        [Header("Scorecard")]
        public GameObject Scorecard;
        public RectTransform ScorePaper;
        public float ScorePaperMargin = 56f;
        public float ScorePaperMinWidth = 1100f;
        /// <summary>Paper height without player rows; each visible row adds <see cref="ScoreRowHeight"/>.</summary>
        public float ScorePaperBaseHeight = 400f;
        public float ScoreRowHeight = 56f;
        /// <summary>Height of the header and par rows above the first player row.</summary>
        public float ScoreHeadHeight = 96f;
        public TMP_Text ScoreHoleOf;
        public RectTransform ScoreGrid;
        public Image ScoreColumnBand;
        public TMP_Text[] ScoreHeader = new TMP_Text[0];
        public TMP_Text[] ScorePar = new TMP_Text[0];
        public HudScoreRow[] ScoreRows = new HudScoreRow[0];
        public HudHintRow ScoreHints;

        private static readonly Color Cream = Palette.Cream;
        private static readonly Color Cyan = Palette.Cyan;
        private static readonly Color Ink = Palette.Ink;
        public static readonly Color Leaf = Palette.Hex(0x6BA52E);
        public static readonly Color Brass = Palette.Hex(0xB8A17B);
        private static readonly Color LaneIdle = Palette.Hex(0x2A3434);
        private static readonly Color LaneCentre = Palette.Hex(0x3D4B4B);
        private static readonly Color PlateIdle = Palette.Hex(0x141A1A).WithAlpha(0.86f);
        private static readonly Color SegmentIdle = Palette.Hex(0x222A2A).WithAlpha(0.95f);
        private static readonly Color UnderPar = Palette.Hex(0x3F7F1F);
        private static readonly Color OverPar = Palette.Hex(0x8A4FC7);

        private HudFallback _fallback;
        private readonly List<Image> _pulse = new List<Image>();
        private long _scoreRevision = -1;
        private bool _scoreShown;

        /// <summary>Runtime stand-in for scenes without the baked HUD (plain boxes and legacy text).</summary>
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
            view._fallback = new HudFallback(go.transform);
            return view;
        }

        public void Render(HudContext ctx)
        {
            if (_fallback != null)
            {
                _fallback.Render(ctx);
                return;
            }

            SessionSnapshot snap = ctx.Snapshot;
            SessionState state = snap.EffectiveState;
            bool inPlay = state >= SessionState.BRIEFING && state <= SessionState.HOLE_COMPLETE && state != SessionState.TUTORIAL;
            _pulse.Clear();

            bool cardShown = RenderCard(ctx, snap);
            bool overlay = cardShown || ctx.ShowScorecard;

            SetActive(PlayTop, inPlay && !ctx.ShowScorecard);
            SetActive(PlayBottom, inPlay && !overlay);
            bool hints = inPlay && !overlay && !string.IsNullOrEmpty(ctx.Hints);
            SetActive(PlayHints.Root.gameObject, hints);
            if (hints) SetHints(PlayHints, ctx.Hints);

            string banner = !string.IsNullOrEmpty(snap.Data.FaultReason) && snap.State != SessionState.FAULT ? snap.Data.FaultReason : ctx.Notice;
            if (string.IsNullOrEmpty(banner) && inPlay && !snap.CabinetReady) banner = snap.CabinetStatus;
            bool showBanner = !overlay && !string.IsNullOrEmpty(banner);
            SetActive(Banner, showBanner);
            if (showBanner) BannerText.text = banner;

            if (inPlay) RenderPlay(ctx, snap, state);
            RenderScorecard(ctx, snap);
        }

        // ---------------------------------------------------------------- Corner chips, lanes, power

        private void RenderPlay(HudContext ctx, SessionSnapshot snap, SessionState state)
        {
            HoleDefinition hole = snap.Hole;
            HoleProgress progress = snap.CurrentProgress;
            PlayerState player = snap.CurrentPlayer;
            ShotRecord last = snap.LastShot;
            bool showingResult = state == SessionState.SHOT_RESOLVED || state == SessionState.SHOT_PRESENTING || state == SessionState.SHOT_RESULT;

            HoleNumber.text = hole.Number.ToString();
            HolePar.text = $"PAR {hole.Par}";
            HoleDifficulty.text = AssistanceProfiles.Get(snap.Data.Settings.Assistance).Label;
            if (progress != null)
            {
                int stroke = showingResult && last != null ? last.StrokeNumber : ScoreEngine.NextStrokeNumber(progress);
                double toPin = Vec3d.HorizontalDistance(progress.Ball.Position, hole.CupM);
                HoleDistance.text = $"{Units.FormatDistance(toPin).ToUpperInvariant()} <size=70%><color=#F2EDDFB3>TO PIN</color></size>";
                HoleStroke.text = $"STROKE {stroke}";
            }

            for (int i = 0; i < Roster.Length; i++)
            {
                HudRosterSlot slot = Roster[i];
                bool present = i < snap.Data.Players.Count;
                SetActive(slot.Root, present);
                if (!present) continue;
                PlayerState p = snap.Data.Players[i];
                HoleProgress hp = snap.ProgressFor(i);
                bool current = player != null && p.Id == player.Id && state != SessionState.HOLE_COMPLETE;
                Color identity = Palette.Player(p.ColorIndex);
                SetMedal(slot.Medal, p.Name, identity, current, hp != null && hp.Status == HoleStatus.HOLED);
                slot.Plate.color = current ? identity : PlateIdle;
                slot.Underline.color = identity;
                slot.Underline.enabled = !current;
                slot.Name.text = p.Name.ToUpperInvariant();
                slot.Name.color = current ? Color.white : Cream;
                slot.Score.text = ChipScore(hp);
                slot.Score.color = current ? Color.white : identity;
            }

            Vec3d aim = snap.Data.Arm?.AimForward ?? AimForward(snap);
            Vec3d windH = hole.WindMps.Horizontal;
            WindValue.text = WindText(windH);
            WindArrow.localRotation = Quaternion.Euler(0, 0, -SignedYawDeg(aim, windH));
            WindArrow.gameObject.SetActive(windH.Length >= 0.1);

            string clubId = snap.Data.Arm?.ClubId ?? snap.Data.Setup.ClubId;
            if (showingResult && last != null) clubId = last.Intent.ClubId;
            if (clubId != null && ClubCatalog.TryGet(clubId, out ClubDefinition club))
            {
                bool putter = club.ShotType == ShotType.Putt;
                ClubName.text = putter ? "PUTTER" : club.DisplayName.ToUpperInvariant();
                PuttScale scale = snap.Data.Setup.PuttScale;
                ClubRange.text = putter
                    ? $"{scale.ToString().ToUpperInvariant()}  \u00b7  {Units.MetersToFeet(ClubCatalog.PuttScaleMaxMeters(scale)):0} FT"
                    : $"{club.RangeMinYd}\u2013{club.RangeMaxYd} YD";
                ClubIcon.sprite = Find(ClubIcons, club.Id);
                ClubIcon.enabled = ClubIcon.sprite != null;
            }
            if (progress != null)
            {
                SurfaceId lie = progress.Ball.Lie;
                LieText.text = SurfaceCatalog.Get(lie).DisplayName.ToUpperInvariant();
                LieText.color = LieColor(lie);
                LieBadge.sprite = Find(LieBadges, LieId(lie) + "_sm");
            }

            int detected = 0;
            if (showingResult && last != null) detected = last.Intent.Lane;
            else if (ctx.LastAck != null && ctx.LastAck.Accepted && state == SessionState.SHOT_RESERVED) detected = ctx.LastAck.Lane;
            for (int i = 0; i < Lanes.Length; i++)
            {
                HudLane lane = Lanes[i];
                int number = i + 1;
                bool isDetected = number == detected;
                bool centre = number == 10 || number == 11;
                lane.Key.color = isDetected ? Cyan : centre ? LaneCentre : LaneIdle;
                lane.Number.color = isDetected ? Ink : Cream.WithAlpha(centre ? 1f : 0.82f);
                lane.Frame.enabled = isDetected || centre;
                lane.Frame.color = isDetected ? Color.white.WithAlpha(0.9f) : Brass.WithAlpha(0.75f);
                Pulse(lane.Glow, isDetected);
            }
            LanePointer.gameObject.SetActive(detected > 0 && detected <= Lanes.Length);
            if (detected > 0 && detected <= Lanes.Length)
                LanePointer.anchoredPosition = new Vector2(Lanes[detected - 1].Root.anchoredPosition.x, LanePointer.anchoredPosition.y);

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
            float p01 = Mathf.Clamp01((float)power);
            PowerFill.fillAmount = p01;
            PowerFill.color = state == SessionState.SHOT_ARMED ? Cyan : Cyan.WithAlpha(0.8f);
            PowerPointer.anchoredPosition = new Vector2(PowerPointer.anchoredPosition.x, Mathf.Lerp(PowerPointerMinY, PowerPointerMaxY, p01));
            PowerValue.text = $"{MeterWaveform.DisplayPercent(power)}<size=60%>%</size>";
            StatusText.text = status;
            StatusPill.color = statusColor;
        }

        private static string ChipScore(HoleProgress hp)
        {
            if (hp == null) return string.Empty;
            switch (hp.Status)
            {
                case HoleStatus.HOLED: return hp.CompletionScore.ToString();
                case HoleStatus.CAPPED: return $"{hp.CompletionScore}M";
                case HoleStatus.WITHDRAWN: return "WD";
                default: return hp.StrokesSoFar.ToString();
            }
        }

        private static string WindText(Vec3d windH) =>
            windH.Length < 0.1 ? "CALM" : $"{Units.MpsToMph(windH.Length):0}<size=55%> MPH</size>";

        // ---------------------------------------------------------------- Cards

        /// <summary>Returns true when a card is visible.</summary>
        private bool RenderCard(HudContext ctx, SessionSnapshot snap)
        {
            HudCard card = null;
            if (!ctx.ShowScorecard)
            {
                switch (snap.State)
                {
                    case SessionState.ATTRACT: card = Attract; RenderAttract(); break;
                    case SessionState.LOBBY: card = Lobby; RenderLobby(ctx, snap); break;
                    case SessionState.BRIEFING: card = Briefing; RenderBriefing(snap); break;
                    case SessionState.SHOT_RESULT:
                        if (snap.LastShot?.Result != null)
                        {
                            card = Result;
                            RenderResult(snap, snap.LastShot);
                        }
                        break;
                    case SessionState.HANDOFF: card = Handoff; RenderHandoff(snap); break;
                    case SessionState.HOLE_COMPLETE: card = HoleComplete; RenderHoleComplete(snap); break;
                    case SessionState.ROUND_COMPLETE: card = RoundComplete; RenderRoundComplete(ctx, snap); break;
                    case SessionState.PAUSED: card = Paused; PausedBody.text = "ROUND SAVED"; break;
                    case SessionState.FAULT:
                        card = Fault;
                        FaultBody.text = string.IsNullOrEmpty(snap.Data.FaultReason) ? "A staff member is on the way." : snap.Data.FaultReason;
                        break;
                }
            }
            foreach (HudCard c in AllCards())
            {
                if (c != null && c.Root != null) SetActive(c.Root, c == card);
            }
            return card != null;
        }

        private IEnumerable<HudCard> AllCards()
        {
            yield return Attract;
            yield return Lobby;
            yield return Briefing;
            yield return Result;
            yield return Handoff;
            yield return HoleComplete;
            yield return RoundComplete;
            yield return Paused;
            yield return Fault;
        }

        private void RenderAttract()
        {
            AttractBody.text = "1\u20136 PLAYERS  \u00b7  18 HOLES";
            SetHints(Attract.Footer, "ENTER START");
        }

        private void RenderLobby(HudContext ctx, SessionSnapshot snap)
        {
            List<PlayerState> players = snap.Data.Players;
            for (int i = 0; i < LobbyPlayers.Length; i++)
            {
                bool present = i < players.Count;
                HudMedallion m = LobbyPlayers[i];
                if (present)
                {
                    Color identity = Palette.Player(players[i].ColorIndex);
                    SetMedal(m, players[i].Name, identity, false, false);
                    m.Ring.color = identity;
                    m.Initial.color = Cream;
                    LobbyNames[i].text = players[i].Name.ToUpperInvariant();
                    LobbyNames[i].color = identity;
                }
                else
                {
                    SetMedal(m, null, Cream.WithAlpha(0.16f), false, false);
                    m.Initial.text = "+";
                    m.Initial.color = Cream.WithAlpha(0.28f);
                    LobbyNames[i].text = "OPEN";
                    LobbyNames[i].color = Cream.WithAlpha(0.3f);
                }
            }
            LobbyCount.text = players.Count == 1 ? "1 PLAYER" : $"{players.Count} PLAYERS";
            for (int i = 0; i < LobbyAssistFill.Length; i++)
            {
                bool on = (int)ctx.LobbyAssistance == i;
                LobbyAssistFill[i].color = on ? Cyan : SegmentIdle;
                LobbyAssistLabel[i].text = AssistanceProfiles.Get((AssistanceProfileId)i).Label;
                LobbyAssistLabel[i].color = on ? Ink : Cream.WithAlpha(0.75f);
            }
            LobbyCapToggle.sprite = ctx.LobbyPaceCap ? ToggleOn : ToggleOff;
            LobbyCapText.text = ctx.LobbyPaceCap ? $"MAX PAR +{snap.Data.Settings.PaceCapOverPar}" : "NO CAP";
            LobbyCapText.color = ctx.LobbyPaceCap ? Cream : Cream.WithAlpha(0.6f);
            SetHints(Lobby.Footer, "P PRO SHOP   ENTER START ROUND");
        }

        private void RenderBriefing(SessionSnapshot snap)
        {
            HoleDefinition hole = snap.Hole;
            BriefHole.text = $"HOLE {hole.Number}";
            BriefPar.text = $"PAR {hole.Par}  \u00b7  {hole.YardageYd} YD";
            BriefHazard.text = hole.Briefing?.Hazard ?? string.Empty;
            BriefPlan.text = hole.Briefing?.SafePlan ?? string.Empty;
            Vec3d windH = hole.WindMps.Horizontal;
            BriefWind.text = WindText(windH);
            BriefWindArrow.localRotation = Quaternion.Euler(0, 0, -SignedYawDeg(AimForward(snap), windH));
            BriefWindArrow.gameObject.SetActive(windH.Length >= 0.1);
            BriefDifficulty.text = AssistanceProfiles.Get(snap.Data.Settings.Assistance).Label;
            PlayerState p = snap.CurrentPlayer;
            BriefPlayer.text = p != null ? $"<color=#{ColorUtility.ToHtmlStringRGB(Palette.Player(p.ColorIndex))}>{p.Name.ToUpperInvariant()}</color>  \u00b7  TEEING OFF" : string.Empty;
            SetHints(Briefing.Footer, "ENTER TEE OFF   ESC PAUSE");
        }

        private void RenderResult(SessionSnapshot snap, ShotRecord last)
        {
            ShotResult r = last.Result;
            HoleDefinition hole = snap.Hole;
            int roster = snap.Data.Players.FindIndex(p => p.Id == last.PlayerId);
            PlayerState shooter = roster >= 0 ? snap.Data.Players[roster] : null;
            Color identity = Palette.Player(shooter?.ColorIndex ?? 0);
            SetMedal(ResultMedal, shooter?.Name, identity, false, last.HoleStatusAfter == HoleStatus.HOLED);
            ResultPlayer.text = $"<color=#{ColorUtility.ToHtmlStringRGB(identity)}>{(shooter?.Name ?? "PLAYER").ToUpperInvariant()}</color>  \u00b7  SHOT {last.StrokeNumber}";
            Result.Accent.color = identity;

            string title, lieId;
            Color titleColor;
            switch (r.Outcome)
            {
                case ShotOutcome.Holed:
                    title = last.StrokeNumber == 1 ? "HOLE IN ONE!" : ToParWord(last.ScoreSoFarAfter - hole.Par).ToUpperInvariant();
                    titleColor = Cyan;
                    lieId = "green";
                    break;
                case ShotOutcome.Water:
                    title = "WATER";
                    titleColor = Palette.Hex(0x4AA3DF);
                    lieId = "water";
                    break;
                case ShotOutcome.OutOfBounds:
                    title = "OUT OF BOUNDS";
                    titleColor = Palette.Hex(0xE0634B);
                    lieId = "ob";
                    break;
                case ShotOutcome.SolverFault:
                    title = "RETRY NEEDED";
                    titleColor = Palette.Warning;
                    lieId = "ob";
                    break;
                default:
                    title = SurfaceCatalog.Get(r.FinalLie).DisplayName.ToUpperInvariant();
                    titleColor = LieColor(r.FinalLie);
                    lieId = LieId(r.FinalLie);
                    break;
            }
            ResultTitle.text = title;
            ResultTitle.color = titleColor;
            ResultLie.sprite = Find(LieBadges, lieId);

            string subtitle;
            if (last.HoleStatusAfter == HoleStatus.HOLED) subtitle = last.ScoreSoFarAfter == 1 ? "1 STROKE" : $"{last.ScoreSoFarAfter} STROKES";
            else if (last.HoleStatusAfter == HoleStatus.CAPPED) subtitle = $"MAX {last.ScoreSoFarAfter}  \u00b7  PACE CAP";
            else
            {
                HoleProgress hp = roster >= 0 ? snap.Data.ProgressFor(roster, snap.Data.HoleIndex) : null;
                subtitle = hp?.Ball != null ? $"{Units.FormatDistance(Vec3d.HorizontalDistance(hp.Ball.Position, hole.CupM)).ToUpperInvariant()} TO PIN" : string.Empty;
            }
            if (last.Penalty != null)
                subtitle += (subtitle.Length > 0 ? "  \u00b7  " : "") + (last.Penalty.Type == PenaltyType.Water && !last.Penalty.UsedFallback ? "+1  DROP TAKEN" : "+1  REPLAY FROM PREVIOUS SPOT");
            ResultSubtitle.text = subtitle;

            if (last.Intent.ShotType == ShotType.Putt)
            {
                SetStat(0, "DISTANCE", Units.FormatDistance(r.TotalM));
                SetStat(1, null, null);
                SetStat(2, null, null);
            }
            else
            {
                int carry = Mathf.RoundToInt((float)Units.MetersToYards(r.CarryM));
                int roll = Mathf.RoundToInt((float)Units.MetersToYards(r.RollM));
                SetStat(0, "CARRY", $"{carry} yd");
                SetStat(1, "ROLL", $"{roll} yd");
                SetStat(2, "TOTAL", $"{carry + roll} yd");
            }
            for (int i = 0; i < ResultDividers.Length; i++) SetActive(ResultDividers[i], ResultStats[i + 1].Root.activeSelf);
            ResultInput.text = $"LANE <color=#00CBE7>{last.Intent.Lane}</color>   \u00b7   POWER <color=#00CBE7>{MeterWaveform.DisplayPercent(last.Intent.Power01)}%</color>";
            SetHints(Result.Footer, "ENTER CONTINUE");
        }

        private void SetStat(int index, string label, string formatted)
        {
            if (index >= ResultStats.Length) return;
            HudStat s = ResultStats[index];
            bool on = label != null;
            SetActive(s.Root, on);
            if (!on) return;
            s.Label.text = label;
            int space = formatted.LastIndexOf(' ');
            string value = space > 0 ? formatted.Substring(0, space) : formatted;
            string unit = space > 0 ? formatted.Substring(space + 1).ToUpperInvariant() : "";
            s.Value.text = unit.Length > 0 ? $"{value}<size=55%> {unit}</size>" : value;
        }

        private void RenderHandoff(SessionSnapshot snap)
        {
            PlayerState p = snap.CurrentPlayer;
            HoleProgress hp = snap.CurrentProgress;
            Color identity = Palette.Player(p?.ColorIndex ?? 0);
            SetMedal(HandoffMedal, p?.Name, identity, true, false);
            Pulse(HandoffMedal.Glow, true);
            HandoffName.text = (p?.Name ?? "PLAYER").ToUpperInvariant();
            HandoffName.color = identity;
            Handoff.Accent.color = identity;
            HandoffStroke.text = hp != null ? $"STROKE {ScoreEngine.NextStrokeNumber(hp)}" : string.Empty;
            if (hp?.Ball != null)
            {
                SurfaceId lie = hp.Ball.Lie;
                HandoffLie.text = $"{Units.FormatDistance(Vec3d.HorizontalDistance(hp.Ball.Position, snap.Hole.CupM)).ToUpperInvariant()} TO PIN  \u00b7  <color=#{ColorUtility.ToHtmlStringRGB(LieColor(lie))}>{SurfaceCatalog.Get(lie).DisplayName.ToUpperInvariant()}</color>";
                HandoffLieBadge.sprite = Find(LieBadges, LieId(lie) + "_sm");
            }
            SetHints(Handoff.Footer, "ENTER READY   ESC PAUSE");
        }

        private void RenderHoleComplete(SessionSnapshot snap)
        {
            HoleDefinition hole = snap.Hole;
            HoleTitle.text = $"HOLE {hole.Number} COMPLETE";
            for (int i = 0; i < HoleColumns.Length; i++)
            {
                HudHoleColumn col = HoleColumns[i];
                bool present = i < snap.Data.Players.Count;
                SetActive(col.Root, present);
                if (!present) continue;
                PlayerState p = snap.Data.Players[i];
                HoleProgress hp = snap.ProgressFor(i);
                Color identity = Palette.Player(p.ColorIndex);
                col.Name.text = p.Name.ToUpperInvariant();
                col.Name.color = identity;
                col.Underline.color = identity;
                string word;
                if (hp == null) word = "";
                else if (hp.Status == HoleStatus.HOLED) word = ToParWord(hp.CompletionScore.GetValueOrDefault() - hole.Par).ToUpperInvariant();
                else if (hp.Status == HoleStatus.CAPPED) word = "MAX";
                else if (hp.Status == HoleStatus.WITHDRAWN) word = "WITHDRAWN";
                else word = "";
                col.Score.text = hp == null ? "-" : hp.Status == HoleStatus.WITHDRAWN ? "WD" : (hp.CompletionScore ?? hp.StrokesSoFar).ToString();
                col.Word.text = word;
                col.Word.color = hp != null && hp.Status == HoleStatus.HOLED && hp.CompletionScore < hole.Par ? Cyan : Cream.WithAlpha(0.7f);
            }
            bool lastHole = snap.Data.HoleIndex >= snap.Data.HoleCount - 1;
            SetHints(HoleComplete.Footer, lastHole ? "ENTER FINAL RESULTS   TAB SCORECARD" : "ENTER NEXT HOLE   TAB SCORECARD");
        }

        private void RenderRoundComplete(HudContext ctx, SessionSnapshot snap)
        {
            bool incomplete = snap.Data.Completion == RoundCompletion.Incomplete;
            RoundTitle.text = incomplete ? "ROUND ENDED" : "FINAL RESULTS";
            var rows = new List<ScorecardRow>(snap.Scorecard);
            rows.Sort((a, b) =>
            {
                int byThru = b.Thru.CompareTo(a.Thru);
                return byThru != 0 ? byThru : a.CompletedTotal.CompareTo(b.CompletedTotal);
            });
            int rank = 0, leaders = 0;
            for (int i = 0; i < RoundRows.Length; i++)
            {
                HudStandingRow row = RoundRows[i];
                bool present = i < rows.Count;
                SetActive(row.Root, present);
                if (!present) continue;
                ScorecardRow r = rows[i];
                bool tied = i > 0 && r.Thru == rows[i - 1].Thru && r.CompletedTotal == rows[i - 1].CompletedTotal;
                if (!tied) rank = i + 1;
                if (rank == 1) leaders++;
                int rosterIndex = snap.Data.Players.FindIndex(p => p.Id == r.PlayerId);
                Color identity = Palette.Player(rosterIndex >= 0 ? snap.Data.Players[rosterIndex].ColorIndex : i);
                row.Rank.text = (tied ? "T" : "") + rank;
                row.Rank.color = rank == 1 ? Ink : Cream;
                row.Badge.color = rank == 1 ? Cyan : Palette.Hex(0x2A3434);
                row.Bar.color = identity;
                row.Name.text = r.Name.ToUpperInvariant();
                row.Total.text = r.Thru > 0 ? r.CompletedTotal.ToString() : "-";
                row.ToPar.text = r.ToParDisplay;
                row.ToPar.color = r.Thru == 0 || r.ToPar == 0 ? Cream : r.ToPar < 0 ? Leaf : Palette.Hex(0xB48CE0);
                row.Thru.text = r.Thru < snap.Data.HoleCount ? $"THRU {r.Thru}" : "F";
            }
            if (incomplete) RoundSubtitle.text = "SCORES SAVED  \u00b7  COMPLETED HOLES ONLY";
            else if (rows.Count == 0) RoundSubtitle.text = string.Empty;
            else if (leaders > 1) RoundSubtitle.text = "SHARED WIN";
            else RoundSubtitle.text = $"<color=#00CBE7>{rows[0].Name.ToUpperInvariant()}</color> WINS";
            var cardRect = (RectTransform)RoundComplete.Root.transform;
            cardRect.sizeDelta = new Vector2(cardRect.sizeDelta.x, RoundBaseHeight + Mathf.Min(rows.Count, RoundRows.Length) * RoundRowHeight);
            SetHints(RoundComplete.Footer, ctx.HomeInProShop ? "ENTER PRO SHOP   TAB SCORECARD" : "ENTER CONTINUE   TAB SCORECARD");
        }

        // ---------------------------------------------------------------- Scorecard

        private void RenderScorecard(HudContext ctx, SessionSnapshot snap)
        {
            SetActive(Scorecard, ctx.ShowScorecard);
            if (!ctx.ShowScorecard)
            {
                _scoreShown = false;
                return;
            }
            if (_scoreShown && _scoreRevision == snap.Revision) return;
            _scoreShown = true;
            _scoreRevision = snap.Revision;

            List<int> pars = snap.Course.Pars;
            int holes = Mathf.Min(pars.Count, MaxHoles);
            bool split = holes > 9;
            ScoreHoleOf.text = $"HOLE <color=#008CA1>{Mathf.Min(snap.Data.HoleIndex + 1, Mathf.Max(1, holes))}</color> OF {holes}";
            SetHints(ScoreHints, "TAB CLOSE");

            // Column geometry: player | holes | (OUT, IN) | TOTAL | TO PAR | THRU
            float nameW = 250f, subW = 84f, totalW = 104f, toParW = 104f, thruW = 84f;
            float fixedW = nameW + (split ? subW * 2 : 0) + totalW + toParW + thruW;
            float holeW = Mathf.Clamp((1720f - ScorePaperMargin * 2 - fixedW) / Mathf.Max(1, holes), 52f, 96f);
            float gridW = fixedW + holeW * holes;
            float minGridW = ScorePaperMinWidth - ScorePaperMargin * 2;
            if (gridW < minGridW)
            {
                float k = minGridW / gridW;
                nameW *= k; subW *= k; totalW *= k; toParW *= k; thruW *= k; holeW *= k;
                gridW = minGridW;
            }
            int rowsShown = Mathf.Min(snap.Scorecard.Count, ScoreRows.Length);
            ScorePaper.sizeDelta = new Vector2(Mathf.Max(ScorePaperMinWidth, gridW + ScorePaperMargin * 2), ScorePaperBaseHeight + rowsShown * ScoreRowHeight);
            ScoreGrid.sizeDelta = new Vector2(gridW, ScoreHeadHeight + rowsShown * ScoreRowHeight);

            var x = new float[ScoreColumns];
            var w = new float[ScoreColumns];
            var on = new bool[ScoreColumns];
            float cursor = 0;
            for (int c = 0; c < ScoreColumns; c++)
            {
                float width;
                if (c == 0) width = nameW;
                else if (c <= MaxHoles) width = c <= holes ? holeW : 0;
                else if (c <= MaxHoles + 2) width = split ? subW : 0;
                else if (c == MaxHoles + 3) width = totalW;
                else if (c == MaxHoles + 4) width = toParW;
                else width = thruW;
                x[c] = cursor;
                w[c] = width;
                on[c] = width > 0;
                cursor += width;
            }

            int parTotal = 0;
            for (int h = 0; h < holes; h++) parTotal += pars[h];
            for (int c = 0; c < ScoreColumns; c++)
            {
                string header, par;
                if (c == 0) { header = "PLAYER"; par = "PAR"; }
                else if (c <= MaxHoles) { header = c <= holes ? snap.Course.Holes[c - 1].Number.ToString() : ""; par = c <= holes ? pars[c - 1].ToString() : ""; }
                else if (c == MaxHoles + 1) { header = "OUT"; par = Sum(pars, 0, Mathf.Min(9, holes)).ToString(); }
                else if (c == MaxHoles + 2) { header = "IN"; par = Sum(pars, 9, holes).ToString(); }
                else if (c == MaxHoles + 3) { header = "TOTAL"; par = parTotal.ToString(); }
                else if (c == MaxHoles + 4) { header = "TO PAR"; par = "\u2014"; }
                else { header = "THRU"; par = ""; }
                PlaceCell(ScoreHeader[c], x[c], w[c], on[c], header);
                PlaceCell(ScorePar[c], x[c], w[c], on[c], par);
            }

            int current = snap.Data.HoleIndex;
            bool band = current >= 0 && current < holes && snap.State != SessionState.ROUND_COMPLETE;
            ScoreColumnBand.gameObject.SetActive(band);
            if (band)
            {
                RectTransform b = ScoreColumnBand.rectTransform;
                b.anchoredPosition = new Vector2(x[current + 1], b.anchoredPosition.y);
                b.sizeDelta = new Vector2(holeW, ScoreHeadHeight + rowsShown * ScoreRowHeight + 6f);
            }

            for (int r = 0; r < ScoreRows.Length; r++)
            {
                HudScoreRow row = ScoreRows[r];
                bool present = r < snap.Scorecard.Count;
                SetActive(row.Root, present);
                if (!present) continue;
                ScorecardRow data = snap.Scorecard[r];
                int rosterIndex = snap.Data.Players.FindIndex(p => p.Id == data.PlayerId);
                row.Bar.color = Palette.Player(rosterIndex >= 0 ? snap.Data.Players[rosterIndex].ColorIndex : r);
                for (int c = 0; c < ScoreColumns; c++)
                {
                    string text;
                    Color color = Ink;
                    if (c == 0) text = data.Name.ToUpperInvariant();
                    else if (c <= MaxHoles)
                    {
                        ScorecardCell cell = c - 1 < data.Cells.Count ? data.Cells[c - 1] : null;
                        text = cell != null && c <= holes ? cell.Display : "";
                        if (cell != null && cell.LiveStrokes.HasValue && !cell.Score.HasValue) color = Palette.CyanOnLight;
                        Image mark = row.Marks[c - 1];
                        Sprite sprite = cell != null && c <= holes ? MarkFor(cell) : null;
                        mark.sprite = sprite;
                        mark.enabled = sprite != null;
                        if (sprite != null) mark.rectTransform.anchoredPosition = new Vector2(x[c] + w[c] / 2f, mark.rectTransform.anchoredPosition.y);
                    }
                    else if (c == MaxHoles + 1) text = SubtotalText(data.Out);
                    else if (c == MaxHoles + 2) text = SubtotalText(data.In);
                    else if (c == MaxHoles + 3) text = data.Thru > 0 ? data.CompletedTotal.ToString() : "\u2014";
                    else if (c == MaxHoles + 4)
                    {
                        text = data.ToParDisplay;
                        color = data.Thru == 0 || data.ToPar == 0 ? Ink : data.ToPar < 0 ? UnderPar : OverPar;
                    }
                    else text = data.Thru.ToString();
                    TMP_Text t = row.Cells[c];
                    PlaceCell(t, x[c], w[c], on[c], text);
                    t.color = color;
                }
            }
        }

        private Sprite MarkFor(ScorecardCell cell)
        {
            if (!cell.Score.HasValue || cell.Status != HoleStatus.HOLED) return null;
            int diff = cell.Score.Value - cell.Par;
            if (diff <= -2) return MarkEagle;
            if (diff == -1) return MarkBirdie;
            if (diff == 1) return MarkBogey;
            if (diff >= 2) return MarkDouble;
            return null;
        }

        private static void PlaceCell(TMP_Text t, float x, float w, bool on, string text)
        {
            t.gameObject.SetActive(on);
            if (!on) return;
            RectTransform rt = t.rectTransform;
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
            t.text = text;
        }

        private static int Sum(List<int> values, int from, int to)
        {
            int s = 0;
            for (int i = from; i < to && i < values.Count; i++) s += values[i];
            return s;
        }

        private static string SubtotalText(Subtotal s)
        {
            if (s == null || s.HoleCount == 0) return "";
            if (s.CompletedHoles == 0) return "\u2014";
            return s.IsPartial ? $"{s.Strokes}p" : s.Strokes.ToString();
        }

        // ---------------------------------------------------------------- Keycap hints

        /// <summary>Lays out a hint string as keycap sprites and labels (see <see cref="HudHints"/>).</summary>
        public void SetHints(HudHintRow row, string hints)
        {
            if (row == null || row.Root == null || row.Current == hints) return;
            row.Current = hints;
            List<HintToken> tokens = HudHints.Parse(hints);
            int k = 0, l = 0, g = 0, order = 0;
            foreach (HintToken token in tokens)
            {
                switch (token.Kind)
                {
                    case HintKind.Key:
                    {
                        if (k >= row.Keys.Length) break;
                        Image key = row.Keys[k];
                        Sprite sprite = Find(Keycaps, token.Value);
                        key.sprite = sprite != null ? sprite : KeyBlank;
                        TMP_Text legend = k < row.Legends.Length ? row.Legends[k] : null;
                        float width = row.KeyHeight;
                        if (sprite != null) width = row.KeyHeight * sprite.rect.width / sprite.rect.height;
                        if (legend != null)
                        {
                            legend.text = sprite == null ? token.Value.ToUpperInvariant() : string.Empty;
                            legend.enabled = sprite == null;
                            if (sprite == null) width = Mathf.Max(row.KeyHeight, legend.GetPreferredValues(legend.text).x + row.KeyHeight * 0.6f);
                        }
                        row.KeyLayouts[k].preferredWidth = width;
                        row.KeyLayouts[k].minWidth = width;
                        key.gameObject.SetActive(true);
                        key.transform.SetSiblingIndex(order++);
                        k++;
                        break;
                    }
                    case HintKind.Text:
                        if (l >= row.Labels.Length) break;
                        row.Labels[l].text = token.Value;
                        row.Labels[l].gameObject.SetActive(true);
                        row.Labels[l].transform.SetSiblingIndex(order++);
                        l++;
                        break;
                    default:
                        if (g >= row.Gaps.Length) break;
                        row.Gaps[g].SetActive(true);
                        row.Gaps[g].transform.SetSiblingIndex(order++);
                        g++;
                        break;
                }
            }
            for (int i = k; i < row.Keys.Length; i++) row.Keys[i].gameObject.SetActive(false);
            for (int i = l; i < row.Labels.Length; i++) row.Labels[i].gameObject.SetActive(false);
            for (int i = g; i < row.Gaps.Length; i++) row.Gaps[i].SetActive(false);
            LayoutRebuilder.MarkLayoutForRebuild(row.Root);
        }

        // ---------------------------------------------------------------- Helpers

        private void SetMedal(HudMedallion m, string name, Color identity, bool glow, bool holed)
        {
            if (m == null || m.Ring == null) return;
            m.Ring.color = identity;
            if (m.Initial != null)
            {
                m.Initial.text = string.IsNullOrEmpty(name) ? "" : name.Substring(0, 1).ToUpperInvariant();
                m.Initial.color = Cream;
            }
            if (m.Glow != null)
            {
                m.Glow.color = identity;
                Pulse(m.Glow, glow);
            }
            if (m.Check != null) m.Check.enabled = holed;
        }

        private void Pulse(Image image, bool on)
        {
            if (image == null) return;
            image.enabled = on;
            if (on && !_pulse.Contains(image)) _pulse.Add(image);
        }

        private void Update()
        {
            if (_fallback != null || _pulse.Count == 0) return;
            float a = ReducedMotion ? 0.85f : 0.62f + 0.38f * (0.5f + 0.5f * Mathf.Sin(UnityEngine.Time.unscaledTime * 3.2f));
            foreach (Image glow in _pulse)
            {
                if (glow != null && glow.enabled) glow.color = glow.color.WithAlpha(a);
            }
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        private static Sprite Find(HudSprite[] table, string id)
        {
            if (table == null || id == null) return null;
            foreach (HudSprite s in table)
            {
                if (s != null && s.Id == id) return s.Sprite;
            }
            return null;
        }

        public static string LieId(SurfaceId lie)
        {
            switch (lie)
            {
                case SurfaceId.Tee: return "tee";
                case SurfaceId.Fairway: return "fairway";
                case SurfaceId.LightRough:
                case SurfaceId.DeepRough: return "rough";
                case SurfaceId.Bunker: return "bunker";
                case SurfaceId.Green: return "green";
                case SurfaceId.Water: return "water";
                default: return "path";
            }
        }

        private static Color LieColor(SurfaceId lie)
        {
            switch (lie)
            {
                case SurfaceId.Tee:
                case SurfaceId.Fairway: return Palette.Hex(0x8CC63F);
                case SurfaceId.Green: return Palette.Hex(0x9BE15D);
                case SurfaceId.LightRough:
                case SurfaceId.DeepRough: return Palette.Hex(0xB5C46A);
                case SurfaceId.Bunker: return Palette.Hex(0xE6CC8F);
                case SurfaceId.Water: return Palette.Hex(0x4AA3DF);
                default: return Cream;
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
            double d = Math.Atan2(wind.x, wind.z) - Math.Atan2(aim.x, aim.z);
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            return (float)(d * 180.0 / Math.PI);
        }
    }
}
