# Runtime notes (scratch)

Scratch list of TODOs, fragile spots, half-finished features and things that look broken in `Assets/_Game/Scripts/Runtime/**`, `Core/Input`, `Main.unity` and `ProShop.unity`. Line numbers are against the working tree on 2026-10-19 (uncommitted changes on top of `8592497`). "Verify" means it was found by reading code/YAML, not by running the game.

## Markers

- A grep for `TODO`, `FIXME`, `HACK`, `XXX` across `Runtime/` and `Core/Input/` finds **none**. The only "TUNE" markers in scope are in Core data (clubs, surfaces, cup capture), outside this list.

## Looks broken (verify first)

- **(Fixed in the handover commit) Main.unity was missing HUD and Player Setup.** The saved scene (last modified Oct 5) had no instance of `Prefabs/HUD/HUD.prefab` (guid `04bf9821964de426f9758c951bd4ebbd`, present in commit `8592497`) and had never received `Prefabs/PlayerSetup/PlayerSetupUI.prefab` (guid `c34d3c093f30b4632bf447c8073a3dad`). Effect at Play: warnings from `GameHost.cs:310`/`:321`, the `HudFallback` HUD, and the Pro Shop opening instead of Player Setup. Both instances were restored with `HudBaker.PlaceInScene` and `PlayerSetupUIBuilder.PlaceInScene` (the two "Place ... in Main Scene" menu items); `MainSceneSmokeTest` went from failing to passing.
- **Main Camera's `GolfCameraRig` has a broken script reference.** `Main.unity:1830` serializes it as `m_Script: {fileID: 1135990058}` with no GUID. `GolfCameraRig` lives in `Views/ShotViews.cs` (file name does not match the class), so Unity cannot bind it as a scene component. `GameHost.BuildPresentation` adds a fresh one at runtime (`GameHost.cs:269-270`), so gameplay works, but expect a "missing script" component in the Inspector.
- **Depth of field probably never follows the camera mode.** `CourseCameraFocus.Awake` caches `_rig = GetComponent<GolfCameraRig>()` (`Course/CourseCameraFocus.cs:48`). Awake runs at scene load, before `GameHost.Start` adds the rig, and is never refreshed, so `Target()` always returns `Setup` (`CourseCameraFocus.cs:83`): putting/celebration/flight focus values are unused. Fix: resolve the rig lazily in `LateUpdate`.
- **Round-complete footer text vs behaviour.** `HudView.Round.cs:157` ("ENTER PRO SHOP"/"ENTER CONTINUE") is dead because `RoundFlow.Decorate` always sets `PlayAgain` in ROUND_COMPLETE (`RoundFlow.cs:202`). The fallback HUD still shows "ENTER PRO SHOP" (`Hud/HudFallback.cs:323`) while ENTER actually plays again (`RoundFlow.cs:177-183`).
- **Unreachable GameHost branches.** `RoundFlow.HandleInput` consumes ENTER in BRIEFING, HOLE_COMPLETE and ROUND_COMPLETE before `GameHost` sees it, so `GameHost.cs:624` (BRIEFING confirm), `GameHost.cs:646` (HOLE_COMPLETE confirm) and `GameHost.cs:649` (ROUND_COMPLETE confirm -> ReturnToAttract) never run. Harmless but misleading.
- **No music.** `GolfSoundBank.ResourceKey` points `MusicMenu`/`MusicRound` at `Audio/Music/music_menu`/`music_round` (`Audio/GolfSoundBank.cs`), but `Resources/Audio/Music/` does not exist; both loops are silent.
- **No authored sound bank asset.** `Resources/Audio/GolfSoundBank.asset` does not exist; `LoadForRuntime` builds from code presets every launch. Tuning in the inspector is not possible until `Puttshack > Audio > Create or Update Sound Bank` is run.

## Half-finished features

- **Grass does not react to the ball.** `GrassField.SetBallPosition` / `ClearBall` (`Course/GrassField.cs:86-89`) are never called anywhere.
- **Placeholder audio.** Every file with `_ph_` in `Resources/Audio/` is a placeholder: all strike sounds, whoosh, cup drop, lip out, splash, roll loops, several landings, `Crowd/aww_ph_01.wav`, `Ambience/wind_ph_01.wav`, `Ambience/cart_hum_ph_01.wav`.
- **`TUTORIAL` state** exists in `SessionState` but nothing enters it; HUD code just excludes it (`HudView.cs:76`, `GameHost.cs:731`).
- **Attract screen is effectively unused** with `HomeInProShop` on: `GoHome` sends `Start` the same frame a session reaches ATTRACT (`GameHost.cs:138-175`).
- **Course is 6 layouts stretched to 18 holes.** `course.json` `Routing` replays h04-h08 as stand-ins (only hole 3 is its own layout). Stand-ins keep the original name, so "Cypress Ridge" is both hole 1 and hole 4. Round par is 70 vs `ProposedPars` 72.
- **File/name mismatch**: `h03-garden-green.json` is the hole named "Stillwater Cliffs".
- **Iron swing reuses the driver clip**: `Swing_Iron`/`Address_Iron` in `Art/Characters/Caleb/Caleb_Shop.controller` use `Golf_Drive_Standard`.
- **No flag pull** when putting; no runtime code hides the flagstick.
- **Shop prices are display only** (`CosmeticCatalog`: "no payments or accounts"); there is no balance, so "buying" always succeeds on Save.
- **Gamepad cannot play a shot.** In the round, gamepad only opens the shop (Y) and skips the cart drive (A/Start) (`GameHost.cs:609`, `620`). No gamepad aim/club/ready/continue, and no gamepad way to reopen Player Setup from the lobby (keyboard `S` only, `GameHost.cs:617`). `GolfAudioDirector.Ui.cs:86-88` plays confirm/back sounds for gamepad A/B on round pages that do nothing.
- **Project-wide Input Actions unused.** `Assets/InputSystem_Actions.inputactions` is registered as `com.unity.input.settings.actions` in `ProjectSettings/EditorBuildSettings.asset` but all input is polled from devices.
- **ProShop.unity is preview only**: no `GameHost`, so the shop controller never opens there.

## Fragile spots

- **Swing sync ignores `PlaybackSpeed`.** `GameHost` scales the launch countdown by `PlaybackSpeed` (`GameHost.cs:464`) but the Animator plays the swing at normal speed (`Shop/GolferMotion.cs` `Swing`), so any `PlaybackSpeed != 1` launches the ball before/after the clubhead arrives.
- **No swing if the golfer is hidden.** `CourseGolfer.Swing` returns 0 when the golfer is inactive (`Shop/CourseGolfer.cs:90`), so the ball launches immediately with no swing.
- **Contact times are baked data.** `GolferMotion.Clubs[*].Contact`/`Ball` on `Prefabs/ProShop/Caleb_Golfer.prefab` (around line 2515) must be re-baked (`Puttshack > Characters > Bake Golfers`) after any change to clips, rig or club seats, or the ball leaves at the wrong moment / the golfer stands off the ball.
- **Animator has no transitions.** `Caleb_Shop.controller` has zero `AnimatorStateTransition`s; non-looping clips (swings, reactions) hold their last frame until `GolferMotion` plays another state. Anything that forgets to call `Idle()`/`Address()` leaves the golfer frozen mid-pose.
- **Animator state names are strings.** `GolferMotion` uses `"Idle"`, `"React_Good"`, `"React_Miss"`, `"Celebrate"` and per-club names from serialized data; renaming a state silently disables it (`Play` returns false when `HasState` fails).
- **Duplicate default-name list.** `GameHost.DefaultNames` (`GameHost.cs:683`) duplicates `SessionCoordinator.DefaultNames`; the lobby UP key uses the GameHost copy, Player Setup uses the Core copy.
- **Lane keys fire in every state.** `GameHost.cs:586-590` triggers emulator lanes on digit keys whatever the state (except shop/setup open); the coordinator rejects them, but they fill the emulator log.
- **No exactly-centred lane.** `LaneMapper.Q` (`Core/Input/InputMath.cs:30`) gives lanes 10 and 11 +/-0.053 of the fan half-angle; aim assistance deadband may or may not zero that depending on club and difficulty.
- **Internal "LANE n" shown to players** on the result card (`Hud/HudView.Cards.cs:255`).
- **`P` putt-scale key is undiscoverable**: not in the SHOT_SETUP hint string (`GameHost.cs:884`).
- **Shop hint "→ SAVE"** (`Shop/ProShopController.cs:573`) means "RIGHT moves focus to the SAVE button, then ENTER"; there is no one-key keyboard save (gamepad Y only).
- **Audio button wiring by name.** `GolfAudioDirector.Ui.cs:57-73` rescans all uGUI Buttons every 1.5 s while a menu is open and picks the cue from the GameObject name ("back", "save", "shop" ...). Renaming buttons changes their sounds.
- **Hole title ticks are fixed at 18** (`Hud/RoundOverlay.cs:336`); a longer routing would need a rebuild.
- **Resume behaviour in the editor**: leaving Play mode sends `EndRound` (`GameHost.cs:253-258`), but a session left in LOBBY is resumed at next Play, and `roster.json` always restores the last players. Tests set `GameHost.ForceInMemoryStorage`.
- **Simulated crash recovery** recreates the coordinator but keeps presentation objects (`GameHost.SimulateRestart`, `GameHost.cs:404-418`); only the playing ball is stopped. Mid-cart-drive or mid-flyover crashes are untested by reading.
- **`HudFallback` vs baked HUD drift**: the two implementations duplicate texts and logic (`ToParWord`, standings sort, chip scores). Fallback lacks condor and triple-bogey words.
- **`EmulatorPanel.Visible` defaults false** (`EmulatorPanel.cs:14`) even though `ShowEmulatorPanel` is on; press F1.
- **Course golfer warning text** says to run `Puttshack > Pro Shop > Place Course Golfer`; the stand-in capsule (`GolferView`) has no animation and never swings.
