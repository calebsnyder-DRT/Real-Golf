using System;
using System.Collections.Generic;
using System.IO;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Hardware;
using Puttshack.Protocol;
using Puttshack.Runtime.Course;
using Puttshack.Runtime.Hud;
using Puttshack.Runtime.Views;
using Puttshack.Session;
using Puttshack.Storage;
using Puttshack.Time;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Puttshack.Runtime
{
    /// <summary>
    /// Presentation host. Owns no rules: it forwards cabinet events and staff/player commands to the
    /// <see cref="SessionCoordinator"/> and renders whatever committed snapshot comes back.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameHost : MonoBehaviour
    {
        [Tooltip("Keep the journal in memory (tests). Off: JSON-lines journal under persistentDataPath.")]
        public bool InMemoryStorage;
        [Tooltip("Presentation playback speed. Display only; results are already committed.")]
        public float PlaybackSpeed = 1f;
        public bool ReducedMotion;
        public bool ShowEmulatorPanel = true;

        /// <summary>Set by tests before a scene loads so they never resume a real on-disk session.</summary>
        public static bool ForceInMemoryStorage;

        private const float ResultHoldSeconds = 0.8f;
        private const float NoticeSeconds = 4f;

        private CourseDefinition _course;
        private ISessionStore _store;
        private IMonotonicClock _clock;
        private string _storeDirectory;

        private Transform _courseRoot;
        private BakedHole _holeVisual;
        private CourseVisualAssets _fallbackAssets;
        private readonly List<BallView> _balls = new List<BallView>();
        private Material _lineMaterial;
        private Material _golferMaterial;
        private GolferView _golfer;
        private AimFanView _fan;
        private GolfCameraRig _cameraRig;
        private HudView _hud;

        private int _playingShotId = -1;
        private float _holdTimer = -1;
        private string _notice;
        private float _noticeUntil;
        private bool _showScorecard;
        private AssistanceProfileId _lobbyAssistance = AssistanceProfileId.Club;
        private bool _lobbyPaceCap = true;

        public SessionCoordinator Coordinator { get; private set; }
        public EmulatorCabinetAdapter Emulator { get; private set; }
        public InputAck LastAck { get; private set; }
        public CommandReply LastReply { get; private set; }
        public int RecoveryCount { get; private set; }
        public BakedHole HoleVisual => _holeVisual;
        public bool UsingFallbackVisuals { get; private set; }
        public bool Initialized => Coordinator != null;

        private void Start() => Initialize();

        public void Initialize()
        {
            if (Coordinator != null) return;

            _course = GrayboxCourse.Create();
            _clock = new SystemMonotonicClock();
            InMemoryStorage |= ForceInMemoryStorage;
            if (InMemoryStorage)
            {
                _store = new InMemorySessionStore();
            }
            else
            {
                _storeDirectory = Path.Combine(Application.persistentDataPath, "PuttshackPro", "sessions");
                _store = new JournalSessionStore(_storeDirectory);
            }

            Emulator = new EmulatorCabinetAdapter(_clock);
            Coordinator = SessionCoordinator.StartOrResume(_course, _store, _clock);
            if (Coordinator.Recovered) ShowNotice("Round restored - your scores are saved.");

            BuildPresentation();
            Debug.Log($"[Puttshack] Session {Coordinator.Snapshot.Data.SessionId} at {Coordinator.State} (store: {(InMemoryStorage ? "memory" : _storeDirectory)}).");
        }

        private void BuildPresentation()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            _cameraRig = cam.GetComponent<GolfCameraRig>();
            if (_cameraRig == null) _cameraRig = cam.gameObject.AddComponent<GolfCameraRig>();
            _cameraRig.Bind(cam);
            _cameraRig.ReducedMotion = ReducedMotion;

            if (FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.2f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            }

            var dynamicRoot = new GameObject("Shot Presentation").transform;
            Shader lineShader = Shader.Find("Sprites/Default");
            _lineMaterial = new Material(lineShader) { name = "Lines" };
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _golferMaterial = CourseVisualAssets.LitMaterial(lit, "Golfer", Color.white, 0.2f);
            _golfer = GolferView.Create(dynamicRoot, _golferMaterial, _golferMaterial);
            _fan = AimFanView.Create(dynamicRoot, _lineMaterial);

            for (int i = 0; i < SessionCoordinator.MaxPlayers; i++)
            {
                Color tint = Color.Lerp(Color.white, Palette.Player(i), 0.25f);
                Material ballMat = CourseVisualAssets.LitMaterial(lit, $"Ball {i + 1}", tint, 0.75f);
                BallView ball = BallView.Create(dynamicRoot, ballMat, _lineMaterial);
                ball.name = $"Ball P{i + 1}";
                ball.gameObject.SetActive(false);
                _balls.Add(ball);
            }

            _hud = HudView.Create();
            if (ShowEmulatorPanel) gameObject.AddComponent<EmulatorPanel>().Host = this;

            GameObject courseGo = GameObject.Find("Course");
            _courseRoot = courseGo != null ? courseGo.transform : new GameObject("Course").transform;
        }

        // ---------------------------------------------------------------- Loop

        private void Update()
        {
            if (Coordinator == null) return;

            PumpCabinet();
            Guarded(() => Coordinator.Tick());
            HandleKeyboard();
            DriveAutomaticTransitions();
            UpdateViews();
        }

        private void PumpCabinet()
        {
            IReadOnlyList<CabinetEvent> events = Emulator.Poll(_clock.NowMs);
            Coordinator.UpdateCabinetHealth(Emulator.Health);
            foreach (CabinetEvent evt in events)
            {
                InputAck ack = null;
                Guarded(() => ack = Coordinator.HandleCabinetEvent(evt));
                if (ack != null) LastAck = ack;
            }
        }

        /// <summary>Runs a service call; a simulated crash rebuilds the service from durable storage like a restart.</summary>
        private void Guarded(Action call)
        {
            try
            {
                call();
            }
            catch (SimulatedCrashException crash)
            {
                SimulateRestart(crash.Point);
            }
        }

        public void SimulateRestart(CrashPoint point)
        {
            Debug.LogWarning($"[Puttshack] Simulated crash {point}; restarting the service from storage.");
            if (!InMemoryStorage) _store = new JournalSessionStore(_storeDirectory);
            Coordinator = SessionCoordinator.StartOrResume(_course, _store, _clock);
            Coordinator.UpdateCabinetHealth(Emulator.Health);
            Coordinator.Tick();
            RecoveryCount++;
            _playingShotId = -1;
            _holdTimer = -1;
            foreach (BallView b in _balls) b.Stop();
            ShowNotice(point == CrashPoint.AfterReservation
                ? "Recovered: your shot was saved and resolved - do not play again."
                : "Recovered: your shot result was restored.");
        }

        public CommandReply Send(CommandType type, Action<SessionCommand> configure = null)
        {
            SessionCommand cmd = SessionCommand.Of(type, Coordinator.Revision);
            configure?.Invoke(cmd);
            CommandReply reply = null;
            Guarded(() => reply = Coordinator.Apply(cmd));
            LastReply = reply;
            if (reply != null && !reply.Accepted && reply.Reason != RejectReason.WRONG_STATE)
                ShowNotice(reply.Detail ?? reply.Reason.ToString());
            return reply;
        }

        public void ShowNotice(string text)
        {
            _notice = text;
            _noticeUntil = UnityEngine.Time.unscaledTime + NoticeSeconds;
        }

        private void DriveAutomaticTransitions()
        {
            SessionSnapshot snap = Coordinator.Snapshot;
            switch (snap.State)
            {
                case SessionState.HOLE_LOAD:
                    EnsureHoleVisual(snap.Hole);
                    Send(CommandType.HoleReady);
                    break;
                case SessionState.SHOT_RESOLVED:
                    if (snap.LastShot?.Result != null)
                    {
                        StartPlayback(snap);
                        Send(CommandType.PresentationBegin);
                    }
                    break;
                case SessionState.SHOT_PRESENTING:
                {
                    if (_playingShotId != snap.Data.LastShotId) StartPlayback(snap);
                    BallView ball = ShooterBall(snap);
                    if (ball != null && !ball.Advance(UnityEngine.Time.deltaTime * PlaybackSpeed)) break;
                    if (_holdTimer < 0) _holdTimer = ResultHoldSeconds / Mathf.Max(PlaybackSpeed, 0.01f);
                    _holdTimer -= UnityEngine.Time.deltaTime;
                    if (_holdTimer <= 0)
                    {
                        _holdTimer = -1;
                        Send(CommandType.PresentationEnd);
                    }
                    break;
                }
            }
        }

        private void StartPlayback(SessionSnapshot snap)
        {
            ShotRecord shot = snap.LastShot;
            if (shot?.Result == null) return;
            EnsureHoleVisual(snap.Hole);
            BallView ball = ShooterBall(snap);
            if (ball == null) return;
            ball.gameObject.SetActive(true);
            ball.Play(shot.Result);
            _playingShotId = shot.ShotId;
            _holdTimer = -1;
        }

        public void SkipPresentation()
        {
            SessionSnapshot snap = Coordinator.Snapshot;
            if (snap.State != SessionState.SHOT_PRESENTING) return;
            ShooterBall(snap)?.Skip();
            _holdTimer = -1;
            Send(CommandType.PresentationSkip);
        }

        private BallView ShooterBall(SessionSnapshot snap)
        {
            ShotRecord shot = snap.LastShot;
            if (shot == null) return null;
            int index = snap.Data.Players.FindIndex(p => p.Id == shot.PlayerId);
            return index >= 0 && index < _balls.Count ? _balls[index] : null;
        }

        /// <summary>Uses the baked, editor-authored hole if its hash matches; otherwise builds a fallback and warns.</summary>
        private void EnsureHoleVisual(HoleDefinition hole)
        {
            if (hole == null || (_holeVisual != null && _holeVisual.HoleId == hole.Id)) return;

            foreach (BakedHole other in FindObjectsByType<BakedHole>(FindObjectsInactive.Include))
                other.gameObject.SetActive(other.HoleId == hole.Id && other.Matches(hole));

            BakedHole baked = BakedHole.FindInScene(hole, out BakedHole stale);
            if (baked != null)
            {
                _holeVisual = baked;
                UsingFallbackVisuals = false;
                return;
            }

            Debug.LogWarning(stale != null
                ? $"[Puttshack] Baked visuals for {hole.Id} are stale (hash mismatch). Run Puttshack > Bake Course. Using a runtime fallback."
                : $"[Puttshack] No baked visuals for {hole.Id} in this scene. Run Puttshack > Build Main Scene. Using a runtime fallback.");
            if (_fallbackAssets == null) _fallbackAssets = CourseVisualAssets.CreateDefault();
            _holeVisual = CourseVisualBuilder.Build(hole, _fallbackAssets, _courseRoot);
            UsingFallbackVisuals = true;
        }

        // ---------------------------------------------------------------- Input

        private void HandleKeyboard()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            for (int d = 0; d < 10; d++)
            {
                Key key = d == 9 ? Key.Digit0 : Key.Digit1 + d;
                if (kb[key].wasPressedThisFrame) Emulator.TriggerLane((d + 1) + (shift ? 10 : 0));
            }
            if (kb.f1Key.wasPressedThisFrame)
            {
                EmulatorPanel panel = GetComponent<EmulatorPanel>();
                if (panel != null) panel.Visible = !panel.Visible;
            }

            SessionSnapshot snap = Coordinator.Snapshot;
            bool confirm = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            bool back = kb.escapeKey.wasPressedThisFrame;

            if (kb.tabKey.wasPressedThisFrame) _showScorecard = !_showScorecard;
            if (_showScorecard && (confirm || back))
            {
                _showScorecard = false;
                return;
            }

            switch (snap.State)
            {
                case SessionState.ATTRACT:
                    if (confirm) Send(CommandType.Start);
                    break;
                case SessionState.LOBBY:
                    LobbyInput(kb, snap, confirm);
                    break;
                case SessionState.BRIEFING:
                    if (confirm) Send(CommandType.BriefingReady);
                    else if (back) Send(CommandType.Pause);
                    break;
                case SessionState.SHOT_SETUP:
                    SetupInput(kb, snap, shift, confirm, back);
                    break;
                case SessionState.ARMING:
                case SessionState.SHOT_ARMED:
                    if (back) Send(CommandType.Disarm);
                    break;
                case SessionState.SHOT_PRESENTING:
                    if (confirm) SkipPresentation();
                    break;
                case SessionState.SHOT_RESULT:
                    if (confirm) Send(CommandType.ResultContinue);
                    else if (back) Send(CommandType.Pause);
                    break;
                case SessionState.HANDOFF:
                    if (confirm) Send(CommandType.NextPlayerReady);
                    else if (back) Send(CommandType.Pause);
                    break;
                case SessionState.HOLE_COMPLETE:
                    if (confirm) Send(CommandType.HoleContinue);
                    break;
                case SessionState.ROUND_COMPLETE:
                    if (confirm) Send(CommandType.ReturnToAttract);
                    break;
                case SessionState.PAUSED:
                    if (confirm || back) Send(CommandType.Resume);
                    else if (kb.eKey.wasPressedThisFrame) Send(CommandType.EndRound);
                    break;
                case SessionState.FAULT:
                    if (confirm) Send(CommandType.Resume);
                    break;
            }
        }

        private void LobbyInput(Keyboard kb, SessionSnapshot snap, bool confirm)
        {
            var names = new List<string>();
            foreach (PlayerState p in snap.Data.Players) names.Add(p.Name);
            if (kb.upArrowKey.wasPressedThisFrame && names.Count < SessionCoordinator.MaxPlayers)
            {
                names.Add(DefaultName(names));
                Send(CommandType.RosterSet, c => c.Names = names);
            }
            else if (kb.downArrowKey.wasPressedThisFrame && names.Count > 1)
            {
                names.RemoveAt(names.Count - 1);
                Send(CommandType.RosterSet, c => c.Names = names);
            }
            if (kb.leftArrowKey.wasPressedThisFrame) _lobbyAssistance = (AssistanceProfileId)(((int)_lobbyAssistance + 2) % 3);
            if (kb.rightArrowKey.wasPressedThisFrame) _lobbyAssistance = (AssistanceProfileId)(((int)_lobbyAssistance + 1) % 3);
            if (kb.cKey.wasPressedThisFrame) _lobbyPaceCap = !_lobbyPaceCap;
            if (confirm)
            {
                AssistanceProfileId assist = _lobbyAssistance;
                bool cap = _lobbyPaceCap;
                Send(CommandType.SettingsConfirm, c =>
                {
                    c.Assistance = assist;
                    c.PaceCapEnabled = cap;
                });
            }
        }

        private static readonly string[] DefaultNames = { "Caleb", "Maya", "Jordan", "Sam", "Nia", "Leo" };

        private static string DefaultName(List<string> taken)
        {
            foreach (string n in DefaultNames)
            {
                if (!taken.Contains(n)) return n;
            }
            return $"Player {taken.Count + 1}";
        }

        private void SetupInput(Keyboard kb, SessionSnapshot snap, bool shift, bool confirm, bool back)
        {
            float step = shift ? 5f : 1f;
            if (kb.leftArrowKey.wasPressedThisFrame) Aim(-step);
            if (kb.rightArrowKey.wasPressedThisFrame) Aim(step);

            int clubIndex = ClubCatalog.IndexOf(snap.Data.Setup.ClubId);
            int count = ClubCatalog.All.Count;
            if (kb.upArrowKey.wasPressedThisFrame) SelectClub(ClubCatalog.All[(clubIndex + count - 1) % count].Id);
            if (kb.downArrowKey.wasPressedThisFrame) SelectClub(ClubCatalog.All[(clubIndex + 1) % count].Id);

            if (kb.pKey.wasPressedThisFrame && snap.Data.Setup.ClubId == ClubCatalog.PutterId)
            {
                PuttScale next = (PuttScale)(((int)snap.Data.Setup.PuttScale + 1) % 3);
                Send(CommandType.SetPuttScale, c => c.PuttScale = next);
            }

            if (confirm) Send(CommandType.Arm);
            else if (back) Send(CommandType.Pause);
        }

        public CommandReply Aim(float degreesRight) =>
            Send(CommandType.SetAim, c => c.AimYawDeltaRad = Units.DegToRad(degreesRight));

        public CommandReply SelectClub(string clubId) => Send(CommandType.SelectClub, c => c.ClubId = clubId);

        // ---------------------------------------------------------------- Views

        private void UpdateViews()
        {
            SessionSnapshot snap = Coordinator.Snapshot;
            SessionState state = snap.EffectiveState;
            HoleDefinition hole = snap.Hole;
            bool inHole = snap.Data.HoleCount > 0 && state >= SessionState.BRIEFING && state <= SessionState.HOLE_COMPLETE && state != SessionState.TUTORIAL;
            if (inHole) EnsureHoleVisual(hole);

            BallView shooterBall = null;
            bool presenting = state == SessionState.SHOT_PRESENTING || state == SessionState.SHOT_RESOLVED;
            if (presenting) shooterBall = ShooterBall(snap);

            for (int i = 0; i < _balls.Count; i++)
            {
                BallView ball = _balls[i];
                HoleProgress hp = inHole ? snap.ProgressFor(i) : null;
                bool visible = hp?.Ball != null && (hp.Status == HoleStatus.ACTIVE || ball == shooterBall || (ball.IsPlaying && ball == shooterBall));
                if (ball == shooterBall && presenting)
                {
                    ball.gameObject.SetActive(true);
                    continue;
                }
                ball.gameObject.SetActive(visible);
                if (visible && !ball.IsPlaying) ball.PlaceAt(hp.Ball.Position);
            }

            PlayerState player = snap.CurrentPlayer;
            HoleProgress progress = snap.CurrentProgress;
            bool setup = state == SessionState.SHOT_SETUP || state == SessionState.ARMING || state == SessionState.SHOT_ARMED;
            bool golferVisible = inHole && progress?.Ball != null && player != null &&
                                 (setup || state == SessionState.HANDOFF || state == SessionState.SHOT_RESERVED);

            Vector3 ballPos = progress?.Ball != null ? progress.Ball.Position.ToUnity() : hole?.TeeM.ToUnity() ?? Vector3.zero;
            Vector3 aim = Vector3.forward;
            if (snap.Data.Arm != null) aim = snap.Data.Arm.AimForward.ToUnity();
            else if (progress?.Ball != null)
            {
                Vector3 toTarget = snap.Data.Setup.AimTargetM.ToUnity() - ballPos;
                toTarget.y = 0;
                if (toTarget.sqrMagnitude > 0.01f) aim = toTarget.normalized;
            }
            if (presenting || state == SessionState.SHOT_RESULT)
            {
                ShotRecord last = snap.LastShot;
                if (last != null)
                {
                    aim = last.Intent.AimForward.ToUnity();
                    ballPos = last.Intent.OriginM.ToUnity();
                }
            }

            _golfer.Show(golferVisible);
            if (golferVisible) _golfer.Stand(ballPos, aim, Palette.Player(player.ColorIndex));

            bool fanVisible = inHole && setup && progress?.Ball != null && snap.Data.Setup.ClubId != null;
            _fan.Show(fanVisible);
            if (fanVisible)
            {
                ClubDefinition club = ClubCatalog.Get(snap.Data.Arm?.ClubId ?? snap.Data.Setup.ClubId);
                float remaining = (float)Vec3d.HorizontalDistance(progress.Ball.Position, hole.CupM);
                float length = club.ShotType == ShotType.Putt ? Mathf.Clamp(remaining * 1.2f, 2f, 10f) : 28f;
                _fan.Draw(ballPos, aim, club, 0, Coordinator.Preview(), length);
            }

            UpdateCamera(snap, state, ballPos, aim, hole, shooterBall);

            _hud.Render(new HudContext
            {
                Snapshot = snap,
                NowMs = _clock.NowMs,
                Preview = null,
                LastAck = LastAck,
                ShowScorecard = _showScorecard,
                Notice = UnityEngine.Time.unscaledTime < _noticeUntil ? _notice : null,
                Hints = Hints(state),
                LobbyAssistance = _lobbyAssistance,
                LobbyPaceCap = _lobbyPaceCap,
            });
        }

        private void UpdateCamera(SessionSnapshot snap, SessionState state, Vector3 ballPos, Vector3 aim, HoleDefinition hole, BallView shooterBall)
        {
            Vector3 cup = hole != null ? hole.CupM.ToUnity() : Vector3.zero;
            Vector3 tee = hole != null ? hole.TeeM.ToUnity() : Vector3.zero;
            CameraMode mode;
            switch (state)
            {
                case SessionState.ATTRACT:
                case SessionState.LOBBY:
                    mode = CameraMode.Attract;
                    ballPos = tee;
                    break;
                case SessionState.HOLE_LOAD:
                case SessionState.BRIEFING:
                case SessionState.HOLE_COMPLETE:
                case SessionState.ROUND_COMPLETE:
                    mode = CameraMode.Flyover;
                    ballPos = tee;
                    break;
                case SessionState.SHOT_RESOLVED:
                case SessionState.SHOT_PRESENTING:
                    mode = CameraMode.Follow;
                    break;
                case SessionState.SHOT_RESULT:
                    mode = CameraMode.Result;
                    shooterBall = ShooterBall(snap);
                    break;
                default:
                {
                    string clubId = snap.Data.Arm?.ClubId ?? snap.Data.Setup.ClubId;
                    mode = clubId == ClubCatalog.PutterId ? CameraMode.Putting : CameraMode.Setup;
                    break;
                }
            }

            _cameraRig.Mode = mode;
            _cameraRig.ReducedMotion = ReducedMotion;
            _cameraRig.SetScene(ballPos, aim, cup);
            _cameraRig.FollowTarget(shooterBall != null ? shooterBall.transform : null);
            if (!_cameraInitialized)
            {
                _cameraRig.Snap();
                _cameraInitialized = true;
            }
        }

        private bool _cameraInitialized;

        private static string Hints(SessionState state)
        {
            switch (state)
            {
                case SessionState.SHOT_SETUP:
                    return "LEFT/RIGHT aim (SHIFT x5)  \u00B7  UP/DOWN club  \u00B7  P putt scale  \u00B7  ENTER ready  \u00B7  TAB scorecard  \u00B7  ESC pause  \u00B7  F1 emulator";
                case SessionState.ARMING:
                case SessionState.SHOT_ARMED:
                    return "1-0 = lanes 1-10, SHIFT+1-0 = lanes 11-20 (emulated target holes)  \u00B7  ESC cancel";
                case SessionState.SHOT_PRESENTING:
                    return "ENTER skip to result";
                default:
                    return "ENTER continue  \u00B7  TAB scorecard  \u00B7  F1 emulator";
            }
        }
    }
}
