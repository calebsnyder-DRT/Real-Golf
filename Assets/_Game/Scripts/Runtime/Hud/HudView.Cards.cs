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
    /// <summary>Card dispatch plus the pre-round and shot cards: attract, round setup, briefing, result, celebration, handoff.</summary>
    public sealed partial class HudView
    {
        [Header("Attract & setup")]
        public HudCard Attract;
        public HudCard Lobby;
        public TMP_Text LobbyCount;
        public HudLobbySlot[] LobbySlots = new HudLobbySlot[0];
        public Image[] LobbyAssistFill = new Image[0];
        public TMP_Text[] LobbyAssistLabel = new TMP_Text[0];
        public Image LobbyCapTrack;
        public RectTransform LobbyCapKnob;
        public float LobbyCapKnobOff = 6f, LobbyCapKnobOn = 50f;
        public TMP_Text LobbyCapText;

        [Header("Briefing")]
        public HudCard Briefing;
        public TMP_Text BriefHoleNumber, BriefPar, BriefHazard, BriefPlan, BriefWind;
        public RectTransform BriefWindArrow;
        public HudBadge BriefPlayer;

        [Header("Shot result")]
        public HudCard Result;
        public HudBadge[] ResultRoster = new HudBadge[0];
        public RectTransform ResultCard;
        public float ResultWidth = 1320f, ResultPenaltyWidth = 1120f;
        public HudBadge ResultTab;
        public Image ResultPanel;
        public TMP_Text ResultTitle, ResultSubtitle, ResultInput;
        public HudStat[] ResultStats = new HudStat[0];
        public HudChip ResultPenalty;

        [Header("Celebration")]
        public HudCard Celebrate;
        public GameObject CelebratePinkRings, CelebrateBlueRings, CelebrateDots;
        public HudBadge CelebratePlayer;
        /// <summary>"HOLE IN one": shown instead of <see cref="CelebrateWord"/> for an ace.</summary>
        public GameObject CelebrateAce;
        public TMP_Text CelebrateWord, CelebrateDetail;

        [Header("Handoff")]
        public HudCard Handoff;
        public Image HandoffHalo;
        public HudDisc HandoffDisc;
        public TMP_Text HandoffName;
        public HudChip[] HandoffChips = new HudChip[0];

        private const string Bullet = "  •  ";

        /// <summary>Returns true when a card is visible.</summary>
        private bool RenderCard(HudContext ctx, SessionSnapshot snap)
        {
            HudCard card = null;
            if (!ctx.ShowScorecard)
            {
                switch (snap.State)
                {
                    case SessionState.ATTRACT: card = Attract; SetHints(Attract.Footer, "ENTER START"); break;
                    case SessionState.LOBBY: card = Lobby; RenderLobby(ctx, snap); break;
                    case SessionState.BRIEFING:
                        if (ctx.HoleIntro) break;
                        card = Briefing;
                        RenderBriefing(snap);
                        break;
                    case SessionState.SHOT_RESULT:
                        ShotRecord last = snap.LastShot;
                        if (last?.Result == null) break;
                        if (IsCelebration(snap, last, out bool top))
                        {
                            card = Celebrate;
                            RenderCelebrate(snap, last, top);
                        }
                        else
                        {
                            card = Result;
                            RenderResult(snap, last);
                        }
                        break;
                    case SessionState.HANDOFF: card = Handoff; RenderHandoff(snap); break;
                    case SessionState.HOLE_COMPLETE: card = HoleComplete; RenderHoleComplete(snap); break;
                    case SessionState.ROUND_COMPLETE:
                        if (ctx.RoundFinale) break;
                        card = RoundComplete;
                        RenderRoundComplete(ctx, snap);
                        break;
                    case SessionState.PAUSED: card = Paused; RenderPaused(snap); break;
                    case SessionState.FAULT: card = Fault; RenderFault(snap); break;
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
            yield return Celebrate;
            yield return Handoff;
            yield return HoleComplete;
            yield return RoundComplete;
            yield return Paused;
            yield return Fault;
        }

        // ---------------------------------------------------------------- Round setup

        private void RenderLobby(HudContext ctx, SessionSnapshot snap)
        {
            List<PlayerState> players = snap.Data.Players;
            for (int i = 0; i < LobbySlots.Length; i++)
            {
                HudLobbySlot slot = LobbySlots[i];
                bool present = i < players.Count;
                slot.Fill.enabled = present;
                slot.Outline.enabled = !present;
                if (present)
                {
                    SetDisc(slot.Disc, players[i].Name, Identity(players[i]), Color.clear);
                    slot.Name.text = players[i].Name.ToUpperInvariant();
                    slot.Name.color = White;
                }
                else
                {
                    SetDisc(slot.Disc, null, Color.clear, White.WithAlpha(0.22f));
                    slot.Disc.Initial.text = "+";
                    slot.Disc.Initial.color = White.WithAlpha(0.5f);
                    slot.Name.text = "OPEN";
                    slot.Name.color = White.WithAlpha(0.5f);
                }
            }
            LobbyCount.text = $"PLAYERS  ·  {players.Count} OF {MaxPlayers}";
            for (int i = 0; i < LobbyAssistFill.Length; i++)
            {
                bool on = (int)ctx.LobbyAssistance == i;
                LobbyAssistFill[i].enabled = on;
                LobbyAssistLabel[i].text = AssistanceProfiles.Get((AssistanceProfileId)i).Label.ToUpperInvariant();
                LobbyAssistLabel[i].color = on ? Midnight : White;
            }
            LobbyCapTrack.color = ctx.LobbyPaceCap ? HudBrand.Aqua : White.WithAlpha(0.18f);
            LobbyCapKnob.anchoredPosition = new Vector2(ctx.LobbyPaceCap ? LobbyCapKnobOn : LobbyCapKnobOff, LobbyCapKnob.anchoredPosition.y);
            LobbyCapText.text = ctx.LobbyPaceCap ? $"MAX PAR +{snap.Data.Settings.PaceCapOverPar}" : "NO CAP";
            LobbyCapText.color = ctx.LobbyPaceCap ? White : HudBrand.Muted;
            SetHints(Lobby.Footer, "ENTER START ROUND");
            SetHints(Lobby.Secondary, "S PLAYERS   P PRO SHOP");
        }

        // ---------------------------------------------------------------- Briefing

        private void RenderBriefing(SessionSnapshot snap)
        {
            HoleDefinition hole = snap.Hole;
            BriefHoleNumber.text = hole.Number.ToString();
            BriefPar.text = $"PAR {hole.Par}  ·  {hole.YardageYd} YD";
            BriefHazard.text = hole.Briefing?.Hazard ?? string.Empty;
            BriefPlan.text = hole.Briefing?.SafePlan ?? string.Empty;
            Vec3d windH = hole.WindMps.Horizontal;
            BriefWind.text = WindText(windH);
            BriefWindArrow.localRotation = Quaternion.Euler(0, 0, -SignedYawDeg(AimForward(snap), windH));
            BriefWindArrow.gameObject.SetActive(windH.Length >= 0.1);
            PlayerState p = snap.CurrentPlayer;
            SetBadge(BriefPlayer, p?.Name, Identity(p), true, null);
            if (BriefPlayer.Name != null) BriefPlayer.Name.text = $"{(p?.Name ?? "PLAYER").ToUpperInvariant()} TEES OFF";
            SetHints(Briefing.Footer, "ESC PAUSE   ENTER TEE OFF");
        }

        // ---------------------------------------------------------------- Shot result

        private void RenderResult(SessionSnapshot snap, ShotRecord last)
        {
            ShotResult r = last.Result;
            HoleDefinition hole = snap.Hole;
            int roster = snap.Data.Players.FindIndex(p => p.Id == last.PlayerId);
            PlayerState shooter = roster >= 0 ? snap.Data.Players[roster] : null;
            Color identity = Identity(shooter);

            for (int i = 0; i < ResultRoster.Length; i++)
            {
                bool present = i < snap.Data.Players.Count;
                SetActive(ResultRoster[i].Root, present);
                if (!present) continue;
                PlayerState p = snap.Data.Players[i];
                SetBadge(ResultRoster[i], p.Name, Identity(p), false, ChipScore(snap.ProgressFor(i)));
            }
            SetBadge(ResultTab, shooter?.Name, identity, true, $"SHOT {last.StrokeNumber}");

            bool penalty = last.Penalty != null;
            string title;
            Color? panel = null;
            switch (r.Outcome)
            {
                case ShotOutcome.Holed:
                    title = ToParWord(last.ScoreSoFarAfter - hole.Par).ToUpperInvariant();
                    break;
                case ShotOutcome.Water:
                    title = "WATER";
                    panel = HudBrand.Navy;
                    break;
                case ShotOutcome.OutOfBounds:
                    title = "OUT OF BOUNDS";
                    panel = HudBrand.Plum;
                    break;
                case ShotOutcome.SolverFault:
                    title = "RETRY NEEDED";
                    panel = HudBrand.Navy;
                    break;
                default:
                    title = SurfaceCatalog.Get(r.FinalLie).DisplayName.ToUpperInvariant();
                    break;
            }
            ResultPanel.color = panel ?? (penalty ? HudBrand.Navy : Midnight.WithAlpha(0.92f));
            ResultTitle.text = title;
            ResultCard.sizeDelta = new Vector2(penalty ? ResultPenaltyWidth : ResultWidth, ResultCard.sizeDelta.y);

            string subtitle;
            if (last.HoleStatusAfter == HoleStatus.HOLED) subtitle = last.ScoreSoFarAfter == 1 ? "1 STROKE" : $"{last.ScoreSoFarAfter} STROKES";
            else if (last.HoleStatusAfter == HoleStatus.CAPPED) subtitle = $"MAX {last.ScoreSoFarAfter}  ·  PACE CAP";
            else
            {
                HoleProgress hp = roster >= 0 ? snap.Data.ProgressFor(roster, snap.Data.HoleIndex) : null;
                subtitle = hp?.Ball != null ? $"{Units.FormatDistance(Vec3d.HorizontalDistance(hp.Ball.Position, hole.CupM)).ToUpperInvariant()} TO PIN" : string.Empty;
            }
            ResultSubtitle.text = penalty ? string.Empty : subtitle;
            SetChip(ResultPenalty, penalty
                ? (last.Penalty.Type == PenaltyType.Water && !last.Penalty.UsedFallback ? "+1  ·  DROP TAKEN" : "+1  ·  REPLAY FROM LAST SPOT")
                : null);

            int carry = Mathf.RoundToInt((float)Units.MetersToYards(r.CarryM));
            int roll = Mathf.RoundToInt((float)Units.MetersToYards(r.RollM));
            bool putt = last.Intent.ShotType == ShotType.Putt;
            SetStat(0, penalty || putt ? null : "CARRY", $"{carry} yd", null);
            SetStat(1, penalty || putt ? null : "ROLL", $"{roll} yd", null);
            SetStat(2, penalty ? null : putt ? "DISTANCE" : "TOTAL", putt ? Units.FormatDistance(r.TotalM) : $"{carry + roll} yd", identity);

            string power = $"POWER {MeterWaveform.DisplayPercent(last.Intent.Power01)}%";
            string tail = penalty ? $"NEXT: STROKE {last.ScoreSoFarAfter + 1}" : ClubLabel(last.Intent.ClubId);
            ResultInput.text = $"LANE {last.Intent.Lane}<space=28>{power}<space=28>{tail}";
            SetHints(Result.Footer, "ENTER CONTINUE");
        }

        private void SetStat(int index, string label, string formatted, Color? hero)
        {
            if (index >= ResultStats.Length) return;
            HudStat s = ResultStats[index];
            bool on = label != null;
            SetActive(s.Root, on);
            if (!on) return;
            s.Label.text = label;
            s.Value.text = WithUnit(formatted, 40);
            if (hero == null) return;
            Color text = HudBrand.TextOn(hero.Value);
            s.Fill.color = hero.Value;
            s.Label.color = text;
            s.Value.color = text;
        }

        private static string ChipScore(HoleProgress hp)
        {
            if (hp == null) return "0";
            switch (hp.Status)
            {
                case HoleStatus.HOLED: return hp.CompletionScore.ToString();
                case HoleStatus.CAPPED: return $"{hp.CompletionScore}M";
                case HoleStatus.WITHDRAWN: return "WD";
                default: return hp.StrokesSoFar.ToString();
            }
        }

        // ---------------------------------------------------------------- Celebration

        /// <summary>Under-par holes get the full-screen takeover; aces and eagles or better get the top tier.</summary>
        private static bool IsCelebration(SessionSnapshot snap, ShotRecord last, out bool topTier)
        {
            topTier = false;
            if (last.Result.Outcome != ShotOutcome.Holed || last.HoleStatusAfter != HoleStatus.HOLED) return false;
            int diff = last.ScoreSoFarAfter - snap.Hole.Par;
            bool ace = last.ScoreSoFarAfter == 1;
            topTier = ace || diff <= -2;
            return ace || diff <= -1;
        }

        private void RenderCelebrate(SessionSnapshot snap, ShotRecord last, bool topTier)
        {
            HoleDefinition hole = snap.Hole;
            int roster = snap.Data.Players.FindIndex(p => p.Id == last.PlayerId);
            PlayerState shooter = roster >= 0 ? snap.Data.Players[roster] : null;
            SetBadge(CelebratePlayer, shooter?.Name, Identity(shooter), true, null);
            SetActive(CelebratePinkRings, !topTier);
            SetActive(CelebrateBlueRings, topTier);
            SetActive(CelebrateDots, topTier);

            bool ace = last.ScoreSoFarAfter == 1;
            int diff = last.ScoreSoFarAfter - hole.Par;
            string dot = $"<color={HudBrand.Hex(topTier ? HudBrand.Sun : HudBrand.Pink)}>{Bullet}</color>";
            SetActive(CelebrateAce, ace);
            SetActive(CelebrateWord.gameObject, !ace);
            if (ace) CelebrateDetail.text = $"HOLE {hole.Number}{dot}{hole.YardageYd} YD{dot}1 STROKE";
            else
            {
                CelebrateWord.text = ToParWord(diff);
                CelebrateDetail.text = $"HOLE {hole.Number}{dot}{last.ScoreSoFarAfter} STROKES{dot}{-diff} UNDER PAR";
            }
            SetHints(Celebrate.Footer, "ENTER CONTINUE");
        }

        // ---------------------------------------------------------------- Handoff

        private void RenderHandoff(SessionSnapshot snap)
        {
            PlayerState p = snap.CurrentPlayer;
            HoleProgress hp = snap.CurrentProgress;
            Color identity = Identity(p);
            HandoffHalo.color = identity;
            SetDisc(HandoffDisc, p?.Name, identity, Color.clear);
            HandoffName.text = (p?.Name ?? "PLAYER").ToUpperInvariant();
            string stroke = hp != null ? $"STROKE {ScoreEngine.NextStrokeNumber(hp)}" : null;
            string distance = null, lie = null;
            if (hp?.Ball != null)
            {
                distance = $"{Units.FormatDistance(Vec3d.HorizontalDistance(hp.Ball.Position, snap.Hole.CupM)).ToUpperInvariant()} TO PIN";
                lie = SurfaceCatalog.Get(hp.Ball.Lie).DisplayName.ToUpperInvariant();
            }
            if (HandoffChips.Length > 0) SetChip(HandoffChips[0], stroke);
            if (HandoffChips.Length > 1) SetChip(HandoffChips[1], distance);
            if (HandoffChips.Length > 2) SetChip(HandoffChips[2], lie);
            SetHints(Handoff.Footer, "ESC PAUSE   ENTER READY");
        }
    }
}
