# Screenshot plan (gameplay)

Ten captures that illustrate [03-gameplay.md](../03-gameplay.md). Save each into `docs/images/` with the filename given; items 1-8 and 10 replace placeholders already in 03-gameplay.md. Timings are approximate and come from constants in code (named in brackets); capture at 1920x1080 in the Game view.

## Before you start

1. Open `Assets/_Game/Scenes/Main.unity` (not `ProShop.unity`, which has no game logic).
2. Press Play once and read the Console. If you see either of these, the saved scene is missing UI and the captures will show the plain fallback HUD and skip Player Setup:
   - `[Puttshack] No baked HUD in this scene. Run Puttshack > HUD > Place HUD in Main Scene.`
   - `[Puttshack] No Player Setup in this scene. Run Puttshack > Player Setup > Place Player Setup in Main Scene.`

   Both menu commands modify and save `Main.unity`. Agree with the team before running them (the on-disk scene lacks both; see [runtime-risks.md](runtime-risks.md)).
3. For a clean, repeatable start select `Game Host` and tick **In Memory Storage** before pressing Play (do not save the scene with it ticked). Otherwise the game resumes a saved lobby and restores the last roster from `~/Library/Application Support/DefaultCompany/Real-Golf/PuttshackPro/`.
4. Keep the default two players, Caleb and Maya. "ENTER" below means the Return key; SPACE works too.
5. The Game view must have keyboard focus: click inside it once after pressing Play.

## Captures

| # | File | Shows | How to reach it |
|---|---|---|---|
| 1 | `gameplay-player-setup.png` | Player Setup: roster list, NAME, BODY, SKIN TONE, HAIR COLOUR, HAIR STYLE, "PRO SHOP · OUTFIT", "START ROUND · 2 PLAYERS", golfer on the turntable | Press Play. Player Setup opens automatically. Wait about 3 s for the shop camera to finish its ease into the portrait framing (`ProShopCameraRig.IntroSeconds` = 1.6 s). Optional: press DOWN twice (first press selects Maya, second moves to NAME) so Maya is on the turntable. |
| 2 | `gameplay-proshop.png` (optional live replacement for `proshop-ui.jpg`) | Pro Shop with the BAGS category and a bag on the plinth | From Player Setup press ESC (lobby "ROUND SETUP" card shows), then press `P`. Wait about 3 s. Press TAB four times (TOPS -> BOTTOMS -> SHOES -> SOCKS -> BAGS), then DOWN once to focus a priced bag and ENTER to try it on (SAVE LOADOUT shows "· 350 CR"). Press ESC afterwards to leave without saving. Alternative: click the "PRO SHOP · OUTFIT" button in Player Setup. |
| 3 | `gameplay-hole-title.png` | Hole title card ("HOLE 1 OF 18", "CYPRESS RIDGE", "PAR 4 · 289 YD", hole ticks) over the start of the flyover | From the lobby card press ENTER ("START ROUND"), or in Player Setup click "START ROUND · 2 PLAYERS". The screen fades in from Midnight over 0.9 s and the title stays 2.8 s (`RoundFlow.TitleSeconds`). Capture about 1.5 s after starting. Do not press any key. |
| 4 | `gameplay-address.png` | Caleb at address on the hole 1 tee with the 3 Wood, 20-lane aim fan, cyan aim line, dotted 75% preview line, bottom rail with key hints | Continue from 3 without input: after about 3 s the briefing card replaces the title ("CALEB TEES OFF"). Press ENTER once to tee off (if the flyover is still moving it dips through Midnight for about 1 s). Wait 2 s for the camera to settle. Optional: press LEFT or RIGHT a few times to show the fan rotating. |
| 5 | `gameplay-armed-meter.png` | SHOT_ARMED: rail power bar filling, pulsing aqua ready dot, hint "1-0 / SHIFT+1-0 LANE" | From 4 press ENTER (Ready). The meter rises from 0 to 100% in 1.8 s for the 3 Wood on CLUB difficulty, then falls back. Capture about 1 s after ENTER (bar around half full). Do not press a number key yet. |
| 6 | `gameplay-ball-flight.png` | Ball in the air with its cyan trail, follow camera behind it | From 5, wait until the bar is near full (about 1.6-1.8 s after ENTER) and press `0` (lane 10, just left of the aim line). The golfer swings; the ball leaves about 1.2 s later (`GolferMotion` contact time). Capture 0.5-1.5 s after the ball leaves. If timing is hard, open the emulator with F1 first and click lane "10" instead of pressing `0`. |
| 7 | `gameplay-result-live.png` (optional live replacement for `hud-result.jpg`) | Result card (surface title, CARRY / ROLL / TOTAL, "LANE 10   POWER nn%   3 WOOD"), golfer reacting behind it | Continue from 6 without input. The card appears 0.8 s after the ball stops (`GameHost.ResultHoldSeconds`), typically 6-10 s after the key press. |
| 8 | `gameplay-cart-drive.png` | Golfer seated in the golf cart on the cart path, chase camera, banner "ON TO HOLE 2 · ARROWHEAD POINT · PAR 3 · 121 YD   ENTER TO SKIP" | From 7 press ENTER (handoff card for Maya appears), then press `F9` (debug auto-play finishes hole 1; notice "DEBUG · HOLE PLAYED OUT"). The hole card appears: press ENTER (scorecard page), then ENTER again. After a 1.2 s Midnight dip the cart drive runs 4.5-7.5 s (`CartDrive.MinSeconds`/`MaxSeconds`). Capture 2-3 s after the second ENTER. Do not press any key during the drive (any confirm key skips it). |
| 9 | `gameplay-winner.png` | Round finale: winner celebrating beside the last cup, orbiting camera, "ROUND COMPLETE · 18 HOLES" banner, confetti | After the cart drive and hole 2 briefing, press `SHIFT+F9` (plays every remaining stroke; the editor may hitch for several seconds; notice "DEBUG · PLAYED ON TO THE LAST HOLE"). The hole 18 hole card appears. Press ENTER (scorecard page), then ENTER again. After the dip, wait 2-3 s for the camera orbit and the victory clip (`RoundFlow.Celebrate`, clips alternate every 4.2 s). Pressing ENTER once more shows the standings card (can recapture `hud-final.jpg`). |
| 10 | `gameplay-emulator-panel.png` | "Cabinet emulator (F1)" window: lane buttons 1-20, Delay/Jitter sliders, fault toggles, log | During any shot setup press `F1`. Best in SHOT_ARMED (press ENTER first) so the log and "State: SHOT_ARMED" are meaningful. Drag the window by its title bar if it covers the golfer. Press `F1` again to hide it. |

## Extra states (if time allows)

- **Pause card**: in shot setup press ESC ("ROUND SAVED", footer "ESC RESUME", "P PRO SHOP   E END ROUND   R RESTART"). ESC again resumes.
- **Scorecard overlay mid-round**: press TAB in shot setup; TAB again closes it.
- **Putting view**: after the tee shot, if the ball is on the green the putter is selected automatically and the camera drops to 3.2 m behind the ball; press `P` to cycle the putt scale.
- **Water penalty card** on hole 3 (Stillwater Cliffs): aim far left with SHIFT+LEFT several times and hit a high-power shot; the card reads "WATER" with "+1 · DROP TAKEN".

## Reset between attempts

Stop Play and press Play again. With In Memory Storage ticked every run starts fresh at Player Setup.
