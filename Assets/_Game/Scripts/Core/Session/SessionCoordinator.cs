using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Input;
using Puttshack.Protocol;
using Puttshack.Rules;
using Puttshack.Simulation;
using Puttshack.Storage;
using Puttshack.Time;

namespace Puttshack.Session
{
    /// <summary>
    /// The single owner of the active session (spec sections 4, 8, 13, 39). Every accepted transition is applied to a
    /// draft, durably committed, and only then becomes authoritative. Invalid commands leave state untouched.
    /// </summary>
    public sealed class SessionCoordinator
    {
        public const double HeartbeatTimeoutMs = 1500;
        public const int MaxPlayers = 6;
        public const int MaxNameLength = 16;

        private static readonly string[] DefaultNames = { "Caleb", "Maya", "Jordan", "Sam", "Nia", "Leo" };

        /// <summary>The first default name not in <paramref name="taken"/>, for a player added to the roster.</summary>
        public static string DefaultName(ICollection<string> taken)
        {
            foreach (string n in DefaultNames)
            {
                if (!taken.Contains(n)) return n;
            }
            return $"Player {taken.Count + 1}";
        }

        private readonly CourseDefinition _course;
        private readonly ISessionStore _store;
        private readonly IMonotonicClock _clock;
        private readonly IInputResolver _resolver;
        private readonly Dictionary<int, ShotRecord> _shots;
        private readonly List<string> _log = new List<string>();

        private SessionData _data;
        private CabinetHealth _health;
        private SessionSnapshot _snapshot;
        private ICollisionWorld _world;
        private int _worldHoleIndex = -1;
        private string _previewKey;
        private ShotResult _preview;

        public CrashPoint CrashAfter { get; set; }
        public IReadOnlyList<string> Log => _log;
        public bool Recovered { get; private set; }

        private SessionCoordinator(CourseDefinition course, ISessionStore store, IMonotonicClock clock, IInputResolver resolver,
            SessionData data, Dictionary<int, ShotRecord> shots)
        {
            _course = course;
            _store = store;
            _clock = clock;
            _resolver = resolver ?? new MeterPowerResolver();
            _data = data;
            _shots = shots;
        }

        /// <summary>Loads the latest compatible session and reconciles it, or creates a fresh one in ATTRACT.</summary>
        public static SessionCoordinator StartOrResume(CourseDefinition course, ISessionStore store, IMonotonicClock clock, IInputResolver resolver = null)
        {
            StoredSession stored = store.LoadLatest();
            bool resumable = stored != null
                && stored.Session.CourseContentHash == course.ContentHash
                && stored.Session.State != SessionState.ROUND_COMPLETE
                && stored.Session.State != SessionState.ATTRACT;

            if (!resumable)
            {
                var fresh = new SessionCoordinator(course, store, clock, resolver, NewSessionData(store.NextSessionId(), course), new Dictionary<int, ShotRecord>());
                fresh.CommitInitial();
                return fresh;
            }

            var coordinator = new SessionCoordinator(course, store, clock, resolver, stored.Session, stored.Shots);
            coordinator.Reconcile();
            return coordinator;
        }

        public SessionSnapshot Snapshot
        {
            get
            {
                bool ready = CabinetReady(out string status);
                if (_snapshot == null || _snapshot.CabinetReady != ready || _snapshot.CabinetStatus != status)
                {
                    SessionData copy = JournalSerializer.Clone(_data);
                    _snapshot = new SessionSnapshot(copy, _course, new Dictionary<int, ShotRecord>(_shots), ready, status);
                }
                return _snapshot;
            }
        }

        public long Revision => _data.Revision;
        public SessionState State => _data.State;

        public ICollisionWorld World
        {
            get
            {
                if (_worldHoleIndex != _data.HoleIndex || _world == null)
                {
                    _world = new AnalyticWorld(_course.Holes[Math.Min(_data.HoleIndex, _course.Holes.Count - 1)]);
                    _worldHoleIndex = _data.HoleIndex;
                }
                return _world;
            }
        }

        // ---------------------------------------------------------------- Cabinet

        public void UpdateCabinetHealth(CabinetHealth health)
        {
            bool before = CabinetReady(out _);
            _health = health;
            if (before != CabinetReady(out _)) _snapshot = null;
        }

        public bool CabinetReady(out string status)
        {
            CabinetCapabilities caps = _health.Capabilities;
            if (!_health.Connected) { status = "Input connection lost - your score is saved"; return false; }
            if (caps == null) { status = "Waiting for cabinet"; return false; }
            if (caps.ProtocolVersion != Versions.Protocol) { status = "Cabinet protocol mismatch - ask staff"; return false; }
            if (!caps.TargetHoleDetection || caps.TargetHoleCount != LaneMapper.LaneCount) { status = "Target holes unavailable - ask staff"; return false; }
            if (!caps.IsCommissioned) { status = "Cabinet not commissioned - ask staff"; return false; }
            if (_clock.NowMs - _health.LastHeartbeatMs > HeartbeatTimeoutMs) { status = "Cabinet not responding - please wait"; return false; }
            status = "Cabinet ready";
            return true;
        }

        /// <summary>
        /// Accept the first valid event of the armed interval: validate, reserve durably, then resolve. Repeated delivery
        /// of an accepted identity returns the same acknowledgement.
        /// </summary>
        public InputAck HandleCabinetEvent(CabinetEvent evt)
        {
            string key = evt.Key;
            if (_data.ConsumedEvents.TryGetValue(key, out int existingShot))
            {
                ShotRecord existing = _shots[existingShot];
                return new InputAck { Accepted = true, Duplicate = true, ShotId = existingShot, EventKey = key, Lane = existing.Intent.Lane, Power01 = existing.Intent.Power01 };
            }

            if (_data.State != SessionState.SHOT_ARMED)
                return RejectInput(evt, InputRejection.NotArmed);

            InputRejection rejection = _resolver.Validate(evt, _data.Arm, _clock.NowMs);
            if (rejection != InputRejection.None)
                return RejectInput(evt, rejection);

            HoleDefinition hole = CurrentHole;
            HoleProgress progress = _data.CurrentProgress;
            int shotId = _data.NextShotId;
            ShotIntent intent = _resolver.Resolve(evt, _data.Arm, new ResolveContext
            {
                SessionId = _data.SessionId,
                SessionRevision = _data.Revision,
                ShotId = shotId,
                HoleId = hole.Id,
                HoleVersion = hole.Version,
                WindMps = hole.WindMps,
                AssistanceProfile = _data.Settings.Assistance,
            });

            var reserved = new ShotRecord
            {
                ShotId = shotId,
                PlayerId = intent.PlayerId,
                HoleIndex = _data.HoleIndex,
                TurnId = intent.TurnId,
                StrokeNumber = ScoreEngine.NextStrokeNumber(progress),
                Status = ShotStatus.RESERVED,
                EventKey = key,
                Intent = intent,
            };

            bool committed = TryMutate("shot.reserve", d =>
            {
                d.ConsumedEvents[key] = shotId;
                d.NextShotId = shotId + 1;
                d.Arm = null;
                d.PendingShotId = shotId;
                d.State = SessionState.SHOT_RESERVED;
            }, reserved);

            if (!committed)
            {
                TryMutate("shot.disarm.storage", d =>
                {
                    d.Arm = null;
                    d.State = SessionState.SHOT_SETUP;
                    d.FaultReason = "Storage unavailable - no stroke was counted. Press Ready to try again.";
                });
                return new InputAck { Accepted = false, Rejection = InputRejection.StorageFailure, EventKey = key, Lane = evt.LogicalLane };
            }

            _log.Add($"reserve shot {shotId} lane {intent.Lane} power {intent.Power01:0.000} ({key})");
            if (CrashAfter == CrashPoint.AfterReservation) throw new SimulatedCrashException(CrashPoint.AfterReservation);

            ResolvePending();
            return new InputAck { Accepted = true, ShotId = shotId, EventKey = key, Lane = intent.Lane, Power01 = intent.Power01 };
        }

        private InputAck RejectInput(CabinetEvent evt, InputRejection rejection)
        {
            _log.Add($"input rejected {rejection} lane {evt.LogicalLane} ({evt.Key})");
            return new InputAck { Accepted = false, Rejection = rejection, EventKey = evt.Key, Lane = evt.LogicalLane };
        }

        /// <summary>Advances time-driven transitions: arming handshake, health-based disarm, and pending resolution.</summary>
        public void Tick()
        {
            bool ready = CabinetReady(out string status);
            switch (_data.State)
            {
                case SessionState.ARMING:
                    if (ready) Arm();
                    break;
                case SessionState.SHOT_ARMED:
                    if (!ready || _data.Arm == null || _data.Arm.BootId != _health.Capabilities?.BootId)
                    {
                        TryMutate("shot.disarm.cabinet", d =>
                        {
                            d.Arm = null;
                            d.State = SessionState.SHOT_SETUP;
                            d.FaultReason = ready ? "Cabinet restarted - press Ready again" : status;
                        });
                    }
                    break;
                case SessionState.SHOT_RESERVED:
                    ResolvePending();
                    break;
            }
        }

        private void Arm()
        {
            SessionData d0 = _data;
            ClubDefinition club = ClubCatalog.Get(d0.Setup.ClubId);
            AssistanceProfile profile = AssistanceProfiles.Get(d0.Settings.Assistance);
            HoleProgress progress = d0.CurrentProgress;
            Vec3d origin = progress.Ball.Position;
            Vec3d aim = (d0.Setup.AimTargetM - origin).Horizontal.Normalized;
            CabinetCapabilities caps = _health.Capabilities;
            double now = _clock.NowMs;

            TryMutate("shot.armed", d =>
            {
                d.ArmSequence++;
                d.FaultReason = null;
                d.Arm = new ArmState
                {
                    Token = $"{d.SessionId}-{d.TurnId}-{d.ArmSequence}",
                    EpochMs = now,
                    SweepSeconds = MeterWaveform.SweepSeconds(club, profile),
                    DeviceId = caps.DeviceId,
                    BootId = caps.BootId,
                    CalibrationVersion = caps.CalibrationVersion,
                    TurnId = d.TurnId,
                    PlayerId = d.CurrentPlayer.Id,
                    ClubId = club.Id,
                    ShotType = club.ShotType,
                    PuttScale = d.Setup.PuttScale,
                    OriginM = origin,
                    AimForward = aim,
                    Lie = progress.Ball.Lie,
                    FanHalfAngleRad = Units.DegToRad(club.FanHalfAngleDeg),
                };
                d.State = SessionState.SHOT_ARMED;
            });
        }

        /// <summary>Resolves the reserved intent exactly once. Never asks the player to hit again.</summary>
        private void ResolvePending()
        {
            if (_data.State != SessionState.SHOT_RESERVED || !_data.PendingShotId.HasValue) return;
            ShotRecord reserved = _shots[_data.PendingShotId.Value];
            ShotIntent intent = reserved.Intent;
            HoleDefinition hole = CurrentHole;

            if (intent.SimulationVersion != Versions.Simulation || intent.RulesVersion != Versions.Rules || intent.HoleVersion != hole.Version)
            {
                EnterFault($"Shot {reserved.ShotId} needs solver/content version {intent.SimulationVersion}/{intent.HoleVersion}. Ask staff.");
                return;
            }

            ShotResult result;
            try
            {
                result = ShotSolver.Solve(intent, World);
            }
            catch (Exception ex)
            {
                EnterFault($"Shot recovery in progress - do not play again. ({ex.GetType().Name})");
                return;
            }

            PenaltyRecord penalty = PenaltyRules.Evaluate(intent, result, World);
            var resolved = JournalSerializer.Clone(reserved);
            resolved.Status = ShotStatus.RESOLVED;
            resolved.Result = result;
            resolved.Penalty = penalty;

            bool committed = TryMutate("shot.resolve", d =>
            {
                HoleProgress progress = FindProgress(d, intent.PlayerId, reserved.HoleIndex);
                progress.Played++;
                if (penalty != null) progress.Penalties += penalty.Strokes;

                var ball = new BallState { LastOrigin = intent.OriginM, LastOriginLie = intent.Lie };
                if (result.Outcome == ShotOutcome.Holed)
                {
                    progress.Status = HoleStatus.HOLED;
                    progress.CompletionScore = progress.StrokesSoFar;
                    progress.StatusReason = "Holed";
                    ball.Position = result.FinalPosition;
                    ball.Lie = SurfaceId.Green;
                }
                else if (penalty != null)
                {
                    ball.Position = penalty.ReliefPosition;
                    ball.Lie = penalty.ReliefLie;
                }
                else if (result.Outcome == ShotOutcome.SolverFault)
                {
                    ball.Position = intent.OriginM;
                    ball.Lie = intent.Lie;
                }
                else
                {
                    ball.Position = result.FinalPosition;
                    ball.Lie = result.FinalLie;
                }
                progress.Ball = ball;

                int par = CurrentHole.Par;
                if (PaceCap.Reached(progress, par, d.Settings)) PaceCap.ApplyCap(progress, par, d.Settings);

                resolved.HoleStatusAfter = progress.Status;
                resolved.ScoreSoFarAfter = progress.Status == HoleStatus.CAPPED ? progress.CompletionScore.Value : progress.StrokesSoFar;
                d.PendingShotId = null;
                d.LastShotId = reserved.ShotId;
                d.State = SessionState.SHOT_RESOLVED;
            }, resolved);

            if (!committed)
            {
                EnterFault("Shot recovery in progress - do not play again. (storage)");
                return;
            }

            _log.Add($"resolve shot {reserved.ShotId}: {result.Outcome} carry {result.CarryM:0.0} m total {result.TotalM:0.0} m");
            if (CrashAfter == CrashPoint.AfterResolution) throw new SimulatedCrashException(CrashPoint.AfterResolution);
        }

        // ---------------------------------------------------------------- Commands

        public CommandReply Apply(SessionCommand cmd)
        {
            if (cmd == null || string.IsNullOrEmpty(cmd.CommandId))
                return CommandReply.Reject(RejectReason.INVALID_VALUE, _data.Revision, "Missing command id.");

            string fingerprint = ContentHasher.Canonical(new { cmd.Type, cmd.Names, cmd.Assistance, cmd.PaceCapEnabled, cmd.ClubId, cmd.AimYawDeltaRad, cmd.AimTargetM, cmd.PuttScale, cmd.PlayerId, cmd.Loadout, cmd.ExpectedRevision });
            // Appended only when present so fingerprints already in older journals still match their redeliveries.
            if (cmd.Profiles != null) fingerprint += "|" + ContentHasher.Canonical(cmd.Profiles);
            if (_data.AppliedCommands.TryGetValue(cmd.CommandId, out string previous))
            {
                return previous == fingerprint
                    ? CommandReply.Ok(_data.Revision, duplicate: true)
                    : CommandReply.Reject(RejectReason.COMMAND_ID_CONFLICT, _data.Revision, "Command id reused with a different payload.");
            }

            if (cmd.ExpectedRevision != _data.Revision)
                return CommandReply.Reject(RejectReason.STALE_REVISION, _data.Revision, $"Expected r{cmd.ExpectedRevision}, session is r{_data.Revision}.");

            CommandReply reply = Dispatch(cmd, fingerprint);
            return reply;
        }

        private CommandReply Dispatch(SessionCommand cmd, string fingerprint)
        {
            SessionState s = _data.State;
            switch (cmd.Type)
            {
                case CommandType.Start:
                    if (s != SessionState.ATTRACT) return WrongState(cmd);
                    return Commit(cmd, fingerprint, d =>
                    {
                        d.State = SessionState.LOBBY;
                        d.Players = BuildRoster(new List<string> { DefaultNames[0], DefaultNames[1] });
                    });

                case CommandType.RosterSet:
                {
                    if (s != SessionState.LOBBY) return WrongState(cmd);
                    if (cmd.Names == null || cmd.Names.Count < 1 || cmd.Names.Count > MaxPlayers)
                        return Invalid("Roster needs 1 to 6 players.");
                    var names = new List<string>();
                    foreach (string raw in cmd.Names)
                    {
                        string name = (raw ?? string.Empty).Trim();
                        if (name.Length == 0 || name.Length > MaxNameLength) return Invalid("Names must be 1-16 characters.");
                        names.Add(name);
                    }
                    if (cmd.Profiles != null)
                    {
                        if (cmd.Profiles.Count != names.Count) return Invalid("Send one profile per player.");
                        foreach (PlayerProfile profile in cmd.Profiles)
                        {
                            string problem = profile != null ? ProfileCatalog.Validate(profile) : null;
                            if (problem != null) return Invalid(problem);
                        }
                    }
                    return Commit(cmd, fingerprint, d => d.Players = BuildRoster(names, d.Players, cmd.Profiles));
                }

                case CommandType.SetLoadout:
                {
                    if (s != SessionState.LOBBY && s != SessionState.PAUSED) return WrongState(cmd);
                    int roster = cmd.PlayerId.HasValue ? _data.Players.FindIndex(p => p.Id == cmd.PlayerId.Value) : -1;
                    if (roster < 0) return Invalid("Unknown player.");
                    string problem = CosmeticCatalog.Validate(cmd.Loadout);
                    if (problem != null) return Invalid(problem);
                    Loadout saved = cmd.Loadout.Clone();
                    return Commit(cmd, fingerprint, d => d.Players[roster].Loadout = saved);
                }

                case CommandType.SettingsConfirm:
                {
                    if (s != SessionState.LOBBY) return WrongState(cmd);
                    if (_data.Players.Count == 0) return Invalid("Add at least one player.");
                    List<string> errors = CourseValidator.Validate(_course);
                    if (errors.Count > 0) return Invalid(string.Join(" ", errors));
                    return Commit(cmd, fingerprint, d =>
                    {
                        if (cmd.Assistance.HasValue) d.Settings.Assistance = cmd.Assistance.Value;
                        if (cmd.PaceCapEnabled.HasValue) d.Settings.PaceCapEnabled = cmd.PaceCapEnabled.Value;
                        d.SettingsFrozen = true;
                        d.CourseId = _course.Id;
                        d.CourseVersion = _course.Version;
                        d.CourseContentHash = _course.ContentHash;
                        d.HoleCount = _course.Holes.Count;
                        d.Progress = new List<HoleProgress>();
                        foreach (PlayerState p in d.Players)
                        {
                            for (int h = 0; h < d.HoleCount; h++)
                                d.Progress.Add(new HoleProgress { PlayerId = p.Id, HoleIndex = h, Status = HoleStatus.NOT_STARTED });
                        }
                        d.HoleIndex = 0;
                        d.State = SessionState.HOLE_LOAD;
                    });
                }

                case CommandType.HoleReady:
                {
                    if (s != SessionState.HOLE_LOAD) return WrongState(cmd);
                    HoleDefinition hole = CurrentHole;
                    List<string> errors = CourseValidator.ValidateHole(hole);
                    if (errors.Count > 0) return Invalid(string.Join(" ", errors));
                    ICollisionWorld world = World;
                    var tee = new Vec3d(hole.TeeM.x, world.GroundHeight(hole.TeeM.x, hole.TeeM.z), hole.TeeM.z);
                    SurfaceId teeLie = world.SurfaceAt(tee.x, tee.z);
                    return Commit(cmd, fingerprint, d =>
                    {
                        foreach (PlayerState p in d.Players)
                        {
                            HoleProgress progress = d.ProgressFor(p.RosterIndex, d.HoleIndex);
                            if (p.Withdrawn)
                            {
                                progress.Status = HoleStatus.WITHDRAWN;
                                continue;
                            }
                            progress.Status = HoleStatus.ACTIVE;
                            progress.Played = 0;
                            progress.Penalties = 0;
                            progress.CompletionScore = null;
                            progress.Ball = new BallState { Position = tee, Lie = teeLie, LastOrigin = tee, LastOriginLie = teeLie };
                        }
                        d.State = SessionState.BRIEFING;
                    });
                }

                case CommandType.BriefingReady:
                {
                    if (s != SessionState.BRIEFING) return WrongState(cmd);
                    int first = TurnScheduler.FirstActive(Statuses(_data));
                    if (first == TurnScheduler.None) return Invalid("No active players.");
                    return Commit(cmd, fingerprint, d =>
                    {
                        d.CurrentRosterIndex = first;
                        BeginTurn(d);
                    });
                }

                case CommandType.SelectClub:
                    if (s != SessionState.SHOT_SETUP) return WrongState(cmd);
                    if (cmd.ClubId == null || !ClubCatalog.TryGet(cmd.ClubId, out ClubDefinition club)) return Invalid("Unknown club.");
                    return Commit(cmd, fingerprint, d =>
                    {
                        d.Setup.ClubId = club.Id;
                        d.FaultReason = null;
                    });

                case CommandType.SetAim:
                {
                    if (s != SessionState.SHOT_SETUP) return WrongState(cmd);
                    Vec3d ball = _data.CurrentProgress.Ball.Position;
                    Vec3d target;
                    if (cmd.AimTargetM.HasValue) target = cmd.AimTargetM.Value;
                    else if (cmd.AimYawDeltaRad.HasValue && !double.IsNaN(cmd.AimYawDeltaRad.Value) && Math.Abs(cmd.AimYawDeltaRad.Value) <= Math.PI)
                        target = ball + (_data.Setup.AimTargetM - ball).Horizontal.RotateYaw(cmd.AimYawDeltaRad.Value);
                    else return Invalid("Aim needs a target or a bounded yaw delta.");
                    if (!target.IsFinite || Vec3d.HorizontalDistance(target, ball) < 0.5) return Invalid("Aim target must be finite and away from the ball.");
                    return Commit(cmd, fingerprint, d => d.Setup.AimTargetM = target);
                }

                case CommandType.SetPuttScale:
                    if (s != SessionState.SHOT_SETUP) return WrongState(cmd);
                    if (!cmd.PuttScale.HasValue) return Invalid("Missing putt scale.");
                    return Commit(cmd, fingerprint, d => d.Setup.PuttScale = cmd.PuttScale.Value);

                case CommandType.Arm:
                    if (s != SessionState.SHOT_SETUP) return WrongState(cmd);
                    if (!CabinetReady(out string status)) return CommandReply.Reject(RejectReason.DEVICE_NOT_READY, _data.Revision, status);
                    return Commit(cmd, fingerprint, d =>
                    {
                        d.State = SessionState.ARMING;
                        d.FaultReason = null;
                    });

                case CommandType.Disarm:
                    if (s != SessionState.ARMING && s != SessionState.SHOT_ARMED) return WrongState(cmd);
                    return Commit(cmd, fingerprint, d =>
                    {
                        d.Arm = null;
                        d.State = SessionState.SHOT_SETUP;
                    });

                case CommandType.PresentationBegin:
                    if (s != SessionState.SHOT_RESOLVED) return WrongState(cmd);
                    return Commit(cmd, fingerprint, d => d.State = SessionState.SHOT_PRESENTING);

                case CommandType.PresentationSkip:
                case CommandType.PresentationEnd:
                    if (s != SessionState.SHOT_PRESENTING && s != SessionState.SHOT_RESOLVED) return WrongState(cmd);
                    return Commit(cmd, fingerprint, d => d.State = SessionState.SHOT_RESULT);

                case CommandType.ResultContinue:
                {
                    if (s != SessionState.SHOT_RESULT) return WrongState(cmd);
                    List<HoleStatus> statuses = Statuses(_data);
                    int next = TurnScheduler.NextActive(statuses, _data.CurrentRosterIndex);
                    return Commit(cmd, fingerprint, d =>
                    {
                        if (next == TurnScheduler.None)
                        {
                            d.State = SessionState.HOLE_COMPLETE;
                            return;
                        }
                        d.CurrentRosterIndex = next;
                        d.State = SessionState.HANDOFF;
                    });
                }

                case CommandType.NextPlayerReady:
                    if (s != SessionState.HANDOFF) return WrongState(cmd);
                    return Commit(cmd, fingerprint, BeginTurn);

                case CommandType.HoleContinue:
                    if (s != SessionState.HOLE_COMPLETE) return WrongState(cmd);
                    return Commit(cmd, fingerprint, d =>
                    {
                        if (d.HoleIndex + 1 < d.HoleCount)
                        {
                            d.HoleIndex++;
                            d.State = SessionState.HOLE_LOAD;
                        }
                        else
                        {
                            d.State = SessionState.ROUND_COMPLETE;
                            d.Completion = RoundCompletion.Complete;
                        }
                    });

                case CommandType.Pause:
                    if (!IsInRound(s) || s == SessionState.SHOT_RESERVED) return WrongState(cmd);
                    return Commit(cmd, fingerprint, d =>
                    {
                        d.ResumeState = PauseTarget(s);
                        d.Arm = null;
                        d.State = SessionState.PAUSED;
                    });

                case CommandType.Resume:
                    if (s != SessionState.PAUSED && s != SessionState.FAULT) return WrongState(cmd);
                {
                    CommandReply reply = Commit(cmd, fingerprint, d =>
                    {
                        d.State = d.ResumeState;
                        d.FaultReason = null;
                    });
                    if (reply.Accepted && _data.State == SessionState.SHOT_RESERVED) ResolvePending();
                    return reply;
                }

                case CommandType.EndRound:
                    if (!(IsInRound(s) || s == SessionState.PAUSED) || s == SessionState.SHOT_RESERVED || EffectiveReserved()) return WrongState(cmd);
                    return Commit(cmd, fingerprint, d =>
                    {
                        d.Arm = null;
                        d.State = SessionState.ROUND_COMPLETE;
                        d.Completion = AllHolesResolved(d) ? RoundCompletion.Complete : RoundCompletion.Incomplete;
                    });

                case CommandType.ReturnToAttract:
                    if (s != SessionState.ROUND_COMPLETE) return WrongState(cmd);
                    return StartNewSession();

                default:
                    return Invalid($"Unsupported command {cmd.Type}.");
            }
        }

        // ---------------------------------------------------------------- Helpers

        public HoleDefinition CurrentHole => _course.Holes[Math.Min(_data.HoleIndex, _course.Holes.Count - 1)];

        /// <summary>Preview at the declared preview power through the same solver and world as the accepted shot.</summary>
        public ShotResult Preview(int lane = 10)
        {
            if (_data.State != SessionState.SHOT_SETUP && _data.State != SessionState.ARMING && _data.State != SessionState.SHOT_ARMED) return null;
            HoleProgress progress = _data.CurrentProgress;
            if (progress?.Ball == null || _data.Setup.ClubId == null) return null;

            string key = $"{_data.SessionId}|{_data.HoleIndex}|{progress.Ball.Position}|{_data.Setup.ClubId}|{_data.Setup.AimTargetM}|{_data.Setup.PuttScale}|{lane}|{_data.Settings.PreviewPower}";
            if (key == _previewKey) return _preview;

            ClubDefinition club = ClubCatalog.Get(_data.Setup.ClubId);
            AssistanceProfile profile = AssistanceProfiles.Get(_data.Settings.Assistance);
            double rawYaw = LaneMapper.RawYawRad(lane, Units.DegToRad(club.FanHalfAngleDeg));
            var intent = new ShotIntent
            {
                ClubId = club.Id,
                ShotType = club.ShotType,
                PuttScale = _data.Setup.PuttScale,
                OriginM = progress.Ball.Position,
                AimForward = (_data.Setup.AimTargetM - progress.Ball.Position).Horizontal.Normalized,
                Lie = progress.Ball.Lie,
                WindMps = CurrentHole.WindMps,
                Power01 = _data.Settings.PreviewPower,
                RawYawRad = rawYaw,
                YawRad = Assistance.ApplyYaw(rawYaw, 0, profile),
            };
            _preview = ShotSolver.Solve(intent, World);
            _previewKey = key;
            return _preview;
        }

        private void BeginTurn(SessionData d)
        {
            HoleProgress progress = d.ProgressFor(d.CurrentRosterIndex, d.HoleIndex);
            HoleDefinition hole = _course.Holes[d.HoleIndex];
            Vec3d ball = progress.Ball.Position;
            double remaining = Vec3d.HorizontalDistance(ball, hole.CupM);
            bool onTee = progress.Played == 0 && progress.Penalties == 0;

            d.TurnId = d.NextTurnId++;
            d.Setup = new SetupState
            {
                ClubId = onTee && hole.Briefing.SuggestedClubId != null ? hole.Briefing.SuggestedClubId : ClubCatalog.Suggest(remaining, progress.Ball.Lie, onTee),
                AimTargetM = hole.CupM,
                PuttScale = SuggestPuttScale(remaining),
            };
            d.Arm = null;
            d.State = SessionState.SHOT_SETUP;
        }

        private static PuttScale SuggestPuttScale(double remainingM)
        {
            double feet = Units.MetersToFeet(remainingM);
            if (feet <= 12) return PuttScale.Short;
            if (feet <= 34) return PuttScale.Medium;
            return PuttScale.Long;
        }

        private static SessionState PauseTarget(SessionState s)
        {
            switch (s)
            {
                case SessionState.ARMING:
                case SessionState.SHOT_ARMED:
                    return SessionState.SHOT_SETUP;
                case SessionState.SHOT_PRESENTING:
                    return SessionState.SHOT_RESOLVED;
                default:
                    return s;
            }
        }

        private bool EffectiveReserved() =>
            (_data.State == SessionState.PAUSED || _data.State == SessionState.FAULT) && _data.ResumeState == SessionState.SHOT_RESERVED;

        private static bool IsInRound(SessionState s) =>
            s != SessionState.ATTRACT && s != SessionState.LOBBY && s != SessionState.ROUND_COMPLETE &&
            s != SessionState.PAUSED && s != SessionState.FAULT;

        private static bool AllHolesResolved(SessionData d)
        {
            foreach (HoleProgress p in d.Progress)
            {
                if (!p.IsComplete && p.Status != HoleStatus.WITHDRAWN) return false;
            }
            return d.Progress.Count > 0;
        }

        private static List<HoleStatus> Statuses(SessionData d)
        {
            var statuses = new List<HoleStatus>(d.Players.Count);
            foreach (PlayerState p in d.Players) statuses.Add(d.ProgressFor(p.RosterIndex, d.HoleIndex).Status);
            return statuses;
        }

        private static HoleProgress FindProgress(SessionData d, int playerId, int holeIndex)
        {
            foreach (PlayerState p in d.Players)
            {
                if (p.Id == playerId) return d.ProgressFor(p.RosterIndex, holeIndex);
            }
            throw new InvalidOperationException($"Unknown player {playerId}.");
        }

        /// <summary>
        /// Roster slots keep their identity colour, any loadout already saved in that slot and its golfer profile, unless
        /// <paramref name="profiles"/> names a new one for the slot.
        /// </summary>
        private static List<PlayerState> BuildRoster(List<string> names, List<PlayerState> previous = null, List<PlayerProfile> profiles = null)
        {
            var roster = new List<PlayerState>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                PlayerState before = previous != null && i < previous.Count ? previous[i] : null;
                PlayerProfile profile = profiles != null && i < profiles.Count ? profiles[i] : null;
                roster.Add(new PlayerState
                {
                    Id = i + 1,
                    RosterIndex = i,
                    Name = names[i],
                    ColorIndex = i,
                    Loadout = before?.Loadout?.Clone() ?? CosmeticCatalog.DefaultFor(i),
                    Profile = profile?.Clone() ?? before?.Profile?.Clone() ?? ProfileCatalog.DefaultFor(i),
                });
            }
            return roster;
        }

        private static SessionData NewSessionData(int sessionId, CourseDefinition course) => new SessionData
        {
            SessionId = sessionId,
            CourseId = course.Id,
            CourseVersion = course.Version,
            CourseContentHash = course.ContentHash,
            HoleCount = 0,
        };

        private CommandReply StartNewSession()
        {
            SessionData fresh = NewSessionData(_store.NextSessionId(), _course);
            try
            {
                fresh.Revision = 1;
                _store.Commit(new JournalEntry { SessionId = fresh.SessionId, Revision = fresh.Revision, Kind = "session.create", MonotonicMs = _clock.NowMs, Session = fresh });
            }
            catch (StoreException ex)
            {
                return CommandReply.Reject(RejectReason.STORAGE_FAILURE, _data.Revision, ex.Message);
            }
            _data = fresh;
            _shots.Clear();
            _snapshot = null;
            _previewKey = null;
            return CommandReply.Ok(_data.Revision);
        }

        private void CommitInitial()
        {
            _data.Revision = 1;
            try
            {
                _store.Commit(new JournalEntry { SessionId = _data.SessionId, Revision = _data.Revision, Kind = "session.create", MonotonicMs = _clock.NowMs, Session = _data });
            }
            catch (StoreException)
            {
                _data.State = SessionState.FAULT;
                _data.FaultReason = "Storage unavailable - ask staff.";
            }
        }

        /// <summary>Startup reconciliation (spec section 39). Never restores an armed timing challenge.</summary>
        private void Reconcile()
        {
            Recovered = true;
            string integrity = CheckIntegrity();
            if (integrity != null)
            {
                EnterFault("Score check failed - ask staff. " + integrity);
                return;
            }

            SessionState s = _data.State;
            SessionState resume = _data.ResumeState;
            bool paused = s == SessionState.PAUSED || s == SessionState.FAULT;
            SessionState effective = paused ? resume : s;
            SessionState target = effective;
            switch (effective)
            {
                case SessionState.ARMING:
                case SessionState.SHOT_ARMED:
                    target = SessionState.SHOT_SETUP;
                    break;
                case SessionState.SHOT_PRESENTING:
                    target = SessionState.SHOT_RESOLVED;
                    break;
            }

            if (target != effective || _data.Arm != null || s == SessionState.FAULT)
            {
                TryMutate("session.recover", d =>
                {
                    d.Arm = null;
                    if (d.State == SessionState.FAULT)
                    {
                        d.State = target;
                        d.FaultReason = null;
                    }
                    else if (paused) d.ResumeState = target;
                    else d.State = target;
                });
            }
            _log.Add($"recovered session {_data.SessionId} at {_data.State} r{_data.Revision}");
        }

        /// <summary>Shot counts, penalties, and hole statuses must agree with the resolved shot records.</summary>
        private string CheckIntegrity()
        {
            if (_data.HoleCount == 0) return null;
            if (_data.Progress.Count != _data.Players.Count * _data.HoleCount) return "Progress table size mismatch.";
            foreach (PlayerState player in _data.Players)
            {
                for (int h = 0; h < _data.HoleCount; h++)
                {
                    HoleProgress progress = _data.ProgressFor(player.RosterIndex, h);
                    int played = 0;
                    int penalties = 0;
                    foreach (ShotRecord shot in _shots.Values)
                    {
                        if (shot.PlayerId != player.Id || shot.HoleIndex != h || shot.Status != ShotStatus.RESOLVED) continue;
                        played++;
                        penalties += shot.Penalty?.Strokes ?? 0;
                    }
                    if (played != progress.Played || penalties != progress.Penalties)
                        return $"Player {player.Id} hole {h + 1}: records {played}+{penalties}, progress {progress.Played}+{progress.Penalties}.";
                    if (progress.Ball != null && !progress.Ball.Position.IsFinite)
                        return $"Player {player.Id} hole {h + 1}: non-finite ball position.";
                }
            }
            return null;
        }

        private void EnterFault(string reason)
        {
            SessionState resume = _data.State == SessionState.FAULT ? _data.ResumeState : _data.State;
            bool ok = TryMutate("session.fault", d =>
            {
                d.ResumeState = resume;
                d.State = SessionState.FAULT;
                d.FaultReason = reason;
            });
            if (!ok)
            {
                _data.ResumeState = resume;
                _data.State = SessionState.FAULT;
                _data.FaultReason = reason;
                _snapshot = null;
            }
            _log.Add("fault: " + reason);
        }

        private CommandReply Commit(SessionCommand cmd, string fingerprint, Action<SessionData> change)
        {
            bool ok = TryMutate("cmd." + cmd.Type, d =>
            {
                change(d);
                d.AppliedCommands[cmd.CommandId] = fingerprint;
            });
            return ok
                ? CommandReply.Ok(_data.Revision)
                : CommandReply.Reject(RejectReason.STORAGE_FAILURE, _data.Revision, "Storage unavailable.");
        }

        private CommandReply WrongState(SessionCommand cmd) =>
            CommandReply.Reject(RejectReason.WRONG_STATE, _data.Revision, $"{cmd.Type} not allowed in {_data.State}.");

        private CommandReply Invalid(string detail) => CommandReply.Reject(RejectReason.INVALID_VALUE, _data.Revision, detail);

        /// <summary>Applies a change to a draft, commits it durably, then swaps it in. On failure nothing changes.</summary>
        private bool TryMutate(string kind, Action<SessionData> change, ShotRecord shot = null)
        {
            SessionData draft = JournalSerializer.Clone(_data);
            change(draft);
            draft.Revision = _data.Revision + 1;
            try
            {
                _store.Commit(new JournalEntry
                {
                    SessionId = draft.SessionId,
                    Revision = draft.Revision,
                    Kind = kind,
                    MonotonicMs = _clock.NowMs,
                    Session = draft,
                    Shot = shot,
                });
            }
            catch (StoreException ex)
            {
                _log.Add($"storage failure on {kind}: {ex.Message}");
                return false;
            }

            _data = draft;
            if (shot != null) _shots[shot.ShotId] = JournalSerializer.Clone(shot);
            _snapshot = null;
            return true;
        }
    }
}
