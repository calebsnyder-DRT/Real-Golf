# Screenshot Tour

A visual walk through one round, in the order a player sees it, followed by the course, golfers, animation and every HUD screen. Each caption names the session state and the code that drives the moment, so you can jump from a picture to the source.

Images come from three sources. All of them were rendered by Unity from this repository. [08-handover-notes.md, section 6](08-handover-notes.md#6-regenerating-the-documentation-screenshots) has the commands to regenerate them.

- **Live**: real frames from the game running in PlayMode. Captured by `DocsGameplayCapture`, which drives the session through `GameHost` and the cabinet emulator.
- **Preview**: the real UI prefabs rendered over stored design backdrops by the editor preview tools (`HudUIPreview`, `ProShopUIPreview`).
- **Review**: course and golfer renders from `CourseBaker.CapturePreview` and `GolferReviewShots`.

## 1. Before the round

![Round Setup card](images/hud-setup.jpg)

**Round Setup card** (Preview, state `LOBBY`). The player count is 1 to 6, changed with UP/DOWN. Difficulty is AMATEUR, CLUB or PRO, changed with LEFT/RIGHT. C toggles the pace cap, P opens the Pro Shop, and ENTER starts the round. This is the lobby card of the baked HUD; Player Setup, the fuller roster editor, opens over it at launch. See [03-gameplay.md, step 2](03-gameplay.md#step-2---player-setup-how-many-players-names-golfer).

![Pro Shop](images/proshop-ui.jpg)

**Pro Shop** (Preview). Each player has a cosmetic loadout: tops, bottoms, shoes, socks, bags and clubs. Q/E switches player, TAB switches category, ENTER tries an item on, and ESC goes back. Prices are display only. Driven by `ProShopController` and `ProShopView`.

## 2. Teeing off

![Hole title](images/gameplay-hole-title.jpg)

**Hole title over the flyover** (Live, `BRIEFING`). "HOLE 1 OF 18, CYPRESS RIDGE, PAR 4, 289 YD", with one tick per hole. `RoundFlow` and `RoundOverlay` show it for 2.8 s while the flyover camera runs.

![Briefing](images/gameplay-briefing.jpg)

**Briefing** (Live, `BRIEFING`). The first player's turn card appears. ENTER tees off; if the flyover is still moving, the screen dips to black first.

![Address](images/gameplay-address.jpg)

**At address** (Live, `SHOT_SETUP`). Caleb stands at address with the suggested club (3 Wood). The 20-line fan shows the 20 cabinet lanes, and the cyan line is the aim. The rail shows player and stroke, hole, distance, par, club, wind and the preview power. LEFT/RIGHT aims (SHIFT for 5 degree steps), UP/DOWN changes club, and ENTER arms.

![Armed](images/gameplay-armed-meter.jpg)

**Shot armed** (Live, `SHOT_ARMED`). The power meter runs as a triangle wave, here at 51%. The hint changes to "1-0 / SHIFT+1-0 LANE". On the real cabinet, the moment the ball drops sets the power and the target hole it drops into sets the direction. In the editor, the number keys emulate that.

![Swing](images/gameplay-swing.jpg)

**Swing** (Live, `SHOT_PRESENTING`). A lane was hit near the top of the meter. The shot is solved instantly and deterministically by `ShotSolver`. The golfer plays the club's swing clip, and the ball leaves at the clip's baked contact time (`GolferMotion`).

![Ball flight](images/gameplay-ball-flight.jpg)

**Ball flight** (Live, `SHOT_PRESENTING`). The follow camera tracks the ball along the solved path, and the distance on the rail counts down to the pin. ENTER skips the rest of the presentation.

![Result](images/gameplay-result-live.jpg)

**Shot result** (Live, `SHOT_RESULT`). The card shows the landing surface (FAIRWAY), distance to pin, carry, roll and total, plus the lane, power and club. The chips at the top show each player's strokes on this hole. ENTER continues.

![Handoff](images/gameplay-handoff-live.jpg)

**Handoff** (Live, `HANDOFF`). The next player is called up. ENTER means "next player ready".

## 3. Finishing a hole

![Hole complete](images/gameplay-hole-complete.jpg)

**Hole complete** (Live, `HOLE_COMPLETE`). Each player's score is shown with its name: PAR, BIRDIE and so on. ENTER shows the scorecard page; ENTER again moves to the next hole. Hole 1 was finished here with the debug auto-play (`DebugAutoPlay.FinishHole`, F9 in the editor).

![Scorecard](images/hud-scorecard.jpg)

**Scorecard** (Preview). TAB toggles this overlay during a round.

![Cart drive](images/gameplay-cart-drive.jpg)

**Cart drive** (Live, `HOLE_LOAD`). The golfer rides the cart along the path to the next tee, with a banner for the next hole. Any confirm key skips it.

![Hole 2](images/gameplay-hole2-address.jpg)

**Hole 2 at address** (Live, `SHOT_SETUP`). "Arrowhead Point", par 3.

## 4. The course

![Tee view](images/course-tee.jpg)

**From the tee** (Review). The baked course seen from a tee: striped fairway, rough, tee markers, ball washer and the coastline.

![Approach](images/course-approach.jpg)

**Approach** (Review).

![Overview](images/course-overview.jpg)

**Overview** (Review). The cliffside green above the cove, with bunkers, cart paths and sea stacks. Course geometry is baked into prefabs under `Assets/_Game/Course/Baked/` from the JSON in `Assets/_Game/Resources/Course/Pebble/`. See [03-gameplay.md, section 4](03-gameplay.md#4-the-course).

## 5. Golfers

| ![Caleb](images/golfer-caleb-body.jpg) | ![Maya](images/golfer-maya-body.jpg) |
|---|---|
| ![Caleb face](images/golfer-caleb-face.jpg) | ![Maya face](images/golfer-maya-face.jpg) |

**Caleb and Maya** (Review, rendered in the Pro Shop preview scene). These are the two bodies every player's golfer is built from, on one shared humanoid rig. Skin, hair and outfit are applied by `GolferAppearance`. The art comes from MPFB/MakeHuman in Blender, and the build is described in [05-art-pipeline.md](05-art-pipeline.md).

## 6. Animation

![Caleb driver swing](images/anim-swing-driver-caleb.jpg)

**Driver swing, Caleb.** Seven evenly spaced frames of the `Swing_Driver` state (Mixamo `Golf_Drive_Standard`), from address to finish.

![Maya driver swing](images/anim-swing-driver-maya.jpg)

**Driver swing, Maya.** The same clip retargeted to the second body.

![Maya putt](images/anim-putt-maya.jpg)

**Putt, Maya.** The `Putt` state (Mixamo `Golf_Putt_Medium`).

## 7. Every HUD screen

![HUD contact sheet](images/hud-preview-contact-sheet.jpg)

All twelve HUD screens on one sheet (Preview), reading left to right and top to bottom:

1. Attract
2. Round setup
3. Shot rail
4. Shot result
5. Briefing
6. Handoff
7. Birdie celebration
8. Hole complete
9. Paused
10. Fault
11. Scorecard
12. Final results

The full-size screens are below.

| | |
|---|---|
| ![Attract](images/hud-attract.jpg) Attract | ![Rail](images/hud-rail.jpg) Shot rail |
| ![Result](images/hud-result.jpg) Shot result | ![Birdie](images/hud-birdie.jpg) Birdie celebration |
| ![Briefing](images/hud-briefing.jpg) Briefing | ![Handoff](images/hud-handoff.jpg) Handoff |
| ![Hole](images/hud-hole.jpg) Hole complete (older footer text) | ![Final](images/hud-final.jpg) Final results |
| ![Paused](images/hud-paused.jpg) Paused (ESC) | ![Fault](images/hud-fault.jpg) Fault (cabinet or storage problem; ENTER is staff resume) |
