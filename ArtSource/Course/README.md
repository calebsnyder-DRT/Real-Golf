# Course content: data-driven holes

Holes are JSON files, not C#. `Puttshack.Core` (`CourseData`) expands them into the same `HoleDefinition`s the
hand-written `GrayboxCourse.cs` used to build, so gameplay, content hashes and the bake pipeline are unchanged.

```
Assets/_Game/Resources/Course/Pebble/
  course.json                 world frame: coast paths + shelves, cart path network, hole list, 18-hole routing
  Holes/<hole id>.json        one file per hole (file name == "Id")
```

They live under `Resources` so players load the same files (`CourseDataResources` installs a `Resources` reader in
players; the editor, tests and tools read the project folder). `GrayboxCourse`, `CourseLayout` and `CourseRouting`
are thin facades over `CourseData.Load()`; nothing else needed to change.

## Pipeline

1. Edit JSON (the editor re-reads it on import: `CourseDataPostprocessor`).
2. `Puttshack > Export Course Layout For Blender` → `ArtSource/Blender/pebble_course.json` (holes, ground grid, exact
   coast samples `CliffEdge`/`Shore`, every cart path line).
3. `Blender -b ArtSource/Blender/PuttshackPro_PebbleBeach.blend --python ArtSource/Blender/scripts/pp_pebble_scenery.py`
4. `Puttshack > Build Main Scene` (bakes every hole prefab), then `Puttshack > Verify Baked Course`.
5. If terrain changed: `Puttshack > Course > Write Terrain Parity Golden`, then run
   `python3 ArtSource/Blender/scripts/test_terrain_parity.py` (and under Blender's python for the numpy port).
6. EditMode tests (`CourseDataTests`, `TerrainParityTests`, `CourseLayoutTests`, `CourseRoutingTests`, `BakedCourseTests`, …).

## Rules the loader enforces

- **Strict keys.** Unknown keys are errors (typos fail loudly). `"Notes"` is allowed in any object.
- **Exact numbers.** Numbers are read as decimals and converted once, correctly rounded, so `0.1` in JSON is the same
  double as `0.1` in C# and in Python on every runtime (Mono's `double.Parse` is not correctly rounded and would shift
  hashes). Keep authored numbers to 15 significant digits; more is an error.
- **Ids.** A hole's `"Id"` must equal its file name; `Routing` may only name listed holes.

## course.json

| Key | Meaning |
|---|---|
| `Format` | `1` |
| `Id`, `Version`, `Name` | `CourseDefinition` id / version / display name |
| `SeaLevelM` | water level for `"WaterLevelM": "Sea"` |
| `ProposedPars` | 18 pars of the full design (tests check they sum to 72) |
| `Paths` | named open Catmull-Rom polylines: `{ "Samples": n, "Control": [[x, z], …] }` or `{ "Pieces": [ … ] }` |
| `Shelves` | coast shelves: `{ "Path", "Inner", "Outer", "Height" }` (`TerrainFeatureKind.Shelf`) |
| `CartPaths` | cart path lines (below) |
| `Holes` | hole ids in course order (also the terrain feature order) |
| `Routing` | layout id played at each of the 18 hole numbers (`CourseRouting`) |

**Pieces.** A path can be several pieces end to end; each piece starts on the previous piece's last control point and
is sampled on its own. Extending a coast with a new piece therefore leaves every existing sample bit-identical; this is
how the hole 6-8 headland was added without moving a single height on holes 3-5. `GrayboxCourse.CliffEdgeControl` is
all control points (junctions once); `GrayboxCourse.CliffEdge` is the exact sampled polyline (use it rather than
re-sampling the controls).

**Cart paths.** Line 0 must be the closed loop (`"Closed": true`); its behaviour (ids `cart-path-<n>`, loop side,
shorter-way-round transitions) is the legacy single loop. Further lines are open spurs that join another line at its
nearest sample (`"JoinStart"` / `"JoinEnd"`: a line id); their ribbons are `cart-path-<line id>-<n>`. Transitions use
the legacy loop route when both ends are on the loop, otherwise a shortest path over the network. Tee parking is the
nearest network point within 12.5 m of the tee, else 11 m from the tee toward it.

## Hole files

| Key | Meaning |
|---|---|
| `Id`, `Version`, `Name`, `Number`, `Par`, `YardageYd`, `DefaultSurface` | as `HoleDefinition` |
| `WindMps` | `[x, y, z]` |
| `Briefing` | `{ "Hazard", "SafePlan", "SuggestedClubId" }` |
| `Terrain` | this hole's shaping: `Plateau` / `Tilt` (`Center`, `Inner`, `Outer`, `Height` or `Slope`), free `Bowl` (`Shape`, `Inner`, `Height`) |
| `Surfaces` | `{ "Id", "Surface", "Priority", "Shape", "Bowl"? }` in order; a `"Bowl": { "Inner", "Height" }` also digs the shape into the terrain (bunkers). `{ "CartPaths": { "Priority": n } }` inserts the nearby cart path ribbons at that position |
| `Hazards` | `{ "Id", "DropPointId", "WaterLevelM"?: "Sea" or metres, "Shape" }` |
| `Drops` | `{ "Id", "HazardId", "At": [x, z] }` |
| `Trees` | `{ "Id", "At", "Trunk": [radius, height], "Canopy": [radius, height] }` |
| `PlayableBoundary` | a shape |
| `Tee`, `Cup`, `Route` | `[x, z]` points; heights come from the terrain |

Shapes (`"Kind"`): `Rect {Min, Max}`, `Ellipse {Center, Radii, Segments, Rotation?}`, `Loop {Control, Samples}` (closed
Catmull-Rom), `Polygon {Points}`, `InlandStrip {Path, Width, MinX/MaxX/MinZ/MaxZ?}` (band inland of a course path),
`Sea {Path, MinX/MaxX/MinZ/MaxZ?, Close}` (the course path's samples inside the limits, closed by far points).

**Terrain is course-wide.** Every hole carries the whole course heightfield: every hole's `Terrain` features in hole
order, then every bunker bowl in hole/surface order, then the shelves. Features only reach as far as their outer radius
or polygon, so a new hole only changes heights where its own features reach; keep new shaping, including the full
`Outer` radius, at least 30 m (the ground margin) away from other holes' boundaries. `pp_pebble.py` relies on any hole's
terrain describing the whole course.

**Cart path surfaces (known quirk).** `GrayboxCourse.cs` added the cart path regions *before* setting the playable
boundary, so its bounds test kept none: the drawn path plays as rough on every legacy hole. The data preserves that so
the baked hashes stay valid; opt a hole in with a `{ "CartPaths": { "Priority": 30 } }` surface entry (and rebake).

## Content hashes

`ContentHasher.HashHole` is SHA-256 over the canonical JSON of the expanded `HoleDefinition`, so any change to a hole
(including the shared terrain list) changes every hash and `Verify Baked Course` asks for a rebake. Two consequences:

- Newtonsoft prints doubles through the runtime, so Unity (Mono) and .NET hash the same values to different strings.
  Compare hashes only within one runtime.
- Adding holes 6-8 extended the shared terrain list, so holes 3-5 have new hashes **with bit-identical gameplay**.
  `Tests/EditMode/Fixtures/LegacyFrame/course.json` is the frozen pre-change world frame: with the live hole files it
  reproduces exactly the hashes Unity baked from `GrayboxCourse.cs` (`CourseDataTests` pins both the Mono and the
  CoreCLR values), and the tests prove holes 3-5 are identical in the grown course apart from the terrain list, with
  bitwise-equal heights over each boundary plus the 30 m ground margin and unchanged cart drives between them.

## Terrain parity (C# ↔ Python)

`TerrainModel.cs` (gameplay) and `pp_pebble_terrain.py` (scenery seam) must agree. `TerrainParity.Golden` writes the
course's features and ~1600 probe points (tees, cups, routes, surfaces, trees, across the cliff and shore every few
metres, and a seeded scatter over the grid) with IEEE bit patterns to
`Assets/_Game/Tests/EditMode/Fixtures/terrain_parity.golden.json`. `TerrainParityTests` checks the golden is current and
reproduces it bit for bit; `test_terrain_parity.py` evaluates the same features in Python (scalar and numpy ports; both
are bit-identical today). `TerrainModel` skips bunkers and coast segments by their bounds; `TerrainParityTests` also
checks that against an unpruned reference.

## Adding a hole

1. Extend `course.json`: new coast as a new path piece (never edit existing pieces), cart path spur, the hole id in
   `Holes`, and `Routing`.
2. Write `Holes/<id>.json`. Keep its terrain shaping 30 m+ from other holes' boundaries. Snap the boundary's extreme
   x/z to the current grid origin plus whole 0.8 m cells if it grows the grid (`MinX`/`MinZ` today: -160, -828) so the
   existing ground vertices keep their positions.
3. Run the pipeline above; `CourseLayoutTests` checks transitions (20-260 m) and tee parking, `CourseRoutingTests` the
   18-hole round, `CourseDataTests` that holes 3-5 are untouched.

## Holes 6-8 (the headland)

The cliff now continues south from hole 5 round a headland and back up its east side, with a ~50 m inlet (the chasm).

- **6 Headland Climb**, par 5, 444 yd: from (0, -214) south along the west cliff, climbing ~11 m to a green at
  (-45, -618); the Pacific runs down the whole right side.
- **7 Land's End**, par 3, 107 yd: 9 m downhill from (-22, -668) to a small green on the tip of the point with the sea
  long, left and right, ringed by five bunkers.
- **8 The Chasm**, par 4, 409 yd: tee shot along the east clifftop to a landing plateau, then a second shot over the
  inlet to a green on the far clifftop at (160, -425).

A `south` cart spur leaves the south end of the loop (between hole 5's tee and green), runs past the hole 6, 7 and 8
tees, and a `return` spur links hole 8's green back to it. The 18-hole routing is 4, 5, 3, 4, 5, 6, 7, 8, then 6, 7, 8 three times and 6 (par 70) until more
holes exist.

## Plan: per-hole streaming and ground LOD

### Where we are

- One scene holds every baked hole prefab, all active (`CourseBaker.BuildMainScene`, `GameHost.EnsureHoleVisual`).
- One ground grid (`CourseTiling`): a rectangle over all boundaries plus 30 m at 0.8 m, each cell drawn by the nearest
  hole. With holes 6-8 it grew from 441 × 584 = 257,544 to 564 × 1,369 = 772,116 vertices (x -160…290, z -828…266).
  Of its 770,184 cells, 24 % are more than 1.5 m below sea level and 23.5 % are dry land outside every boundary plus a
  6 m skirt; only ~52 % are ground a ball can actually rest on or see up close.
- Gameplay never reads the meshes: ball physics, lies and hazards use the analytic `HoleDefinition` from Core.
  Streaming and LOD are purely visual, as long as the playable ground stays exactly the analytic surface.
- `TerrainModel.Height` evaluates the whole course list at every point; bounds pruning keeps the cost per call about
  where three holes were, but it still grows with the course.

### Ground LOD (do first; static, no runtime cost)

1. Classify cells once at bake time (a Core helper beside `CourseTiling`, e.g. `CourseStreaming.ClassifyCell`):
   *playable* (inside a boundary, or within 6 m of one, and not deep water) → 0.8 m; everything else → 3.2 m
   (4 × 0.8, so coarse vertices are a subset of fine ones).
2. Build each hole's ground as 32 × 32-cell chunks; a chunk is fine if any of its cells is playable, else coarse.
   Stitch fine/coarse borders by moving the fine edge's in-between vertices onto the coarse edge (heights by linear
   interpolation along the edge, not `TerrainModel`), which removes T-junction cracks without skirts.
3. Estimated result for the current course: ~427k cells instead of 770k (55 %), with every playable vertex unchanged.
4. Keep the outer rectangle border at 0.8 m (or export its spacing as `Grid.BorderSpacing`): `pp_pebble.py`'s ring 0
   shares those vertices with the scenery.
5. Far chunks (beyond ~250 m from any tee/green camera) can additionally get a `LODGroup` with a 6.4 m level.

### Per-hole streaming

1. Bake each hole into its own additive scene (`Assets/_Game/Scenes/Holes/<id>.unity`, or an Addressables group)
   holding its `BakedHole` (ground chunks, surfaces, props, trees). The Main scene keeps the host, UI, shop, sky,
   ocean and the Blender scenery, which acts as the far LOD of every hole.
2. Precompute each hole's *visible neighbours* in the exported layout: holes whose boundary is within ~150 m, or seen
   from its tee or green (a coarse ray test against the scenery height field). Store them in `pebble_course.json` and
   in a small runtime asset.
3. Keep loaded: the current hole, its visible neighbours, and the next hole in the round (`CourseRouting` layout ids,
   since stand-in holes reuse their layout's visuals). Load the next set when the cup drops; the cart transition
   (`CartDrive`, 45-260 m of driving) hides the load. Unload with one hole of hysteresis.
4. `GameHost.EnsureHoleVisual` waits for the hole's scene instead of building a fallback; the fallback stays for
   missing or stale bakes. `Verify Baked Course` checks each hole scene's hash as it checks the prefabs today.
5. Cart paths are drawn per line (`CourseLayout.Lines`), so each spur can live in the scene of the hole it serves.

### Terrain scaling (later)

Give each hole only the features that reach its boundary plus margin (plus the shelves) instead of the whole course
list. That changes every hash (one rebake), and `pp_pebble.py` must then read the union list from the export rather
than `Holes[0]`. Bounds pruning makes this unnecessary until well past 18 holes.
