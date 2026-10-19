using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Rules;
using Puttshack.Runtime.Course;
using Puttshack.Runtime.Hud;
using Puttshack.Runtime.Shop;
using Puttshack.Runtime.Views;
using Puttshack.Session;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Puttshack.Runtime
{
    /// <summary>
    /// The round's presentation between strokes, layered on the session's own states. Each hole opens with its title card
    /// and a flyover under the briefing; it closes on the hole card, then the running scorecard, then dips through
    /// Midnight into the cart drive to the next tee. The last hole dips into the winner celebrating on the final green,
    /// then the standings, which offer play again (same players) or the menu. Owns no rules: every step is a session
    /// command sent through <see cref="GameHost"/>, and nothing here changes what is scored.
    /// </summary>
    public sealed class RoundFlow : MonoBehaviour
    {
        private const float TitleSeconds = 2.8f;
        private const float CelebrateEvery = 4.2f;
        /// <summary>Where the winner stands on the last green: this far from the cup toward the tee (m).</summary>
        private const float WinnerFromCup = 2.4f;

        private enum HolePage { Card, Summary }
        private enum FinalPage { None, Winner, Standings }

        private GameHost _host;
        private GolfCameraRig _rig;
        private CourseGolfer _golfer;
        private RoundOverlay _overlay;
        private HoleFlyover _flyover;

        private SessionState _seenState = (SessionState)(-1);
        private (int session, int hole) _seenHole = (-1, -1);
        private int _revealedSession = -1, _teedOffSession = -1;
        private float _titleUntil;
        private HolePage _holePage;
        private FinalPage _finalPage;
        private float _nextCelebrate;
        private bool _celebrateFist;
        private int _snapFrame = -1;
        private Vector3 _winnerSpot, _winnerFacing, _winnerCup;
        private readonly System.Random _debugRandom = new System.Random(7);

        public bool OwnsCamera => (_flyover != null && _flyover.Active) || _finalPage != FinalPage.None;
        public bool OwnsGolfer => _finalPage != FinalPage.None;
        public bool Transitioning => _overlay != null && _overlay.Dipping;

        public void Bind(GameHost host, GolfCameraRig rig, CourseGolfer golfer, HudView hud, Transform presentationRoot)
        {
            _host = host;
            _rig = rig;
            _golfer = golfer;
            _overlay = RoundOverlay.Create(hud);
            _flyover = HoleFlyover.Create(presentationRoot);
        }

        // ---------------------------------------------------------------- State

        /// <summary>Called by the host each frame before it renders, with the committed snapshot.</summary>
        public void Tick(SessionSnapshot snap)
        {
            _overlay.ReducedMotion = _host.ReducedMotion;
            SessionState state = snap.EffectiveState;
            var hole = (snap.Data.SessionId, snap.Data.HoleIndex);
            if (state != _seenState || hole != _seenHole)
            {
                Leave(_seenState);
                Enter(state, snap);
                _seenState = state;
                _seenHole = hole;
            }

            if (state == SessionState.BRIEFING && _overlay.TitleVisible && UnityEngine.Time.unscaledTime >= _titleUntil) _overlay.HideTitle();
            if (state == SessionState.ROUND_COMPLETE && _finalPage != FinalPage.None) Celebrate(snap);
        }

        private void Enter(SessionState state, SessionSnapshot snap)
        {
            int session = snap.Data.SessionId;
            switch (state)
            {
                case SessionState.HOLE_LOAD:
                case SessionState.BRIEFING:
                    // The first tee appears without a drive: reveal it out of Midnight, once per round.
                    bool firstTee = snap.Data.HoleIndex == 0 && _teedOffSession != session;
                    if (firstTee && _revealedSession != session)
                    {
                        _revealedSession = session;
                        _overlay.Reveal();
                    }
                    if (state != SessionState.BRIEFING) break;
                    _teedOffSession = session;
                    BeginIntro(snap, firstTee);
                    break;
                case SessionState.HOLE_COMPLETE:
                    _holePage = HolePage.Card;
                    break;
                case SessionState.ROUND_COMPLETE:
                    BeginFinale(snap);
                    break;
            }
        }

        private void Leave(SessionState state)
        {
            switch (state)
            {
                case SessionState.BRIEFING:
                    _overlay.HideTitle();
                    _flyover.End();
                    break;
                case SessionState.ROUND_COMPLETE:
                    EndFinale();
                    break;
            }
        }

        private void LateUpdate()
        {
            // A cut hidden by the dip: by now the host has framed the new state, so jump straight to it.
            if (_snapFrame < 0 || UnityEngine.Time.frameCount < _snapFrame || _rig == null) return;
            _snapFrame = -1;
            _rig.Snap();
        }

        private void SnapCameraNextFrame() => _snapFrame = UnityEngine.Time.frameCount + 1;

        // ---------------------------------------------------------------- Input

        /// <summary>
        /// Round-flow keys, offered by the host before its own per-state handling. Returns true when the key was used.
        /// Input waits while a dip is running so a transition is never interrupted half way.
        /// </summary>
        public bool HandleInput(SessionSnapshot snap, Keyboard kb, bool confirm, bool back)
        {
            if (Transitioning) return true;
            if (Debug.isDebugBuild && kb.f9Key.wasPressedThisFrame)
            {
                DebugSkip(kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                return true;
            }

            switch (snap.State)
            {
                case SessionState.BRIEFING:
                    if (!confirm) return false;
                    TeeOff();
                    return true;

                case SessionState.HOLE_COMPLETE:
                    if (confirm)
                    {
                        if (_holePage == HolePage.Card) _holePage = HolePage.Summary;
                        else _overlay.Dip(() => _host.Send(CommandType.HoleContinue));
                        return true;
                    }
                    if (back && _holePage == HolePage.Summary)
                    {
                        _holePage = HolePage.Card;
                        return true;
                    }
                    return false;

                case SessionState.ROUND_COMPLETE:
                    if (_finalPage == FinalPage.Winner)
                    {
                        if (!confirm) return false;
                        ShowStandings();
                        return true;
                    }
                    if (confirm)
                    {
                        _overlay.Dip(() =>
                        {
                            if (!_host.PlayAgain() && !_host.OpenSetup()) _host.OpenShop();
                        }, 0.45f, 0.2f, 0.8f);
                        return true;
                    }
                    if (back)
                    {
                        _overlay.Dip(() => _host.ReturnToMenu(), 0.45f, 0.2f, 0.6f);
                        return true;
                    }
                    return false;
            }
            return false;
        }

        /// <summary>HUD flags for the current page of the flow.</summary>
        public void Decorate(HudContext ctx)
        {
            SessionSnapshot snap = ctx.Snapshot;
            SessionState state = snap.State;
            ctx.HoleIntro = state == SessionState.BRIEFING && _overlay.TitleVisible;
            ctx.RoundFinale = state == SessionState.ROUND_COMPLETE && _finalPage == FinalPage.Winner;
            ctx.PlayAgain = state == SessionState.ROUND_COMPLETE;
            if (state == SessionState.HOLE_COMPLETE && _holePage == HolePage.Summary)
            {
                bool last = snap.Data.HoleIndex >= snap.Data.HoleCount - 1;
                ctx.ShowScorecard = true;
                ctx.ScorecardFooter = last ? "ESC BACK   ENTER FINAL RESULTS" : "ESC BACK   ENTER NEXT HOLE";
            }
        }

        // ---------------------------------------------------------------- Hole intro

        private void BeginIntro(SessionSnapshot snap, bool firstTee)
        {
            HoleDefinition hole = snap.Hole;
            if (hole == null) return;
            _overlay.ShowTitle(hole, snap.Data.HoleIndex, snap.Data.HoleCount);
            _titleUntil = UnityEngine.Time.unscaledTime + TitleSeconds;
            Camera cam = _rig != null ? _rig.Camera : Camera.main;
            if (_host.ReducedMotion || cam == null || _host.ShopOpen) return;
            // The first tee has no drive to come out of (the camera may still be in the shop): start from overhead.
            if (firstTee) Establish(cam.transform, hole);
            _flyover.Begin(hole, cam, _rig);
        }

        private static void Establish(Transform cam, HoleDefinition hole)
        {
            Vector3 tee = hole.TeeM.ToUnity(), cup = hole.CupM.ToUnity();
            Vector3 mid = Vector3.Lerp(tee, cup, 0.5f);
            Vector3 d = cup - tee;
            d.y = 0f;
            d = d.sqrMagnitude > 1f ? d.normalized : Vector3.forward;
            Vector3 pos = mid - d * 140f + Vector3.up * 95f;
            cam.SetPositionAndRotation(pos, Quaternion.LookRotation(mid - pos, Vector3.up));
        }

        /// <summary>
        /// ENTER in the briefing: the first press clears the hole title (the briefing card follows); then tee off. A flyover
        /// still in the air dips through Midnight to the address view rather than racing the camera back to the tee.
        /// </summary>
        private void TeeOff()
        {
            if (_overlay.TitleVisible)
            {
                _overlay.HideTitle();
                return;
            }
            if (_flyover.Active && !_flyover.Settled)
            {
                _overlay.Dip(() =>
                {
                    _flyover.End();
                    if (Accepted(_host.Send(CommandType.BriefingReady))) SnapCameraNextFrame();
                }, 0.3f, 0.12f, 0.5f);
                return;
            }
            _flyover.End();
            _host.Send(CommandType.BriefingReady);
        }

        // ---------------------------------------------------------------- Finale

        /// <summary>
        /// A finished round opens on the winner (or the tied leaders' first) celebrating beside the last cup under the banner
        /// and confetti; an ended-early round goes straight to the standings.
        /// </summary>
        private void BeginFinale(SessionSnapshot snap)
        {
            _finalPage = FinalPage.None;
            List<ScorecardRow> rows = Ranked(snap, out int leaders);
            HoleDefinition green = snap.Hole;
            if (snap.Data.Completion != RoundCompletion.Complete || rows.Count == 0 || rows[0].Thru == 0 || green == null) return;

            ScorecardRow top = rows[0];
            PlayerState winner = snap.Data.Players.Find(p => p.Id == top.PlayerId);
            Color identity = HudBrand.Player(winner?.ColorIndex ?? 0);

            _winnerCup = green.CupM.ToUnity();
            Vector3 toTee = green.TeeM.ToUnity() - _winnerCup;
            toTee.y = 0f;
            _winnerFacing = toTee.sqrMagnitude > 1f ? toTee.normalized : Vector3.back;
            Vector3 spot = _winnerCup + _winnerFacing * WinnerFromCup;
            spot.y = (float)TerrainModel.Height(green.Terrain, spot.x, spot.z);
            _winnerSpot = spot;

            if (_golfer != null && winner != null)
            {
                // CourseGolfer.Stand faces the golfer across the aim line; aim 90 degrees off so they face the tee side.
                Vector3 fwd = Quaternion.Euler(0f, -90f, 0f) * _winnerFacing;
                Vector3 left = Vector3.Cross(fwd, Vector3.up);
                _golfer.Show(true);
                _golfer.Stand(_winnerSpot - left * _golfer.AddressOffset + fwd * 0.15f, fwd, winner, null, address: false, wait: true);
            }
            _celebrateFist = false;
            _nextCelebrate = UnityEngine.Time.time + 0.5f;

            if (_rig != null)
            {
                _rig.Mode = CameraMode.Celebration;
                _rig.SetScene(_winnerSpot, _winnerFacing, _winnerCup);
                _rig.FollowTarget(null);
                if (Transitioning) SnapCameraNextFrame();
            }

            string name, twist;
            if (leaders <= 1)
            {
                name = top.Name.ToUpperInvariant();
                twist = "wins";
            }
            else
            {
                name = leaders == 2 ? $"{rows[0].Name.ToUpperInvariant()} & {rows[1].Name.ToUpperInvariant()}" : $"{leaders}-WAY TIE";
                twist = "share the win";
            }
            string dot = "<space=24>·<space=24>";
            string detail = $"{top.CompletedTotal} STROKES{dot}{top.ToParDisplay.Replace("-", "\u2212")}";
            string hint = $"<color={HudBrand.Hex(HudBrand.Sun)}>ENTER</color><space=18>STANDINGS";
            _overlay.ShowWinner($"ROUND COMPLETE{dot}{snap.Data.HoleCount} HOLES", name, twist, detail, identity, hint);
            _finalPage = FinalPage.Winner;
        }

        private void ShowStandings()
        {
            _overlay.HideWinner();
            _overlay.SetConfetti(false);
            _finalPage = FinalPage.Standings;
        }

        private void EndFinale()
        {
            _overlay.HideWinner();
            _overlay.SetConfetti(false);
            _finalPage = FinalPage.None;
        }

        /// <summary>Keeps the winner framed and alternates the victory and fist-pump clips while the finale is up.</summary>
        private void Celebrate(SessionSnapshot snap)
        {
            if (_rig != null)
            {
                _rig.Mode = CameraMode.Celebration;
                _rig.SetScene(_winnerSpot, _winnerFacing, _winnerCup);
            }
            if (_golfer == null || UnityEngine.Time.time < _nextCelebrate) return;
            GolferMotion motion = _golfer.Appearance != null ? _golfer.Appearance.GetComponent<GolferMotion>() : null;
            if (motion != null) motion.React(_celebrateFist ? GolferMotion.Reaction.Good : GolferMotion.Reaction.Holed);
            _celebrateFist = !_celebrateFist;
            _nextCelebrate = UnityEngine.Time.time + CelebrateEvery;
        }

        /// <summary>Most holes completed first, then fewest strokes; <paramref name="leaders"/> counts those tied for first.</summary>
        private static List<ScorecardRow> Ranked(SessionSnapshot snap, out int leaders)
        {
            var rows = new List<ScorecardRow>(snap.Scorecard);
            rows.Sort((a, b) =>
            {
                int byThru = b.Thru.CompareTo(a.Thru);
                return byThru != 0 ? byThru : a.CompletedTotal.CompareTo(b.CompletedTotal);
            });
            leaders = 0;
            foreach (ScorecardRow r in rows)
            {
                if (r.Thru != rows[0].Thru || r.CompletedTotal != rows[0].CompletedTotal) break;
                leaders++;
            }
            return rows;
        }

        // ---------------------------------------------------------------- Debug

        private void DebugSkip(bool wholeRound)
        {
            if (_host.DebugInput == null) return;
            bool ok = wholeRound ? DebugAutoPlay.FinishRound(_host, _debugRandom) : DebugAutoPlay.FinishHole(_host, _debugRandom);
            _host.ShowNotice(ok
                ? wholeRound ? "DEBUG  ·  PLAYED ON TO THE LAST HOLE" : "DEBUG  ·  HOLE PLAYED OUT"
                : "DEBUG  ·  NOTHING TO AUTO-PLAY HERE");
        }

        private static bool Accepted(CommandReply reply) => reply != null && reply.Accepted;
    }
}
