using System.Collections.Generic;
using RealGolf.CameraRig;
using RealGolf.Course;
using RealGolf.Data;
using RealGolf.Gameplay;
using RealGolf.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RealGolf.Core
{
    /// <summary>
    /// Runs a hot-seat round: builds the course, cycles turns in proper golf
    /// order (the ball furthest from the cup plays next) and keeps the card.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        private const float HoleIntroDuration = 3.6f;
        private const float ResultPauseSeconds = 2.4f;
        private const int StrokeLimitOverPar = 6;
        private const float PenaltyStrokes = 1f;

        [Range(1, 4)] public int PlayerCount = 2;
        public int Seed = 0;
        public bool RandomiseSeed = true;

        /// <summary>
        /// Name for the scorecard and the results screen, set by the course select
        /// menu. Left null for a random draw, where CourseDefinition's own default
        /// title stands.
        /// </summary>
        public string CourseName;

        private CourseDefinition _course;
        private readonly List<GolfPlayer> _players = new();

        private GameHUD _hud;
        private GolfCamera _camera;
        private SwingController _swing;
        private LineRenderer _aimLine;
        private LineRenderer _slopeArrow;
        private GolfAudio _audio;
        private Wind _wind;

        private BuiltHole _hole;
        private BakedCourse _baked;
        private int _holeIndex;
        private GolfPlayer _activePlayer;

        private GamePhase _phase = GamePhase.Loading;
        private float _phaseTimer;
        private bool _overhead;

        public CourseDefinition Course => _course;
        public IReadOnlyList<GolfPlayer> Players => _players;

        private void Start()
        {
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Time.fixedDeltaTime = 0.01f;

            BuildSceneServices();
            StartNewRound();
        }

        private void BuildSceneServices()
        {
            _hud = gameObject.AddComponent<GameHUD>();
            _hud.Build();

            _audio = gameObject.AddComponent<GolfAudio>();

            _swing = gameObject.AddComponent<SwingController>();
            _swing.ShotTaken += OnShotTaken;

            // Reuse the scene camera when there is one so the URP per-camera
            // settings and post-processing volume stay in effect.
            Camera cam = Camera.main;
            if (cam == null)
            {
                var cameraGo = new GameObject("GolfCamera") { tag = "MainCamera" };
                cam = cameraGo.AddComponent<Camera>();
                cameraGo.AddComponent<AudioListener>();
            }

            cam.farClipPlane = 2000f;
            cam.nearClipPlane = 0.02f;
            cam.fieldOfView = 62f;
            cam.clearFlags = CameraClearFlags.Skybox;

            // The grading volume does nothing until the camera opts in, and a runtime
            // camera never does by default.
            UniversalAdditionalCameraData urpCamera = cam.GetUniversalAdditionalCameraData();
            if (urpCamera != null)
            {
                urpCamera.renderPostProcessing = true;
                urpCamera.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                urpCamera.antialiasingQuality = AntialiasingQuality.Medium;
            }

            _camera = cam.GetComponent<GolfCamera>();
            if (_camera == null)
            {
                _camera = cam.gameObject.AddComponent<GolfCamera>();
            }

            var aimGo = new GameObject("AimLine");
            aimGo.transform.SetParent(transform, false);
            _aimLine = aimGo.AddComponent<LineRenderer>();
            _aimLine.material = MaterialLibrary.AimMarker;
            _aimLine.widthMultiplier = 0.09f;
            _aimLine.positionCount = 2;
            _aimLine.useWorldSpace = true;
            _aimLine.enabled = false;

            var slopeGo = new GameObject("SlopeArrow");
            slopeGo.transform.SetParent(transform, false);
            _slopeArrow = slopeGo.AddComponent<LineRenderer>();
            _slopeArrow.material = MaterialLibrary.SlopeArrow;
            _slopeArrow.widthMultiplier = 0.07f;
            // Shaft plus two barbs, drawn as one polyline.
            _slopeArrow.positionCount = 5;
            _slopeArrow.useWorldSpace = true;
            _slopeArrow.enabled = false;
        }

        public void StartNewRound()
        {
            // A course baked into the scene fixes the seed: its meshes were generated
            // from one layout and would not describe another.
            _baked = FindFirstObjectByType<BakedCourse>();
            if (_baked != null)
            {
                if (RandomiseSeed || Seed != _baked.Seed)
                {
                    Debug.Log($"[GameManager] Using the baked course in the scene (seed {_baked.Seed}); " +
                              "ignoring the requested seed.");

                    // The name belongs to the layout that was asked for, which is not
                    // the one the baked meshes describe. Keeping it would put another
                    // course's name on this scorecard.
                    CourseName = null;
                }

                Seed = _baked.Seed;
                RandomiseSeed = false;
            }
            else if (RandomiseSeed)
            {
                Seed = Random.Range(1, int.MaxValue);
            }

            _course = CourseGenerator.Generate(Seed, CourseName);

            foreach (GolfPlayer player in _players)
            {
                if (player.Ball != null)
                {
                    Destroy(player.Ball.gameObject);
                }
            }

            _players.Clear();
            for (int i = 0; i < Mathf.Clamp(PlayerCount, 1, 4); i++)
            {
                var player = new GolfPlayer(i, $"Player {i + 1}") { Ball = CreateBall(i) };
                _players.Add(player);
            }

            _holeIndex = 0;
            LoadHole(_holeIndex);
        }

        private GolfBall CreateBall(int playerIndex)
        {
            // Physics lives on an unscaled root so the visual model can be sized freely.
            var go = new GameObject($"Ball_P{playerIndex + 1}");
            go.transform.SetParent(transform, false);

            var collider = go.AddComponent<SphereCollider>();
            collider.radius = GolfBall.Radius;

            float diameter = GolfBall.Radius * 2f;
            GameObject visual = ModelLibrary.Spawn(
                ModelLibrary.Ball(playerIndex), go.transform, go.transform.position, diameter, Quaternion.identity);

            if (visual != null)
            {
                // Spawn rests a model's base on the pivot; a ball wants its centre there.
                visual.transform.localPosition = new Vector3(0f, -GolfBall.Radius, 0f);
            }
            else
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(sphere.GetComponent<SphereCollider>());
                sphere.transform.SetParent(go.transform, false);
                sphere.transform.localScale = Vector3.one * diameter;
                sphere.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.PlayerColor(playerIndex);
            }

            go.AddComponent<Rigidbody>();
            var ball = go.AddComponent<GolfBall>();
            ball.Initialise(playerIndex, _audio);
            ball.CameToRest += info => OnBallCameToRest(playerIndex, info);
            return ball;
        }

        private void LoadHole(int index)
        {
            HoleDefinition definition = _course.Holes[index];

            if (_baked != null && _baked.Hole(index) != null)
            {
                // Slide the whole course so this hole sits at the origin, which is
                // where its analytic terrain lives. Nothing is built or destroyed, so
                // the other holes stay in view around it.
                _baked.AlignTo(index);
                _hole = HoleBuilder.Rebind(definition, _baked.Hole(index));
            }
            else
            {
                if (_hole != null)
                {
                    Destroy(_hole.Root);
                }

                _hole = HoleBuilder.Build(definition);
            }
            _wind = Wind.ForHole(Seed, definition.Number);

            CourseGenerator.SampleCenterline(definition, 0.02f, out Vector2 teeNormal);

            for (int i = 0; i < _players.Count; i++)
            {
                GolfPlayer player = _players[i];
                Vector2 planar = definition.TeePosition + teeNormal * ((i - (_players.Count - 1) * 0.5f) * 0.9f);
                Vector3 world = _hole.Terrain.ToWorld(planar);

                player.BeginHole(world);
                player.Ball.Configure(_hole.Terrain, _hole.CupWorldPosition);
                player.Ball.SetWind(_wind);
                player.Ball.PlaceAt(world, onTee: true);
            }

            _hud.SetHole(definition);
            _hud.UpdateLeaderboard(_players, _course);
            _hud.SetMeterVisible(false);
            _aimLine.enabled = false;
            _slopeArrow.enabled = false;

            _camera.SetHoleIntro(_hole.TeeWorldPosition, _hole.CupWorldPosition, HoleIntroDuration);
            _hud.ShowMessage($"Hole {definition.Number}  ·  {definition.ParLabel}", HoleIntroDuration);

            _phase = GamePhase.HoleIntro;
            _phaseTimer = HoleIntroDuration;
        }

        private void Update()
        {
            HandleGlobalInput();

            switch (_phase)
            {
                case GamePhase.HoleIntro:
                    _phaseTimer -= Time.deltaTime;
                    if (_phaseTimer <= 0f || GolfInput.ConfirmPressed)
                    {
                        _hud.HideMessage();
                        BeginNextTurn();
                    }

                    break;

                case GamePhase.Aiming:
                case GamePhase.Swinging:
                    TickSwing();
                    break;

                case GamePhase.BallInMotion:
                    if (_activePlayer != null)
                    {
                        _hud.SetDistanceToPin(DistanceToCup(_activePlayer.Ball.Position));
                    }

                    break;

                case GamePhase.HoleComplete:
                    _phaseTimer -= Time.deltaTime;
                    if (_phaseTimer <= 0f)
                    {
                        AdvanceHole();
                    }

                    break;
            }
        }

        private void HandleGlobalInput()
        {
            if (GolfInput.ScorecardPressed)
            {
                _hud.ToggleScorecard(_players, _course);
            }

            if (GolfInput.RestartPressed && _phase == GamePhase.RoundComplete)
            {
                StartNewRound();
                return;
            }

            if (GolfInput.PausePressed && _phase == GamePhase.RoundComplete)
            {
                // Unpicking a finished round in place would mean tearing down a built
                // hole, every player's ball, the HUD canvas and the camera rig, and
                // getting any of it wrong leaves debris in the next round. Reloading
                // the single scene does all of that and puts GameBootstrap back at
                // the course select, which is where the player wants to be.
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            if (GolfInput.OverheadPressed && _hole != null)
            {
                _overhead = !_overhead;
                if (_overhead)
                {
                    Vector3 center = Vector3.Lerp(_hole.TeeWorldPosition, _hole.CupWorldPosition, 0.5f);
                    float span = Vector3.Distance(_hole.TeeWorldPosition, _hole.CupWorldPosition);
                    _camera.SetOverhead(center, Mathf.Max(70f, span * 0.85f));
                }
                else if (_activePlayer != null)
                {
                    _camera.SetAim(_activePlayer.Ball.transform, _activePlayer.Ball.Position, _swing.AimDirection, IsPutting(_activePlayer));
                }
            }
        }

        private void TickSwing()
        {
            _swing.Tick();

            if (_activePlayer == null)
            {
                return;
            }

            Vector3 ballPosition = _activePlayer.Ball.Position;

            if (!_overhead)
            {
                _camera.UpdateAim(ballPosition, _swing.AimDirection);
            }

            _hud.UpdateMeter(_swing.Stage, _swing.MeterValue, _swing.LockedPower);
            _hud.SetClub(_swing.CurrentClub, _swing.EstimatedDistance(1f));
            _hud.SetDistanceToPin(DistanceToCup(ballPosition));
            _hud.SetWind(_wind, _swing.AimDirection);

            // On the green the player wants the slope, everywhere else they want
            // to know what the ball will do in the air.
            bool putting = IsPutting(_activePlayer);
            if (putting)
            {
                GreenRead read = GreenRead.Sample(_hole.Terrain, ballPosition, _hole.CupWorldPosition);
                _hud.SetPuttRead(read.Describe());
                DrawSlopeArrow(ballPosition, read);
            }
            else
            {
                _hud.SetShotShape(_swing.SpinLabel);
                _slopeArrow.enabled = false;
            }

            float previewLength = Mathf.Min(_swing.EstimatedDistance(1f), 60f);
            _aimLine.enabled = _swing.Stage == SwingStage.Ready;
            _aimLine.SetPosition(0, ballPosition + Vector3.up * 0.05f);
            _aimLine.SetPosition(1, ballPosition + Vector3.up * 0.05f + _swing.AimDirection * previewLength);
        }

        /// <summary>
        /// Lays a short arrow on the green pointing the way the ball will drift,
        /// so the read is something the player can see rather than just read.
        /// </summary>
        private void DrawSlopeArrow(Vector3 ballPosition, GreenRead read)
        {
            Vector3 toCup = _hole.CupWorldPosition - ballPosition;
            toCup.y = 0f;
            if (toCup.sqrMagnitude < 0.0001f)
            {
                _slopeArrow.enabled = false;
                return;
            }

            Vector3 forward = toCup.normalized;
            Vector3 drift = read.DownhillDirection(forward, Vector3.Cross(Vector3.up, forward));
            if (drift == Vector3.zero || _swing.Stage != SwingStage.Ready)
            {
                _slopeArrow.enabled = false;
                return;
            }

            // Sits just off the ball so it never hides it, and floats a touch above
            // the turf to avoid z-fighting with the green.
            Vector3 side = Vector3.Cross(Vector3.up, drift);
            Vector3 start = ballPosition + Vector3.up * 0.02f + drift * 0.35f;
            Vector3 tip = start + drift * 1.5f;

            _slopeArrow.enabled = true;
            _slopeArrow.SetPosition(0, start);
            _slopeArrow.SetPosition(1, tip);
            _slopeArrow.SetPosition(2, tip - drift * 0.35f + side * 0.22f);
            _slopeArrow.SetPosition(3, tip);
            _slopeArrow.SetPosition(4, tip - drift * 0.35f - side * 0.22f);
        }

        private void BeginNextTurn()
        {
            _activePlayer = SelectNextPlayer();

            if (_activePlayer == null)
            {
                CompleteHole();
                return;
            }

            SurfaceType lie = _hole.Terrain.SampleSurface(
                new Vector2(_activePlayer.Ball.Position.x, _activePlayer.Ball.Position.z));

            _swing.BeginTurn(_activePlayer, _activePlayer.Ball.Position, _hole.CupWorldPosition, lie);

            _overhead = false;
            _camera.SetAim(_activePlayer.Ball.transform, _activePlayer.Ball.Position, _swing.AimDirection, IsPutting(_activePlayer));

            _hud.SetPlayer($"{_activePlayer.Name}  ·  stroke {_activePlayer.StrokesThisHole + 1}", GameHUD.PlayerUIColor(_activePlayer.Index));
            _hud.SetLie(lie);
            _hud.SetMeterVisible(true);
            _hud.UpdateLeaderboard(_players, _course);

            _phase = GamePhase.Aiming;
        }

        /// <summary>Golf order: whoever lies furthest from the cup plays next.</summary>
        private GolfPlayer SelectNextPlayer()
        {
            GolfPlayer furthest = null;
            float best = -1f;

            foreach (GolfPlayer player in _players)
            {
                if (player.HoledOut)
                {
                    continue;
                }

                float distance = DistanceToCup(player.Ball.Position);
                if (distance > best)
                {
                    best = distance;
                    furthest = player;
                }
            }

            return furthest;
        }

        private void OnShotTaken(ShotParameters shot)
        {
            if (_activePlayer == null)
            {
                return;
            }

            _activePlayer.StrokesThisHole++;
            _activePlayer.Ball.Launch(shot.Velocity, shot.Spin);
            _audio.PlayStrike(shot.Power, shot.Club.IsPutter);

            _aimLine.enabled = false;
            _slopeArrow.enabled = false;
            _hud.SetMeterVisible(false);
            _hud.ShowMessage(_swing.AccuracyLabel(shot.AccuracyOffset), 1.1f);

            _overhead = false;
            _camera.SetFlight(_activePlayer.Ball.transform);

            _phase = GamePhase.BallInMotion;
        }

        private void OnBallCameToRest(int playerIndex, BallRestInfo info)
        {
            if (_phase != GamePhase.BallInMotion || _activePlayer == null || _activePlayer.Index != playerIndex)
            {
                return;
            }

            HoleDefinition definition = _course.Holes[_holeIndex];

            if (info.Holed)
            {
                _activePlayer.HoledOut = true;
                _activePlayer.CompleteHole(_holeIndex);
                _hud.ShowMessage(
                    $"{_activePlayer.Name}: {GolfPlayer.ScoreName(_activePlayer.StrokesThisHole, definition.Par)}", 2.2f);
            }
            else if (info.Penalty)
            {
                _activePlayer.StrokesThisHole += Mathf.RoundToInt(PenaltyStrokes);
                _activePlayer.Ball.PlaceAt(_activePlayer.LastSafePosition, onTee: false);
                _hud.ShowMessage($"{info.PenaltyReason} — penalty stroke", 2f);
            }
            else
            {
                _activePlayer.LastSafePosition = info.Position;
            }

            // Prevent a stuck player from stalling the round.
            if (!_activePlayer.HoledOut && _activePlayer.StrokesThisHole >= definition.Par + StrokeLimitOverPar)
            {
                _activePlayer.HoledOut = true;
                _activePlayer.CompleteHole(_holeIndex);
                _hud.ShowMessage($"{_activePlayer.Name} picks up", 2f);
            }

            _hud.UpdateLeaderboard(_players, _course);
            Invoke(nameof(BeginNextTurn), 1.1f);
        }

        private void CompleteHole()
        {
            _phase = GamePhase.HoleComplete;
            _phaseTimer = ResultPauseSeconds;

            _hud.SetMeterVisible(false);
            _aimLine.enabled = false;
            _hud.RefreshScorecard(_players, _course);

            GolfPlayer best = null;
            foreach (GolfPlayer player in _players)
            {
                if (best == null || player.Strokes[_holeIndex] < best.Strokes[_holeIndex])
                {
                    best = player;
                }
            }

            if (best != null && _players.Count > 1)
            {
                _hud.ShowMessage($"{best.Name} wins the hole with {best.Strokes[_holeIndex]}", ResultPauseSeconds);
            }
        }

        private void AdvanceHole()
        {
            _holeIndex++;

            if (_holeIndex >= _course.Holes.Count)
            {
                _phase = GamePhase.RoundComplete;
                _hud.SetMeterVisible(false);
                _hud.ShowResults(_players, _course);
                return;
            }

            LoadHole(_holeIndex);
        }

        private bool IsPutting(GolfPlayer player) =>
            _hole.Terrain.SampleSurface(new Vector2(player.Ball.Position.x, player.Ball.Position.z)) == SurfaceType.Green;

        private float DistanceToCup(Vector3 position)
        {
            Vector3 delta = position - _hole.CupWorldPosition;
            delta.y = 0f;
            return delta.magnitude;
        }
    }
}
