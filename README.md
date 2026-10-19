# Puttshack Pro (Real-Golf)

A Unity 6 golf game built for a Puttshack-style cabinet. Players set up a group of up to six, dress their golfer in the Pro Shop, and play an 18-hole round on a Pebble Beach-inspired cliffside course. On the real cabinet, a shot is taken by dropping a ball into one of 20 target holes. The timing of the drop sets power and the hole sets direction. Today that cabinet is emulated from the keyboard.

![Golfer at address on hole 1](docs/images/gameplay-address.jpg)

| | |
|---|---|
| Engine | Unity **6000.4.11f1**, Universal Render Pipeline 17.4 |
| Platforms | Developed and tested in the macOS editor; no player builds have been shipped |
| Entry scene | `Assets/_Game/Scenes/Main.unity` |
| Code | About 30,000 lines of C# in four assemblies (Core, Hardware, Runtime, Editor) plus EditMode and PlayMode tests |
| Art source | Blender 5.2 scripts and files in `ArtSource/` |
| Status | Playable prototype. EditMode tests 147/147 pass; PlayMode 1/3 pass (two stale assertions, see [handover notes](docs/08-handover-notes.md#2-test-status-at-handover)) |

## Quick start

1. Install **Unity Hub**, sign in, add a licence, and install editor **6000.4.11f1**.
2. Clone the repo:
   ```bash
   git clone https://github.com/calebsnyder-DRT/Real-Golf.git
   ```
3. In Hub, choose **Add project from disk** and pick the folder. The first import takes several minutes.
4. Open `Assets/_Game/Scenes/Main.unity` and press **Play**.
5. Click inside the Game view so it has keyboard focus. Player Setup opens. Click **START ROUND**, or press **ESC** to reach the Round Setup card and then **ENTER**.
6. When the hole title or briefing shows, press **ENTER** to tee off. You may need to press it twice: the first press hides the title.
7. Aim with **LEFT/RIGHT**, change club with **UP/DOWN**, and press **ENTER** to arm the power meter.
8. Press a number key **1-0**, or **SHIFT+1-0**, to "drop the ball" into one of the 20 cabinet lanes. Lanes 10 and 11 are nearest the aim line.

Full setup, including the one-time git merge configuration for Unity scenes, is in [docs/01-getting-started.md](docs/01-getting-started.md). The complete controls table is in [docs/03-gameplay.md](docs/03-gameplay.md#2-controls).

## How it plays

| | |
|---|---|
| ![Round Setup](docs/images/hud-setup.jpg) **Round Setup:** players, difficulty and pace cap (UI shown over its design backdrop). | ![Pro Shop](docs/images/proshop-ui.jpg) **Pro Shop:** cosmetic loadouts per player (UI shown over its design backdrop). |
| ![Hole title](docs/images/gameplay-hole-title.jpg) **Hole intro:** title card over a flyover. | ![Armed meter](docs/images/gameplay-armed-meter.jpg) **Shot armed:** the power meter runs; the lane chosen sets direction. |
| ![Ball flight](docs/images/gameplay-ball-flight.jpg) **Ball flight:** deterministic solver, follow camera. | ![Result](docs/images/gameplay-result-live.jpg) **Result card:** surface, carry, roll and total. |
| ![Cart drive](docs/images/gameplay-cart-drive.jpg) **Between holes:** cart drive to the next tee. | ![Course overview](docs/images/course-overview.jpg) **The course:** cliffside par 3 and surrounding holes. |

| ![Caleb](docs/images/golfer-caleb-body.jpg) | ![Maya](docs/images/golfer-maya-body.jpg) |
|---|---|
| Caleb | Maya |

![Driver swing, Caleb](docs/images/anim-swing-driver-caleb.jpg)

More in the [screenshot tour](docs/screenshot-tour.md). Every image above was rendered by Unity from this repository, not taken as a screen capture. [Handover notes, section 6](docs/08-handover-notes.md#6-regenerating-the-documentation-screenshots) has the commands to regenerate them.

## Documentation

| Doc | What it covers |
|---|---|
| [01 - Getting started](docs/01-getting-started.md) | Tools, licence, clone, first open, git setup for Unity files, repo layout |
| [02 - Architecture](docs/02-architecture.md) | Assemblies, boot sequence, the session state machine, ball physics, rules, content, save data, a "where to change X" table |
| [03 - Gameplay](docs/03-gameplay.md) | Player journey, every control, a shot step by step, course, golfers and animation, clubs, HUD, camera, audio, scene contents |
| [04 - Editor tools](docs/04-editor-tools.md) | Every `Puttshack/*` menu item, a clean-rebuild runbook, which bakes are destructive, import rules |
| [05 - Art pipeline](docs/05-art-pipeline.md) | Blender to Unity for golfers, animations, clubs, course, Pro Shop, UI and audio, plus licences |
| [06 - Hardware and protocol](docs/06-hardware-and-protocol.md) | The cabinet model, protocol types, the emulator, how to plug in real hardware |
| [07 - Testing](docs/07-testing.md) | Running tests in the editor and from the command line; what each fixture covers |
| [Screenshot tour](docs/screenshot-tour.md) | Every screenshot in play order, with the state and code behind each moment |
| [08 - Handover notes](docs/08-handover-notes.md) | **Start here after setup.** Current state, test results, known risks in priority order, suggested first tasks |

Itemised findings with file and line references are in [docs/notes/](docs/notes/).

## Repository layout

```
Assets/_Game/      Game code (Scripts/Core, Hardware, Runtime, Editor), tests, scenes, prefabs, baked course, art
Assets/Settings/   URP pipeline and renderer assets
ArtSource/         Blender sources and scripts, exports, Mixamo clips, UI and audio sources
docs/              Documentation and screenshots
Packages/          Unity package manifest
ProjectSettings/   Unity project settings
```

## Important before you start

- **Keep this repository private.** It contains raw Mixamo animation files, which Mixamo's terms do not allow redistributing. Several asset licences still need checking ([details](docs/08-handover-notes.md#46-licensing-to-verify-before-any-public-release)).
- **Some editor bakes are destructive.** Read [04-editor-tools.md](docs/04-editor-tools.md) before running anything under the `Puttshack` menu.
- **The Blender scripts hard-code the original author's paths.** Edit `ROOT` in the scripts listed in the [handover notes](docs/08-handover-notes.md#44-art-scripts-only-run-on-the-original-mac) before running them.
