# 07 - Testing

The project has two Unity Test Framework assemblies: **EditMode** ([Assets/_Game/Tests/EditMode](../Assets/_Game/Tests/EditMode), 16 fixtures, about 147 test cases counted from the `[Test]`, `[TestCase]` and `[Values]` attributes) and **PlayMode** ([Assets/_Game/Tests/PlayMode](../Assets/_Game/Tests/PlayMode), 3 end-to-end tests that load `Main.unity`). EditMode covers the simulation (ball flight, bounce, cup capture), the session state machine and crash recovery, the data-driven course and its terrain, Pro Shop loadouts and Player Setup profiles, and checks that the **baked assets** (course prefabs, golfers, HUD, audio) are present and current. There is also one Python check, `test_terrain_parity.py`, that keeps the Blender terrain port identical to the C# one. This page explains how to run all of them from the Test Runner window and from the command line, lists every fixture, and records the gaps. The Test Runner window's own count is authoritative; the numbers here were counted from source, not from a run.

---

## 1. Test assemblies

| Assembly | asmdef | References | Platforms | Notes |
|---|---|---|---|---|
| `Puttshack.Tests.EditMode` | [Puttshack.Tests.EditMode.asmdef](../Assets/_Game/Tests/EditMode/Puttshack.Tests.EditMode.asmdef) | `Puttshack.Core`, `Puttshack.Hardware`, `Puttshack.Runtime`, **`Puttshack.Editor`**, `UnityEngine.UI`, `Unity.TextMeshPro`, test runner assemblies; precompiled `nunit.framework.dll`, `Newtonsoft.Json.dll` | Editor only | Can call the editor tools directly (e.g. `CourseBaker.Verify()`, `GolferCharacterBaker.Verify()`). Fixtures in `Fixtures/`: `terrain_parity.golden.json`, `LegacyFrame/course.json`. |
| `Puttshack.Tests.PlayMode` | [Puttshack.Tests.PlayMode.asmdef](../Assets/_Game/Tests/PlayMode/Puttshack.Tests.PlayMode.asmdef) | `Puttshack.Core`, `Puttshack.Hardware`, `Puttshack.Runtime`, `UnityEngine.UI`, `Unity.RenderPipelines.Universal.Runtime`, `UnityEngine.TestRunner`; precompiled `nunit.framework.dll` | All | Loads `Main` by scene name (it must be in Build Settings, which `Build Main Scene` guarantees). Sets `GameHost.ForceInMemoryStorage = true` so tests never touch real save data. |

Both assemblies have `defineConstraints: ["UNITY_INCLUDE_TESTS"]` and `autoReferenced: false`. Test classes are in namespace `Puttshack.Tests`. No test uses `[Category]`, `[Explicit]` or `[Ignore]`; `AudioBankTests.MixerHasAGroupPerBus` calls `Assert.Ignore` when no mixer exists. Test framework package: `com.unity.test-framework` 1.6.0 ([Packages/manifest.json](../Packages/manifest.json)). No code-coverage package is installed.

---

## 2. Running tests in the editor (Test Runner window)

1. Open the project in Unity 6000.4.11f1 and wait for compilation.
2. `Window > General > Test Runner`.
3. **EditMode** tab: `Run All`, or select a fixture/test and `Run Selected`. Failures show the assertion message; many of them tell you which `Puttshack/*` menu to run (for example "Run Puttshack > Build Main Scene to rebake.").
4. **PlayMode** tab: `Run All`. Unity asks to save modified scenes, enters Play mode, loads `Main.unity` for each test and restores your scene afterwards. Each PlayMode test takes several seconds (they wait for the host, the shop camera and a real shot).


Before running, make sure the bakes are current (see [04-editor-tools.md](04-editor-tools.md)); otherwise `BakedCourseTests`, `GolferCharacterTests`, `HudRenderTests`, `TerrainParityTests` and the PlayMode tests fail by design.

---

## 3. Running tests from the command line

The Unity editor **must be closed** for this project: only one Unity instance can open a project at a time (a second one fails on the project lock in `Temp/`). Run from the repository root.

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.4.11f1/Unity.app/Contents/MacOS/Unity
PROJECT="$(pwd)"            # the Real-Golf folder

# EditMode
"$UNITY" -batchmode -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testResults "$PROJECT/Logs/TestResults-EditMode.xml" \
  -logFile "$PROJECT/Logs/TestRun-EditMode.log"

# PlayMode
"$UNITY" -batchmode -projectPath "$PROJECT" \
  -runTests -testPlatform PlayMode \
  -testResults "$PROJECT/Logs/TestResults-PlayMode.xml" \
  -logFile "$PROJECT/Logs/TestRun-PlayMode.log"
```

Notes:

- Do **not** add `-quit`: with `-runTests` Unity exits by itself when the run finishes, and `-quit` can end the process before the tests run.
- Do **not** add `-nographics` for PlayMode: the PlayMode tests load the full URP scene, and the optional capture paths render with cameras. **Unclear** whether the non-capture path would pass without a graphics device; it was not tested.
- `-testResults` writes an NUnit 3 XML file; `Logs/` is gitignored, so it is a convenient place. The process exits with a non-zero code when tests fail; treat the XML as the source of truth.
- Run one fixture or test: add `-testFilter "Puttshack.Tests.TerrainParityTests"` (full class name; a full method name or a `;`-separated list also works). Restrict to one assembly with `-assemblyNames Puttshack.Tests.EditMode`.
- The first batch run after a pull re-imports assets and can take a long time; the log shows progress.
- These commands were not executed while writing this page (the editor had the project open).

### 3.1 Optional review captures from the PlayMode tests

Set these environment variables before starting Unity (editor or batch) to make the PlayMode tests also write 1920x1080 screenshots:

| Variable | Test | Value | Writes |
|---|---|---|---|
| `PUTTSHACK_HUD_CAPTURE` | `MainSceneSmokeTest` | a **file** path, e.g. `Logs/hud.png` | the shot-setup screen with the HUD |
| `PUTTSHACK_SETUP_CAPTURE` | `PlayerSetupFlowTest` | a **folder** | `a_caleb_native.png`, `b_male_long_blonde_deep.png`, `c_female_short_copper_porcelain.png`, `d_maya_native.png`, `e_jordan_default.png` |
| `PUTTSHACK_PROSHOP_CAPTURE` | `ProShopFlowTest` | a **folder** | `a_overview.png`, `b_tops_polo.png`, `c_bottoms_shorts_socks.png`, `d_bags_try_on.png`, `e_player_switched.png`, `f_clubs.png`, `g_course_tee.png` |

### 3.2 Python terrain parity check

Not part of the Unity run; run it whenever terrain or `pp_pebble_terrain.py` changes (details in [05-art-pipeline.md](05-art-pipeline.md#22-terrain-parity-system-c---python)):

```bash
python3 ArtSource/Blender/scripts/test_terrain_parity.py
/Applications/Blender.app/Contents/MacOS/Blender -b --python ArtSource/Blender/scripts/test_terrain_parity.py   # adds the numpy port
```

Prints `PASS` and exits 0, or `FAIL` with the worst probe and exits 1. The first command was run while writing this page and passed (1623/1623 probes bit-identical).

---

## 4. Fixture reference

### 4.1 EditMode

| Fixture (file) | Tests | What it covers | Depends on baked/generated assets? |
|---|---|---|---|
| `AudioBankTests` ([AudioBankTests.cs](../Assets/_Game/Tests/EditMode/AudioBankTests.cs)) | 3 | Every `GolfSound` has a preset and a resource key; the shipped clips under `Resources/Audio/` cover every non-music sound; `GolfMixer` has a group per `AudioBus` (ignored if no mixer). | Yes: `Assets/_Game/Resources/Audio/**` (from `build_golf_audio.py`) |
| `BakedCourseTests` ([BakedCourseTests.cs](../Assets/_Game/Tests/EditMode/BakedCourseTests.cs)) | 3 | `CourseBaker.Verify()` is empty (baked hash == definition); every hole prefab's meshes and materials are persistent assets, has `Tee` and `Cup`, and has no colliders; `Main.unity` is the only Build Settings scene. | Yes: `Puttshack/Build Main Scene` |
| `BallBounceTests` ([BallBounceTests.cs](../Assets/_Game/Tests/EditMode/BallBounceTests.cs)) | 6 + 20 cases | Surfaces-1.2 ball response: visible second hop on fairway, green/rough rebound and bunker plugs, water stops dead; per-club carry unchanged and total slightly longer (16 cases); putt distances unchanged (4 cases); 20 ft putt; rendered ball near real size; a logged shot report on the current hole. | No |
| `CourseDataTests` ([CourseDataTests.cs](../Assets/_Game/Tests/EditMode/CourseDataTests.cs)) | 11 | Data-driven course loader: the frozen `Fixtures/LegacyFrame/course.json` + live hole files reproduce the hashes Unity baked from the old hand-written course (Mono and CoreCLR values pinned); holes 3-5 play identically in the grown course; legacy cart drives unchanged; deterministic build; exact decimal number conversion; loader rejects mistakes; cart-path surface entries; design intent of holes 6, 7, 8. | No (reads `Resources/Course` JSON) |
| `CourseLayoutTests` ([CourseLayoutTests.cs](../Assets/_Game/Tests/EditMode/CourseLayoutTests.cs)) | 3 | Course validates; cart-path transition from each green to the next tee; tee parking off the tee and out of hazards. | No |
| `CourseRoutingTests` ([CourseRoutingTests.cs](../Assets/_Game/Tests/EditMode/CourseRoutingTests.cs)) | 6 | 18-hole routing numbered 1-18; authored holes unchanged; stand-in holes keep their layout's collision and visuals; consecutive holes linked by cart path; short rounds; a full 18-hole two-player round through the real service. | No |
| `CupCaptureTests` ([CupCaptureTests.cs](../Assets/_Game/Tests/EditMode/CupCaptureTests.cs)) | 9 + 7 cases | Sim-1.3 cup: centre and edge catches, edge less forgiving of pace, balls dying at the lip drop, nothing rests on the lip, clearly bad putts still miss, holed set is a superset of sim-1.2, max capture speed falls toward the edge, rim assist only on slow balls, holed balls visibly drop. | No |
| `GoldenFixtureTests` ([GoldenFixtureTests.cs](../Assets/_Game/Tests/EditMode/GoldenFixtureTests.cs)) | 10 | "Golden fixtures from spec section 43": meter triangle wave, lane fan mapping, unit conversion, assistance dead-band/gain, water penalty scoring, live scorecard, par-24 to-par, pace cap, turn scheduler, course validation and stable hash. | No |
| `GolferCharacterTests` ([GolferCharacterTests.cs](../Assets/_Game/Tests/EditMode/GolferCharacterTests.cs)) | 1 | `GolferCharacterBaker.Verify()` reports no problems (both bodies, slots, avatars, controller states, grips, measured club contact). | Yes: `Puttshack/Characters/Bake Golfers` |
| `HudRenderTests` ([HudRenderTests.cs](../Assets/_Game/Tests/EditMode/HudRenderTests.cs)) | 3 | Instantiates the baked `HUD.prefab` and drives it through real session states: one screen per state over a round, the penalty card on a water shot, the celebration on an under-par hole. Uses `HudView.Render` (no GPU rendering). | Yes: `Puttshack/HUD/Bake HUD` (`SetUp` fails with "Bake the HUD first") |
| `LoadoutTests` ([LoadoutTests.cs](../Assets/_Game/Tests/EditMode/LoadoutTests.cs)) | 11 | Pro Shop loadouts: catalogue validity, roster defaults, Back restores, Save per player, Reset, item states (equipped/owned/for sale), state and catalogue guards, editing another player from pause without moving the turn, cosmetics never change clubs or the shot, persistence across restart, stale loadouts fall back. | No |
| `ProfileTests` ([ProfileTests.cs](../Assets/_Game/Tests/EditMode/ProfileTests.cs)) | 4 | Player Setup profiles: catalogue defaults and natives, filling missing/retired choices, `RosterSet` assignment and validation. | No |
| `RecoveryTests` ([RecoveryTests.cs](../Assets/_Game/Tests/EditMode/RecoveryTests.cs)) | 7 (9 cases) | Crash recovery (spec section 39): crash after reservation and after resolution (each in memory and on disk via a journal in a temp folder that is deleted afterwards), crash while armed, during presentation, torn journal tail, torn in-memory write, completed round starts fresh. | No |
| `SessionFlowTests` ([SessionFlowTests.cs](../Assets/_Game/Tests/EditMode/SessionFlowTests.cs)) | 16 | Session state machine via [SessionHarness.cs](../Assets/_Game/Tests/EditMode/SessionHarness.cs) (real service + emulator + virtual clock): revision-guarded idempotent commands, roster validation, arming refused when the cabinet is unavailable, rejected/duplicate/late/conflicting events, controller reboot, meter power from capture time, water and out-of-bounds penalties, two-player hole completion, hole continue, pause/resume, early end, storage failure. | No |
| `SolverTests` ([SolverTests.cs](../Assets/_Game/Tests/EditMode/SolverTests.cs)) | 8 + 14 cases | Shot solver: carry rises with power, full-power carry in club range, realistic fairway rollout per club (8 cases), rough/bunker stopping, approach check-up on greens (6 cases), stimp 10 green, putt scale, no tunnelling through trunks, deterministic hash, finite results across lanes and powers. | No |
| `TerrainParityTests` ([TerrainParityTests.cs](../Assets/_Game/Tests/EditMode/TerrainParityTests.cs)) | 3 | The golden still describes the current course; golden features reproduce golden heights bit for bit; pruned `TerrainModel.Height` equals an unpruned reference. | Yes: `Fixtures/terrain_parity.golden.json` (`Puttshack/Course/Write Terrain Parity Golden`) |

`SessionHarness.cs` is a helper (no tests): it drives the real session service through the emulator adapter with a virtual clock and can simulate restarts.

### 4.2 PlayMode

| Test (file) | What it covers | Requires in `Main.unity` |
|---|---|---|
| `MainSceneSmokeTest.OneShot_ThroughEmulator_OnBakedCourse` ([MainSceneSmokeTest.cs](../Assets/_Game/Tests/PlayMode/MainSceneSmokeTest.cs)) | Launch lands in the lobby with Player Setup open; set a two-player roster; briefing uses the **baked** hole visuals (not the runtime fallback); emulator panel hidden; arm, fire lane 11 through the emulator, get one finite shot result; hand-off to Maya. | `GameHost`, baked holes, Player Setup, HUD (for the optional capture) |
| `PlayerSetupFlowTest.Setup_CountNamesAndLooks_ReachTheRound` ([PlayerSetupFlowTest.cs](../Assets/_Game/Tests/PlayMode/PlayerSetupFlowTest.cs)) | Roster count capped at `SessionCoordinator.MaxPlayers`; names trimmed, blank refused; selecting Maya previews her body and long hair; gender/hair/skin choices reach the profile string and the preview body; shaved hair hides hair; starting the round closes setup with all players. | `GameHost`, Player Setup, Pro Shop (preview golfer) |
| `ProShopFlowTest.Shop_FromLobbyAndPause_SaveKeepsBackDiscards` ([ProShopFlowTest.cs](../Assets/_Game/Tests/PlayMode/ProShopFlowTest.cs)) | Exactly one Pro Shop; opens from the lobby; Back discards try-ons for every player and restores course lighting; Save commits and marks items owned; shop closed during play; the course golfer wears the saved loadout and bag; shop opens from pause on the current player, editing another player never moves the turn; resume closes the shop; restart from pause returns to the lobby with the shop open and looks kept. | `GameHost`, Pro Shop, course golfer |
| `DocsGameplayCapture.CaptureGameplay` ([DocsGameplayCapture.cs](../Assets/_Game/Tests/PlayMode/DocsGameplayCapture.cs)) | Not a correctness test. It is **ignored unless `PUTTSHACK_DOCS_CAPTURE` names an output folder**. It plays from Player Setup through a tee shot, hand-off, hole 1 (auto-played) and the cart drive, and writes the `gameplay-*.png` frames used in `docs/images`. | Same as the smoke test |

### 4.3 Results at handover (2026-10-19)

Run in batch mode on a clean copy of the handover tree:

| Suite | Result |
|---|---|
| EditMode | 147 / 147 passed |
| PlayMode | 1 / 3 passed. `MainSceneSmokeTest` passes. `PlayerSetupFlowTest` and `ProShopFlowTest` fail on stale assertions. |

The causes and one-line fixes for both failures are in [08-handover-notes.md](08-handover-notes.md#2-test-status-at-handover).

---

## 5. Coverage gaps

No coverage tool is installed, so there is no coverage percentage. From reading the tests:

- **Editor tools are only covered through their `Verify()` functions.** Nothing tests `ProShopBaker.Verify()` (only the PlayMode tests indirectly need the shop), the club pipeline (`ClubLibrary.asset` contents, prefab reference points), `PlayerSetupUIBuilder`/`ProShopUIBuilder` output beyond what the PlayMode tests touch, or the capture/review tools.
- **Import rules (all `AssetPostprocessor`s) have no tests.** A wrong import setting shows up only visually or through `Verify Golfers`/`Verify Pro Shop`.
- **Hardware:** `Puttshack.Hardware` contains only the emulator adapter; tests drive the session through it. There is no test for a real device adapter (none exists yet; see [06-hardware-and-protocol.md](06-hardware-and-protocol.md)).
- **Rendering/visual quality:** lighting presets, post-processing, the scenery seam and LODs are checked only by eye (capture menus and the optional PlayMode captures). `TerrainParityTests` guards the seam's height function, not the exported mesh.
- **Python:** only `test_terrain_parity.py`. The Blender build scripts, UI sprite generators and the audio builder have no tests; the 9-slice `BORDERS` tables duplicated between Python and C# are not cross-checked.
- **Runtime content loading:** no test loads every `Resources.Load` path used by `CourseVisualAssets`, `CartDrive` or `ClubLibrary`; a missing model makes the runtime fall back silently (behaviour not verified here).
- **Persistence on disk** is covered by `RecoveryTests` (journal store in a temp folder); the PlayMode tests always use in-memory storage, so the real save location used by the game is not exercised end to end.
- **No CI**: there is no `.github/` or other CI configuration in the repository; tests run only when someone runs them.
