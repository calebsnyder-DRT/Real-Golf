# Clubs

15 club models in three colourways, built from AI-generated art and held by the golfers through the existing
lead-hand seats. Gameplay still has the same nine clubs (`ClubCatalog`). Models and colourways are cosmetic only.

## Pipeline

1. **AI art** (Cursor image generator), checked in here:
   - `Concepts/`: the lineup and colourway concepts used as the style target.
   - `Silhouettes/sil_*.jpg`: orthographic black-on-white head outlines (driver, 3-wood and hybrid plans; cavity
     iron, blade iron and wedge faces; blade, mallet and fang putter plans).
   - `Textures/ai/tex_*.jpg`: tileable grip rubber, carbon weave and milled putter face.
2. **Blender**, run with `Blender -b --factory-startup -P ArtSource/Blender/scripts/pp_clubs.py -- all`:
   - Traces each silhouette into an outline. Irons, wedges and putters become meshes with constrained Delaunay
     fill and thickness fields (cavity window, muscle back, sole). Woods are a dome cut by a boolean face plane,
     then bevelled.
   - Adds the hosel, ferrule, stepped steel or graphite shaft, and grip.
   - Derives normal maps from the AI textures, plus procedural face grooves.
   - Writes `Exports/Club_<id>.fbx`, `Exports/clubs_manifest.json`, `Textures/Club_*.png`,
     `PuttshackPro_Clubs.blend` (backed up to `_backup.blend` before each save) and `ArtSource/Review/Clubs/blender_*.png`.
3. **Unity**, through the menu `Puttshack > Clubs` (`ClubArtBaker`). Each step is short:
   - Copy Textures, then Copy Models into `Assets/_Game/Art/Clubs/{Textures,Models}`. `ClubImportRules` sets up the
     imports: no animation, normal maps flagged.
   - Build Palettes: URP/Lit materials per colourway in `Materials/<palette>/`, plus `Palettes/ClubPalette_*.asset`.
   - Build Prefabs: `Prefabs/Club_<id>.prefab`, holding a `ClubModel`, the FBX under `Model`, and the reference points.
   - Build Library: `Assets/_Game/Resources/Clubs/ClubLibrary.asset`, holding the models, palettes and sets.
   - Bake Icons: re-renders `Art/ProShop/UI/Icons/club.<id>.png`, keeping the same files and sprite GUIDs.
   - Review: Lineup and Heads, written to `ArtSource/Review/Clubs/unity_*.png`.

## Hand-attachment convention

Every club is authored in the **seat frame**, the `ClubGrip_<Kind>` bone under `ClubGrip.L` that the baked
`Club_<Kind>` skin rides. The seat frame is defined by `pp_golfer_clubs.py`, which these clubs do not change:

- **Origin**: the lead-hand grip point, 55 mm below the butt.
- **+Y**: down the shaft, toward the head.
- **+Z**: the square face normal, toward the target. The toe points to **-X**.
- The prefab root has an identity local pose under the seat, at scale 1 and in metres.

The shaft line, length and lie match the old club of the same seat kind, so heads land where the old heads did.
The golfer agent measured the ball contact at 0.0–1.3 cm for Caleb and Maya.

| Seat kind | Length (butt to sole) | Lie | Models |
|---|---|---|---|
| Driver | 1.156 m (45.5 in) | 58.0° | driver, wood3 |
| Iron | 0.940 m (37 in) | 62.5° | hybrid, iron5/7/9 cavity, iron5/7/9 blade |
| Wedge | 0.895 m (35.2 in) | 64.0° | wedge_pw, wedge_sw, wedge_lw |
| Putter | 0.864 m (34 in) | 70.0° | putter_blade, putter_mallet, putter_fang |

Reference points are child transforms with seat-aligned axes. `clubs_manifest.json` has the same values.

| Child | Where |
|---|---|
| `Grip` | (0, 0, 0): lead hand, the seat origin |
| `Grip_Trail` | (0, 0.085, 0): trail hand (`TrailGripGap`) |
| `Butt` | (0, -0.055, 0) |
| `Hosel` | the top of the hosel on the shaft line |
| `Face` | the face centre. Its local +Z is the lofted face normal. |
| `Sole` | the lowest point of the sole at address |

`ClubModel` also stores `Kind`, `LengthMeters`, `LieDegrees`, `LoftDegrees`, `Graphite`, `FaceStyle`, and `Roles`
(the material role of each submesh: Head, Face, Accent, Crown, Shaft, Grip, Trim, Decal).
`ClubModel.Paint(palette)` assigns the materials.

## In game

- **Course golfer**: `CourseGolfer` adds a `HeldClubView` to the golfer. The view spawns the shooter's set model
  under the active seat and hides the baked club renderer. It does not change GolferHandIK, GolferMotion or the
  character assets. Without a library model, the baked club shows as before.
- **Pro Shop plinth**: on the Clubs tab, `BagDisplay.ShowClub` stands the edited player's model for the club.
- **Icons**: `ProShopBaker` uses `ClubArtBaker.IconSubject` when it re-bakes icons.

Sets are picked by roster colour (`ClubLibrary.SetFor(colorIndex)`):

| Colour | Set | Irons | 5 iron slot | Putter |
|---|---|---|---|---|
| 0 (Caleb) | Puttshack Cyan: satin silver, cyan accents, ink carbon crown | cavity | hybrid | fang |
| 1 (Maya) | Midnight: black PVD, gold accents | cavity | 5 iron cavity | mallet |
| 2 | Tour Chrome: chrome, black accents, carbon crown | muscle-back blade | 5 iron blade | blade |

All three sets use the same driver, 3-wood and three wedges (46°, 56° and 60° lofts), each in its own colourway.
Sets wrap for colours above 2.

## If the geometry changes

Contact depends on head geometry in seat space. If you rebuild clubs with a different length, lie or head
position, re-run `Puttshack > Characters > Bake Golfers` and confirm its check reports 0 problems.
