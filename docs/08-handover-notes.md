# 08 - Handover Notes: State, Risks and Next Steps

This is the honest status of the project at handover on 2026-10-19: what was committed, what the tests say, what is known to be broken or risky, and a suggested order of work for the new team. The three files under [notes/](notes/) hold the full itemised findings with file and line references; this page is the prioritised summary.

## 1. What is in the repository

The repository was pushed as three commits on top of the original eight:

| Commit | What it contains |
|---|---|
| `chore: snapshot working tree for handover` | Everything that was uncommitted on the authoring machine: MPFB golfers (Caleb, Maya), Player Setup, clubs, audio, Pebble Beach course data and bakes, 500 deleted legacy assets (Kenney furniture/props, old golfer materials, superseded Mixamo clips). Nothing still references the deleted files (checked by GUID against every scene, prefab, material, asset and controller). |
| `fix: restore HUD and Player Setup in Main scene` | `Main.unity` only. See section 3.1. |
| `docs: handover documentation with screenshots` | `README.md`, `docs/`, `.gitattributes`, `.gitignore` additions. |

Deliberately **not** in git (still on the authoring Mac if needed):

- `Library/`, `Temp/`, `Logs/`, `UserSettings/`: Unity caches, rebuilt on first open.
- `ArtSource/Review/` (about 470 MB of review renders) and `ArtSource/Blender/wip/`: already ignored by the original `.gitignore`.
- `*_backup.blend`, `*.blend1` and `ArtSource/Backup/`: tool-generated backup copies of files that are committed.

**Keep the repository private.** `ArtSource/Mixamo/` contains raw Mixamo FBX downloads, and Mixamo's terms do not allow redistributing them on their own. Several third-party licences are also unverified (section 4.6).

There is no Git LFS. Every file is under GitHub's 100 MB limit (largest: `ArtSource/Blender/PuttshackPro_Caleb_Modular.blend`, 57 MB). If the team expects to churn large `.blend` or lighting files, migrate to LFS early, before history grows: `git lfs migrate import --include="*.blend,*.fbx,*.bytes,*.png,*.wav" --everything` (this rewrites history; coordinate a force-push).

## 2. Test status at handover

Run on a clean copy of the handover tree with Unity 6000.4.11f1 in batch mode (`-runTests`, see [07-testing.md](07-testing.md)).

| Suite | Result |
|---|---|
| EditMode | **147 / 147 passed** |
| PlayMode | **1 / 3 passed** (`MainSceneSmokeTest` passes; two flow tests fail on stale assertions, below) |

Before the scene fix in section 3.1, all three PlayMode tests failed at their first Player Setup assertion.

The two remaining failures are test drift, not crashes:

1. **`PlayerSetupFlowTest.Setup_CountNamesAndLooks_ReachTheRound`** fails at [PlayerSetupFlowTest.cs:47](../Assets/_Game/Tests/PlayMode/PlayerSetupFlowTest.cs). The test renames player 3 to `"  Jordan "`, but `SessionCoordinator.DefaultName` already names the third player "Jordan", and `PlayerSetupController.Rename` returns `false` when the trimmed name equals the current one ([PlayerSetupController.cs:128](../Assets/_Game/Scripts/Runtime/Setup/PlayerSetupController.cs)). Fix: rename to a name that is not a default (for example `"  Riley "`) and update the expectation on the next line.
2. **`ProShopFlowTest.Shop_FromLobbyAndPause_SaveKeepsBackDiscards`** fails at [ProShopFlowTest.cs:144](../Assets/_Game/Tests/PlayMode/ProShopFlowTest.cs), `"Restart reopens the Pro Shop."`. Home now opens Player Setup when the scene has one (`GameHost.OpenHome`: `if (!OpenSetup()) OpenShop();`). Line 47 of the same test was already updated to expect Player Setup at launch; line 144 was not. Decide which is the intended product behaviour after Restart, then fix either the test or `OpenHome`.

There is no CI. Setting up a GitHub Actions runner with [GameCI](https://game.ci) is a good first task; the batch commands in [07-testing.md](07-testing.md) are what it should run.

## 3. Fixes made during handover

### 3.1 Main scene was missing its HUD and Player Setup

The saved `Assets/_Game/Scenes/Main.unity` (last saved 2026-10-05) had lost its `HUD.prefab` instance, which the previous commit had. It had also never received the newer `PlayerSetupUI.prefab`. Playing it showed the plain fallback HUD and opened the Pro Shop instead of Player Setup.

Both were restored using the project's own tools, `HudBaker.PlaceInScene` and `PlayerSetupUIBuilder.PlaceInScene`. Those are the code paths behind **Puttshack > HUD > Place HUD in Main Scene** and **Puttshack > Player Setup > Place Player Setup in Main Scene**, run without re-baking either prefab. No other file changed. If the Console ever prints "No baked HUD in this scene" or "No Player Setup in this scene", run those two menu items again.

## 4. Known risks, in suggested priority order

### 4.1 Release blockers

- **Debug and emulator input ships in every build.** Number keys fire emulated cabinet lanes with no debug check ([GameHost.cs:586-590](../Assets/_Game/Scripts/Runtime/GameHost.cs)). The F1 emulator window, which includes crash-injection buttons, is enabled because `Main.unity` serialises `ShowEmulatorPanel: 1`. Only F9 auto-play is gated by `Debug.isDebugBuild`.
- **There is no real hardware path.** `GameHost.Initialize` always constructs `EmulatorCabinetAdapter`, and `GameHost.Emulator` is typed as the concrete emulator. The seam to implement is `ICabinetAdapter`; [06-hardware-and-protocol.md](06-hardware-and-protocol.md) has a step-by-step plan.
- **The session journal grows without bound, and corruption blocks startup.** Every mutation appends the full `SessionData`, including every applied command id. On the authoring machine single session files reached 4-8 MB after a few holes. Old session files are never deleted. A malformed line that is not the last one throws `StoreCorruptException`, which nothing catches, so the game will not start until the file is deleted by hand. Save data lives in `~/Library/Application Support/DefaultCompany/Real-Golf/PuttshackPro/` on macOS.
- **A FAULT while a shot is reserved has no in-game exit** (version mismatch or solver/storage failure). Staff would have to delete the journal.

### 4.2 Project identity is still the template default

`ProjectSettings.asset` still has `companyName: DefaultCompany`, `productName: Real-Golf` and bundle id `com.Unity-Technologies.com.unity.template.urp-blank`. Changing company or product name **moves the save folder** (it is under `Application.persistentDataPath`), so existing sessions and `roster.json` are silently left behind. Decide the final identity before any build reaches real players.

### 4.3 Destructive editor menus

Read [04-editor-tools.md](04-editor-tools.md) before running bakes. The dangerous ones:

- **Puttshack > Pro Shop > Bake Pro Shop** rebuilds `Caleb_Golfer.prefab` from Caleb alone and resets the Idle clip, removing Maya, hand IK, club contact data and hair. Always follow it with **Puttshack > Characters > Bake Golfers**.
- **Puttshack > Build Main Scene** recreates `Main.unity` from an empty scene, resets Build Settings and rewrites the URP and Graphics settings assets. That is why those files often show as modified.
- **Rebuild Fonts** (both menus) delete and recreate font assets with new GUIDs; re-bake all UI afterwards.
- **Bake Golfers** deletes textures, clips and materials in `Art/Characters/` that it did not create. Do not hand-place assets there.

### 4.4 Art scripts only run on the original Mac

About 15 Blender scripts hard-code `/Users/calebsnyder/Real-Golf` (for example `ROOT` in `ArtSource/Blender/scripts/pp_lib.py:10`, `pp_golfer.py:24`, `pp_pebble.py:29`). The current golfer build also depends on the MPFB add-on data folder for Blender 5.2 on macOS. The full list with line numbers is in [notes/tools-and-pipeline-risks.md](notes/tools-and-pipeline-risks.md). A one-time fix is to derive `ROOT` from `__file__`, as `pp_clubs.py` and `test_terrain_parity.py` already do. Unity-side code has no machine-specific paths.

### 4.5 Gameplay gaps

- **Gamepad cannot play a shot.** It only opens the shop (Y) and skips the cart drive (A or Start). All in-round input is keyboard, polled directly; the project's `InputSystem_Actions.inputactions` is unused.
- **No music.** The sound bank expects `Resources/Audio/Music/`, which does not exist. Many sound effects are placeholders (`_ph_` in the file name).
- **Six authored hole layouts are stretched to an 18-hole round.** `course.json` routing replays five layouts as renumbered stand-ins, which keep their original names, so "Cypress Ridge" is both hole 1 and hole 4. The cliff par 3 "Stillwater Cliffs" lives in `h03-garden-green.json`.
- **Main Camera has a missing-script component.** It is a `GolfCameraRig` serialised without a GUID, because the class lives in `Views/ShotViews.cs`. GameHost adds a working rig at runtime, but `CourseCameraFocus` caches the rig in `Awake`, before that happens, so depth of field probably never follows the camera mode.
- The iron swing reuses the driver clip, there is no flag pull when putting, and shop prices are display-only (no currency balance).

### 4.6 Licensing to verify before any public release

- The six "Ringside" HUD fonts have no licence file in the repo.
- MakeHuman community assets used by the current golfers (clothes, skins, hair, brows, lashes) are not credited or verified.
- `ArtSource/CREDITS.md` is out of date: it credits Hunyuan3D for the golfers, which are now MPFB/MakeHuman, and lists old Mixamo clip names.
- Mixamo raw files: keep private (section 1).

## 5. Suggested first two weeks

1. Clone, open, and play through one hole using [01-getting-started.md](01-getting-started.md). Run both test suites.
2. Fix the two stale PlayMode assertions (section 2) so the suite is green, then add CI.
3. Gate emulator keys and the F1 panel behind `Debug.isDebugBuild` or a scripting define.
4. Bound the session journal (snapshot plus truncate, or compact `AppliedCommands`), delete old sessions, and catch `StoreCorruptException` at startup with a clean fallback.
5. Agree the product identity (section 4.2) before the save folder matters.
6. Make the Blender scripts relocatable (section 4.4) so artists other than the original author can rebuild assets.
7. Start the real `ICabinetAdapter` against the target hardware.

## 6. Regenerating the documentation screenshots

Every image in [docs/images](images/) was rendered by Unity itself, not taken as a screen capture, so it can be regenerated on any machine. Close the editor first, because batch mode needs the project lock. Set `U` to the editor binary:

```bash
U=/Applications/Unity/Hub/Editor/6000.4.11f1/Unity.app/Contents/MacOS/Unity
```

Course views (`course-*.jpg`):

```bash
PUTTSHACK_PREVIEW_DIR=/tmp/docs/course "$U" -batchmode -quit -projectPath . -executeMethod Puttshack.EditorTools.CourseBaker.CapturePreviewBatch
```

HUD screens over the design backdrops (`hud-*.jpg`). This also re-bakes `HUD.prefab`:

```bash
PUTTSHACK_HUD_PREVIEW=/tmp/docs/hud "$U" -batchmode -quit -projectPath . -executeMethod Puttshack.EditorTools.HudUIPreview.BakeAndCaptureBatch
```

Pro Shop UI over its backdrop (`proshop-ui.jpg`):

```bash
PUTTSHACK_UI_PREVIEW=/tmp/docs/proshop-ui.png "$U" -batchmode -quit -projectPath . -executeMethod Puttshack.EditorTools.ProShopUIPreview.CaptureBatch
```

Golfer stills (`golfer-*.jpg`). These are written to `ArtSource/Review/Characters`:

```bash
"$U" -batchmode -quit -projectPath . -executeMethod Puttshack.EditorTools.GolferReviewShots.BatchRender -reviewTag docs
```

Animation frames (`anim-*.jpg`, combined side by side):

```bash
"$U" -batchmode -quit -projectPath . -executeMethod Puttshack.EditorTools.GolferReviewShots.BatchRenderStates -reviewDir /tmp/docs/poses -reviewStates Swing_Driver,Putt -reviewFrames 6
```

Live gameplay frames (`gameplay-*.jpg`). This plays the real game in PlayMode and renders each moment:

```bash
PUTTSHACK_DOCS_CAPTURE=/tmp/docs/gameplay "$U" -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter Puttshack.Tests.DocsGameplayCapture -testResults /tmp/docs/capture.xml
```

The HUD and Pro Shop previews composite the real UI prefabs over stored backdrop images from `ArtSource/UI/HUD/Brand/Backdrops/`. The `gameplay-*` images are true in-game frames. PNGs were converted to JPEG for the docs with `sips -s format jpeg -s formatOptions 85 in.png --out out.jpg`.

Player Setup and the Pro Shop are not shown as live frames. In batch-mode renders their Text Mesh Pro labels come out blank, while the HUD's text renders correctly. The author's own earlier Pro Shop review captures show the same blank labels, so this is a capture limitation rather than a game bug. Confirm it once in the editor's Game view. `proshop-ui.jpg` and `hud-setup.jpg` come from the editor preview tools, which render the text correctly.

Not captured, because they need the editor UI on screen: the Puttshack menu, the Test Runner window and the F1 emulator window (IMGUI cannot be rendered through a camera). The menus and the emulator window are fully described in text in [04-editor-tools.md](04-editor-tools.md) and [06-hardware-and-protocol.md](06-hardware-and-protocol.md).

## 7. Detailed findings

- [notes/core-and-hardware-risks.md](notes/core-and-hardware-risks.md): session service, solver, storage, protocol; dead code and magic numbers.
- [notes/runtime-risks.md](notes/runtime-risks.md): gameplay, HUD, camera, audio, scenes.
- [notes/tools-and-pipeline-risks.md](notes/tools-and-pipeline-risks.md): editor bakes, Blender pipeline, hard-coded paths, licences, tests.
- [notes/live-screenshot-plan.md](notes/live-screenshot-plan.md): exact keys and timings to reproduce each gameplay moment by hand in the editor.
