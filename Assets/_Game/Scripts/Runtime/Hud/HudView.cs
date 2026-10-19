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
    /// <summary>
    /// Brand bay-screen HUD (Claude Design "Puttshack Pro HUD", round 1). In the shot the whole HUD is one Midnight rail
    /// along the bottom so the sky and fairway stay clear; everything else is a full-screen overlay card, one at a time.
    /// The hierarchy and art are baked by <c>Puttshack > HUD > Bake HUD</c> into HUD.prefab and placed in Main.unity; this
    /// component only fills text, colours and visibility. Scenes without the baked HUD fall back to <see cref="Create"/>.
    /// </summary>
    public sealed partial class HudView : MonoBehaviour
    {
        public const int MaxPlayers = SessionCoordinator.MaxPlayers;
        public const int MaxHoles = 18;
        /// <summary>Scorecard columns: player, 18 holes, OUT, IN, TOTAL, TO PAR, THRU.</summary>
        public const int ScoreColumns = 1 + MaxHoles + 5;

        [Header("Art")]
        public Sprite MarkBirdie, MarkEagle, MarkBogey, MarkDouble;
        public bool ReducedMotion;

        [Header("Rail")]
        public GameObject Rail;
        public HudBadge RailPlayer;
        public TMP_Text RailHole, RailDistance, RailPar, RailClub, WindValue, PowerValue;
        public RectTransform WindArrow;
        public RectTransform PowerFill;
        public Image ReadyDot;
        public HudMotion ReadyPulse;
        public HudHintRow PlayHints;
        public GameObject Banner;
        public TMP_Text BannerText;

        private static readonly Color White = HudBrand.White;
        private static readonly Color Midnight = HudBrand.Midnight;

        private HudFallback _fallback;

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
            HudMotion.Reduced = ReducedMotion;

            SessionSnapshot snap = ctx.Snapshot;
            SessionState state = snap.EffectiveState;
            bool inPlay = state >= SessionState.BRIEFING && state <= SessionState.HOLE_COMPLETE && state != SessionState.TUTORIAL;

            bool cardShown = RenderCard(ctx, snap);
            bool overlay = cardShown || ctx.ShowScorecard;

            bool rail = inPlay && !overlay;
            SetActive(Rail, rail);
            if (rail) RenderRail(ctx, snap, state);
            bool hints = rail && !string.IsNullOrEmpty(ctx.Hints);
            SetActive(PlayHints.Root.gameObject, hints);
            if (hints) SetHints(PlayHints, ctx.Hints);

            string banner = !string.IsNullOrEmpty(snap.Data.FaultReason) && snap.State != SessionState.FAULT ? snap.Data.FaultReason : ctx.Notice;
            if (string.IsNullOrEmpty(banner) && inPlay && !snap.CabinetReady) banner = snap.CabinetStatus;
            bool showBanner = !overlay && !string.IsNullOrEmpty(banner);
            SetActive(Banner, showBanner);
            if (showBanner) BannerText.text = banner;

            RenderScorecard(ctx, snap);
        }

        // ---------------------------------------------------------------- Rail

        private void RenderRail(HudContext ctx, SessionSnapshot snap, SessionState state)
        {
            HoleDefinition hole = snap.Hole;
            HoleProgress progress = snap.CurrentProgress;
            PlayerState player = snap.CurrentPlayer;
            ShotRecord last = snap.LastShot;
            bool showingResult = state == SessionState.SHOT_RESOLVED || state == SessionState.SHOT_PRESENTING || state == SessionState.SHOT_RESULT;

            string stroke = string.Empty;
            if (progress != null)
            {
                int n = showingResult && last != null ? last.StrokeNumber : ScoreEngine.NextStrokeNumber(progress);
                stroke = $"STROKE {n}";
                RailDistance.text = WithUnit(Units.FormatDistance(Vec3d.HorizontalDistance(progress.Ball.Position, hole.CupM)), 60);
            }
            SetBadge(RailPlayer, player?.Name, Identity(player), true, stroke);
            RailHole.text = $"HOLE {hole.Number}";
            RailPar.text = $"PAR {hole.Par}";

            string clubId = snap.Data.Arm?.ClubId ?? snap.Data.Setup.ClubId;
            if (showingResult && last != null) clubId = last.Intent.ClubId;
            RailClub.text = ClubLabel(clubId);

            Vec3d aim = snap.Data.Arm?.AimForward ?? AimForward(snap);
            Vec3d windH = hole.WindMps.Horizontal;
            WindValue.text = WindText(windH);
            WindArrow.localRotation = Quaternion.Euler(0, 0, -SignedYawDeg(aim, windH));
            WindArrow.gameObject.SetActive(windH.Length >= 0.1);

            double power;
            Color dot;
            switch (state)
            {
                case SessionState.SHOT_ARMED:
                    power = snap.MeterEstimate(ctx.NowMs);
                    dot = HudBrand.Aqua;
                    break;
                case SessionState.ARMING:
                    power = 0;
                    dot = White.WithAlpha(0.4f);
                    break;
                case SessionState.SHOT_RESERVED:
                case SessionState.SHOT_RESOLVED:
                case SessionState.SHOT_PRESENTING:
                case SessionState.SHOT_RESULT:
                    power = last?.Intent.Power01 ?? ctx.LastAck?.Power01 ?? 0;
                    dot = White;
                    break;
                case SessionState.HANDOFF:
                    power = 0;
                    dot = HudBrand.Sun;
                    break;
                default:
                    power = snap.Data.Settings.PreviewPower;
                    dot = White.WithAlpha(0.4f);
                    break;
            }
            bool offline = !snap.CabinetReady && (state == SessionState.SHOT_ARMED || state == SessionState.ARMING || state == SessionState.SHOT_SETUP);
            if (offline) dot = HudBrand.Danger;
            float p01 = Mathf.Clamp01((float)power);
            PowerFill.anchorMax = new Vector2(p01, 1f);
            PowerValue.text = $"{MeterWaveform.DisplayPercent(power)}<size=60%>%</size>";
            ReadyDot.color = dot;
            bool pulse = state == SessionState.SHOT_ARMED && !offline;
            if (ReadyPulse != null && ReadyPulse.enabled != pulse) ReadyPulse.enabled = pulse;
        }

        private static string ClubLabel(string clubId)
        {
            if (clubId == null || !ClubCatalog.TryGet(clubId, out ClubDefinition club)) return string.Empty;
            return club.ShotType == ShotType.Putt ? "PUTTER" : club.DisplayName.ToUpperInvariant();
        }

        private static string WindText(Vec3d windH) =>
            windH.Length < 0.1 ? "CALM" : $"{Units.MpsToMph(windH.Length):0}<size=60%> MPH</size>";

        /// <summary>"165 yd" → "165<size=60%> YD</size>": the number at full size, the unit small.</summary>
        private static string WithUnit(string formatted, int unitPercent)
        {
            int space = formatted.LastIndexOf(' ');
            if (space <= 0) return formatted;
            return $"{formatted.Substring(0, space)}<size={unitPercent}%> {formatted.Substring(space + 1).ToUpperInvariant()}</size>";
        }

        // ---------------------------------------------------------------- Keycap hints

        /// <summary>Lays out a hint string as keycaps and labels (see <see cref="HudHints"/>).</summary>
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
                        if (k >= row.Keys.Length) break;
                        LayoutKey(row, k, token.Value);
                        row.Keys[k].transform.SetSiblingIndex(order++);
                        k++;
                        break;
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

        private static void LayoutKey(HudHintRow row, int index, string id)
        {
            TMP_Text legend = row.Legends[index];
            legend.text = KeyLegend(id);
            float width = Mathf.Max(row.KeyHeight, legend.GetPreferredValues(legend.text).x + row.KeyHeight * 0.75f);
            row.KeyLayouts[index].preferredWidth = width;
            row.KeyLayouts[index].minWidth = width;
            row.Keys[index].gameObject.SetActive(true);
        }

        private static string KeyLegend(string id)
        {
            switch (id)
            {
                case "left": return "←";
                case "right": return "→";
                case "up": return "↑";
                case "down": return "↓";
                default: return id.ToUpperInvariant();
            }
        }

        // ---------------------------------------------------------------- Helpers

        private static Color Identity(PlayerState p) => HudBrand.Player(p?.ColorIndex ?? 0);

        /// <summary>
        /// Fills a player pill. <paramref name="onIdentity"/> puts the pill in the player's colour with a Midnight disc (the
        /// active player); otherwise the pill keeps its Midnight fill and the disc carries the colour.
        /// </summary>
        private static void SetBadge(HudBadge b, string name, Color identity, bool onIdentity, string extra)
        {
            if (b == null || b.Root == null) return;
            string upper = string.IsNullOrEmpty(name) ? "PLAYER" : name.ToUpperInvariant();
            Color text = onIdentity ? HudBrand.TextOn(identity) : White;
            if (onIdentity && b.Fill != null) b.Fill.color = identity;
            if (b.Disc != null) b.Disc.color = onIdentity ? Midnight : identity;
            if (b.Initial != null)
            {
                b.Initial.text = upper.Substring(0, 1);
                b.Initial.color = onIdentity ? White : HudBrand.TextOn(identity);
            }
            if (b.Name != null)
            {
                b.Name.text = upper;
                b.Name.color = text;
            }
            if (b.Extra != null)
            {
                b.Extra.text = extra ?? string.Empty;
                b.Extra.color = text;
                SetActive(b.Extra.gameObject, !string.IsNullOrEmpty(extra));
            }
        }

        private static void SetDisc(HudDisc d, string name, Color fill, Color ring)
        {
            if (d == null || d.Root == null) return;
            d.Fill.color = fill;
            if (d.Ring != null)
            {
                d.Ring.color = ring;
                d.Ring.enabled = ring.a > 0f;
            }
            d.Initial.text = string.IsNullOrEmpty(name) ? string.Empty : name.Substring(0, 1).ToUpperInvariant();
            d.Initial.color = HudBrand.TextOn(fill);
        }

        private static void SetChip(HudChip c, string text)
        {
            if (c == null || c.Root == null) return;
            bool on = !string.IsNullOrEmpty(text);
            SetActive(c.Root, on);
            if (on) c.Text.text = text;
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        private static string ToParWord(int diff)
        {
            switch (diff)
            {
                case -4: return "condor";
                case -3: return "albatross";
                case -2: return "eagle";
                case -1: return "birdie";
                case 0: return "par";
                case 1: return "bogey";
                case 2: return "double bogey";
                case 3: return "triple bogey";
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
