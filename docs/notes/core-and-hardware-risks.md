# Scratch notes: Core / Hardware risks, dead code, magic numbers

Working notes from reading `Assets/_Game/Scripts/Core/**`, `Assets/_Game/Scripts/Hardware/**` and the Runtime entry points (`GameHost`, `EmulatorPanel`, `DebugAutoPlay`, `RosterMemory`, `CourseDataResources`). Line numbers are as of 2026-10-19. A case-sensitive whole-word grep for `TODO`, `FIXME`, `HACK`, `XXX` across Core and Hardware (and `GameHost.cs`, `DebugAutoPlay.cs`) finds **no matches**. The code marks tunable values with `TUNE` instead (19 occurrences in Core).

## Highest risks

- **Journal grows roughly quadratically per session.** Every journal line holds the whole `SessionData`, which includes `AppliedCommands` (every command id + its canonical-JSON fingerprint) and `ConsumedEvents`, both unbounded per session. `SessionData.cs:49`, `SessionData.cs:51`, written by `SessionCoordinator.TryMutate` `SessionCoordinator.cs:914-941`, fingerprint built at `SessionCoordinator.cs:371`. Every aim key press is a `SetAim` command. On the dev machine, `~/Library/Application Support/DefaultCompany/Real-Golf/PuttshackPro/sessions/session-000033.jsonl` is 8.2 MB, 240 lines, last line 57 KB, with 185 applied commands, and only got 3-4 holes into an 18-hole round. Every mutation also deep-clones `_data` through JSON (`SessionCoordinator.cs:916`), and so does every `Snapshot` rebuild (`SessionCoordinator.cs:94`), so CPU cost per command grows during a round too. Not profiled.
- **Old sessions are never deleted.** `JournalSessionStore` only appends new `session-NNNNNN.jsonl` files (`JournalSessionStore.cs:164-175`); torn-tail backups `*.torn-<ticks>` are never cleaned up (`JournalSessionStore.cs:118`). The dev folder already has 44 files. On a kiosk this is unbounded disk use.
- **Corrupt journal stops startup.** `JournalSerializer.Replay` throws `StoreCorruptException` for a bad line that is not the last one (`SessionStore.cs:98`). Nothing catches it: `SessionCoordinator.StartOrResume` (`SessionCoordinator.cs:69`) -> `GameHost.Initialize` (`GameHost.cs:126`). The game would not start until someone deletes the file by hand.
- **FAULT with a reserved shot is a dead end.** If `ResolvePending` faults (version mismatch, solver exception or storage failure, `SessionCoordinator.cs:289-304`, `354-357`), the state is `FAULT` with `ResumeState = SHOT_RESERVED`. `Resume` retries the same resolution (`SessionCoordinator.cs:602-612`), `EndRound` is refused from `FAULT` and while a shot is effectively reserved (`SessionCoordinator.cs:614-615`, `708-709`), and `Pause` is refused in `SHOT_RESERVED` (`SessionCoordinator.cs:594`). For a version mismatch there is no in-game way out; staff would have to delete the journal.
- **Emulator input is live in every build.** Digit keys fire `Emulator.TriggerLane` without a debug check (`GameHost.cs:586-590`). `EmulatorPanel` (F1, including the crash and restart buttons) is added whenever `ShowEmulatorPanel` is true (`GameHost.cs:313`), and `Main.unity` serializes `ShowEmulatorPanel: 1`. Only F9 auto-play is debug-gated (`RoundFlow.cs:143`).
- **No real hardware path.** `GameHost.cs:125` always builds `EmulatorCabinetAdapter`, and `GameHost.Emulator` is typed as the concrete emulator (`GameHost.cs:85`), so `PumpCabinet`, `EmulatorPanel` and `DebugAutoPlay` depend on emulator-only APIs.
- **A solver fault still costs a stroke.** `progress.Played++` runs before the outcome branch (`SessionCoordinator.cs:315`), and the `SolverFault` branch only puts the ball back at the origin (`SessionCoordinator.cs:332-336`). This may be intended, since "the played stroke always counts", but it is not documented as a rule. `MaxDurationS = 40` triggers it (`ShotSolver.cs:20`, `94-95`).
- **Rename hazard.** The save location comes from `Application.persistentDataPath` + `PuttshackPro` (`GameHost.cs:119`). `ProjectSettings.asset:15-16` still says `companyName: DefaultCompany`, `productName: Real-Golf`. Fixing these for the "Puttshack Pro" rebrand moves the folder, so existing sessions and `roster.json` are silently left behind.

## Correctness / consistency questions

- `Vec3d.cs:6` says "Right-handed, Y-up", but `RotateYaw` (`Vec3d.cs:71-77`) turns +Z toward +X for positive yaw ("player's right"), and `Palette.ToUnity` (`Runtime/Palette.cs:38`) maps x, y, z 1:1 into Unity's left-handed frame. In practice the data behaves as Unity's frame. The comment looks wrong.
- The putter launch speed always uses the **green's** rolling resistance (`ShotSolver.cs:62`), even when putting from fairway or rough. The roll then decelerates on the real surface. Unclear if intended.
- `HazardVolume.WaterLevelM` (`HoleDefinition.cs:25`) is ignored by the solver. `AnalyticWorld.HazardAt` uses only the polygon (`CollisionWorld.cs:62-69`). Only Runtime visuals read it (`CourseVisualBuilder.cs:45`, `521`).
- `CupProfile` (`HoleDefinition.cs:79-89`) is never read from hole JSON (`CourseData.cs:242-342` does not set `hole.Cup`). Every hole uses the C# defaults.
- `SessionData.RulesVersion` / `SimulationVersion` (`SessionData.cs:26-27`) are stored but never checked on resume. Only `CourseContentHash` gates resume (`SessionCoordinator.cs:71`). The version check happens only per reserved shot (`SessionCoordinator.cs:289`).
- Since resume requires a matching course hash, the `HoleVersion` mismatch branch in `ResolvePending` (`SessionCoordinator.cs:289`) appears unreachable after a content change. It still guards simulation and rules version bumps.
- `CommitInitial` leaves an in-memory FAULT session with `Revision = 1` that was never written if the first commit fails (`SessionCoordinator.cs:792-804`). `EnterFault` also mutates in memory when storage fails (`SessionCoordinator.cs:886-892`). Both are by design ("your score is saved" may then be untrue).
- `Reconcile` -> `EnterFault("Score check failed ...")` on an integrity mismatch (`SessionCoordinator.cs:811-815`). A `Resume` from that FAULT simply continues with the inconsistent data.
- `Preview()` defaults to lane 10 (`SessionCoordinator.cs:637`). Lane 10 maps to Q = -0.053, so the preview line sits slightly left of the aim. With 20 lanes there is no centre lane (`InputMath.cs:30`).
- `SessionSnapshot` is described as immutable (`SessionSnapshot.cs:8-9`) but exposes the mutable `SessionData` copy. If one reader mutates it, every other reader of the same cached snapshot sees the change (authority is unaffected).
- `CabinetHealth.Capabilities` from the emulator is the adapter's own mutable object (`EmulatorCabinetAdapter.cs:56-61`, `114-115`), so the coordinator sees `SetCommissioned` changes before `UpdateCabinetHealth` is called. A real adapter should hand over immutable copies.
- `EmulatorCabinetAdapter.SupplyCaptureTimestamp` changes event emission at once (`EmulatorCabinetAdapter.cs:146-148`), but `Capabilities.CaptureTimestamp` is refreshed only on `Boot()` (`EmulatorCabinetAdapter.cs:128`).
- The emulator refreshes the heartbeat on every `Poll` (`EmulatorCabinetAdapter.cs:65`). A real adapter must not copy this.
- `Poll` writes `BridgeReceiptMs` into the same `CabinetEvent` object that `TriggerLane` returned to its caller (`EmulatorCabinetAdapter.cs:79`).
- With `ReceiptTimeOnly`, power is computed from Poll/frame time (`MeterPowerResolver.cs:50`, `CabinetProtocol.cs:57`). That adds latency and frame quantization.
- `RosterMemory.Remember` writes `roster.json` with a non-atomic `File.WriteAllText` (`Runtime/RosterMemory.cs:51`).
- The "spec section N" document cited throughout the comments is not in the repo.

## Dead / unused code

- `EmulatorScript` (whole file `Hardware/EmulatorScript.cs`). Nothing uses it.
- `EmulatorCabinetAdapter.EventsEmitted` / `EventsPerLane` (`EmulatorCabinetAdapter.cs:36-37`, `159-160`). Never read.
- `SessionState.TUTORIAL` (`Enums.cs:7`). Nothing transitions into it; only excluded in Runtime range checks (`GameHost.cs:731`, `HudView.cs:76`, `HudFallback.cs:139`).
- `ShotStatus.VOIDED_BY_OPERATOR` (`Enums.cs:37`), `RejectReason.SESSION_NOT_FOUND` (`Commands.cs:40`). Unused.
- `PlayerState.Withdrawn` (`Models.cs:22`). No command ever sets it true, so `HoleStatus.WITHDRAWN` / "WD" are unreachable in play.
- `SessionSettings.InputMode` (`Models.cs:204`). Never read.
- `SessionSettings.PaceCapOverPar` (`Models.cs:203`). No command changes it (always 5).
- `ClubDefinition.RangeFromReference` (`ClubCatalog.cs:26`). Set on some clubs, never read.
- Catalog version constants `ClubCatalog.Version` (`ClubCatalog.cs:37`), `CosmeticCatalog.Version` (`CosmeticCatalog.cs:52`), `ProfileCatalog.Version` (`ProfileCatalog.cs:26`). Never read. `SurfaceCatalog.Version` is only printed by a test.
- Putter `SpeedSamples` are all zero (`ClubCatalog.cs:61`). Not used, because putts go through `PuttSpeed`.
- `CabinetEvent.PhysicalHoleId`, and capabilities `CaptureTimestamp`, `SpeedMeasurement`, `BallReturnSensor`, `MenuActions`, `LightingGroups` (`CabinetProtocol.cs:18-23`, `45`). Never consumed by the service.
- `GrayboxCourse.ProposedPars` / course.json `ProposedPars`. Only asserted in `GoldenFixtureTests.cs:133`.
- Duplicate default-name logic: `GameHost.DefaultNames` / `DefaultName` (`GameHost.cs:683-692`) copies `SessionCoordinator.DefaultNames` / `DefaultName` (`SessionCoordinator.cs:24-34`).

## Magic numbers (not in a named constant)

- `ShotSolver.cs:75` airborne only if launch speed > 0.05 m/s.
- `ShotSolver.cs:118`, `221` tree-contact nudge 1e-4 m.
- `ShotSolver.cs:119` keeps 30% of vertical speed on an airborne tree hit.
- `ShotSolver.cs:236` lip-out deflection up to 0.35 rad.
- `ShotSolver.cs:240` lip re-arms beyond `2 * LipRadius`.
- `CupCapture.cs:87` drop curve control point at half of `min(speed * fallTime, CupRadius)`.
- `TerrainModel.cs:52` normal finite-difference step 0.02 m.
- `MeterPowerResolver.cs:44` +1.0 ms future-capture tolerance.
- `SessionCoordinator.cs:518` aim yaw delta bounded by pi. `SessionCoordinator.cs:521` aim target must be >= 0.5 m from the ball.
- `SessionCoordinator.cs:689-690` putt-scale thresholds 12 ft and 34 ft.
- `ClubCatalog.cs:119-127` suggestion thresholds 12 yd (putter), 250 yd (driver from the tee), 0.95 of `RangeMaxYd`.
- `ClubTraits.cs:38-48` display normalisation: sweep 1.4-3.0 s, fan `(deg - 4) / 10`, power `/300 yd`, wind `0.2 + launch / 60`, forgiveness weights 0.15 / 0.5 / 0.45.
- `CourseLayout.cs:228` cart path ribbons within 6 m of a hole boundary become surfaces. `CourseLayout.cs:250` 1.0 m park join. `CourseLayout.cs:259` +1.5 m tee-park slack.
- `JournalSessionStore.cs:24-27` snapshot every 100 entries (minimum 10).
- `EmulatorCabinetAdapter.cs:48` default seed 1234. `:97` conflict offset 5 ms. `:156` duplicate offset 1 ms.
- `EmulatorPanel.cs:61` "Late event" = lane 10, 60 000 ms old. `:62` conflict lanes 7 and 8.
- `DebugAutoPlay.cs:44-45` 600 steps per hole, 8% power scatter. `:99` lanes 9-12.

## Performance notes

- `CourseSpec.BuildHole` calls `Terrain()` for every hole (`CourseData.cs:265`). Each hole carries its own full copy of the course heightfield. `CourseRouting.StandIn` then deep-clones holes through JSON (`CourseRouting.cs:53`), so an 18-hole round holds 18 copies. Load-time and memory cost only.
- `ShotSolver.Solve` runs synchronously on the main thread inside `HandleCabinetEvent`, between two journal flushes (`SessionCoordinator.cs:185`, `298`, `312`). It is capped at 4800 steps. `DebugAutoPlay.BestPower` runs about 30 solves per stroke (`DebugAutoPlay.cs:146-148`).

## Test gaps noticed (emulator area)

- No tests for `JitterMs`, `SupplyCaptureTimestamp = false` (ReceiptTimeOnly), `Disconnect`/`Reconnect`, or `EmulatorScript`. Searched `Assets/_Game/Tests` for those members.
