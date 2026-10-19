# Scratch notes: editor tools, art pipeline, tests

Working notes collected while writing [04-editor-tools.md](../04-editor-tools.md), [05-art-pipeline.md](../05-art-pipeline.md) and [07-testing.md](../07-testing.md). Raw material for [08-handover-notes.md](../08-handover-notes.md). Paths are relative to the repo root; line numbers are from the working tree on 2026-10-19.

## TODO / FIXME / HACK markers

- None. `grep -rnwE "TODO|FIXME|HACK|XXX"` over `Assets/_Game/Scripts`, `Assets/_Game/Tests`, `ArtSource/Blender/scripts`, `ArtSource/UI`, `ArtSource/Audio/tools` finds no markers. (A non-word grep for `HACK` only matches "PUTTSHACK".)

## Hardcoded machine-specific paths

### Will break on another machine (deliverable scripts; edit before running)

- `ArtSource/Blender/scripts/pp_lib.py:10` `ROOT = "/Users/calebsnyder/Real-Golf/ArtSource"` (also derives `BLEND_DIR`, `EXPORT_DIR`, `REVIEW_DIR`, `TEX_DIR`)
- `ArtSource/Blender/scripts/pp_pebble.py:29` `ROOT = "/Users/calebsnyder/Real-Golf"` (reads `pebble_course.json`, writes into `Assets/`)
- `ArtSource/Blender/scripts/pp_pebble_scenery.py:14` `ROOT = "/Users/calebsnyder/Real-Golf"`
- `ArtSource/Blender/scripts/pp_nature.py:36` `ROOT = "/Users/calebsnyder/Real-Golf"`
- `ArtSource/Blender/scripts/pp_course_props.py:27` `ROOT = "/Users/calebsnyder/Real-Golf"`
- `ArtSource/Blender/scripts/pp_golfer.py:24` `ROOT = "/Users/calebsnyder/Real-Golf"` (inherited by `pp_golfer_build.py`, `pp_golfer_bake.py`, `pp_golfer_export.py`, `pp_golfer_anim.py` through `G.ROOT`/`BLEND_DIR`)
- `ArtSource/Blender/scripts/pp_proshop.py:23` `HERE = "/Users/calebsnyder/Real-Golf/ArtSource/Blender/scripts"` (fallback only when `__file__` is undefined)
- `ArtSource/Blender/scripts/pp_proshop.py:29` `REAL = "/Users/calebsnyder/Real-Golf"` (source blend, FBX output dir, review dir, HDRI)
- `ArtSource/Blender/scripts/pp_stage.py:9` `HDRI = "/Users/calebsnyder/Real-Golf/ArtSource/Blender/textures/kloofendal_48d_partly_cloudy_puresky_2k.hdr"` (used by `pp_golfer_review.py`)
- `ArtSource/Blender/scripts/pp_mpfb.py:19` `CLOTHES_DIR = "/Users/calebsnyder/Library/Application Support/Blender/5.2/extensions/.user/user_default/mpfb/data/clothes/"` (legacy script)
- `ArtSource/Blender/scripts/pp_golfer_build.py:31` `MPFB_DATA = os.path.expanduser("~/Library/Application Support/Blender/5.2/extensions/.user/user_default/mpfb/data")` (not user-specific, but macOS-only and Blender-5.2-only; current golfer pipeline depends on it)
- `ArtSource/Blender/scripts/pp_bakeoff.py:17` `QM = "/tmp/cc0/umc/Ultimate Modular Men- Feb 2022/All together/Blends/Humans_Master.blend"` (legacy; external download expected in /tmp)
- `ArtSource/Blender/scripts/pp_cc0.py:17` `UBC = "/tmp/cc0/ubc/Universal Base Characters[Standard]/"` (legacy; same)
- `ArtSource/Blender/scripts/pp_golfer_anim.py:1836`, `:1840`, `:1847` render output under `/tmp/pp_anim/` (superseded procedural animation script)
- `ArtSource/Audio/tools/build_golf_audio.py:41` `CACHE = Path(os.environ.get("GOLF_AUDIO_CACHE", "/tmp/golfaudio"))` (env-overridable default; `/tmp` does not exist on Windows)
- `Assets/_Game/Scripts/Editor/CourseBaker.cs:154` `CapturePreviewBatch` default output `"/tmp/puttshack-verify"` (override with `PUTTSHACK_PREVIEW_DIR`)
- `Assets/_Game/Scripts/Editor/ProShopUIPreview.cs:32` `CaptureBatch` default output `"/tmp/proshop_ui_preview.png"` (override with `PUTTSHACK_UI_PREVIEW`)

### Documentation-only (in docstrings/comments; misleading but not executed)

- `ArtSource/Blender/scripts/pp_proshop.py:4` run line uses `/Applications/Blender.app/Contents/MacOS/Blender`
- `ArtSource/UI/make_proshop_ui.py:3` `Run: /tmp/pyenv/bin/python ...`
- `ArtSource/UI/HUD/make_hud_ui.py:4` `Run: /tmp/pyenv/bin/python ...`
- `ArtSource/Audio/tools/build_golf_audio.py:18-20` venv in `/tmp/golfaudio/venv`

### Inside binary files

- All `.blend` files are zstd-compressed; decompressed (`zstd -dc`) they contain `/Users/calebsnyder` (single occurrence each, likely a stored home/recent path) and some store render-output paths under `/Users/calebsnyder/Real-Golf/ArtSource/Review/...` (`PuttshackPro_Caleb_Modular.blend`, `PuttshackPro_Nature.blend`, `PuttshackPro_ProShop.blend`, `PuttshackPro_ReviewSet_WIP.blend`, `ArtSource/Clubs/PuttshackPro_Clubs.blend`). Harmless for the scripts (they set paths at run time); only matters when rendering from the Blender UI.
- No `/Users/...` paths in `Assets/**/*.{meta,asset,prefab,unity,mat}`, `ProjectSettings/` or `Packages/` (grep).

### In excluded folders (not deliverables; listed for completeness)

- `ArtSource/Backup/GolferFix/tools/waistline_probe.py:8` `sys.path.insert(0, "/Users/calebsnyder/Real-Golf/ArtSource/Blender/scripts")`
- `ArtSource/Review/Course/RESUME.md:20`, `ArtSource/Review/Course/after/review_log.txt:1`, `ArtSource/Review/Ball/RESUME.md:5-7`, `ArtSource/Review/Characters/RESUME.md:9,38,40-43` (references to `/tmp/rg-*` sandboxes and `/Users/calebsnyder/...`)

### Relocatable (compute paths from `__file__`; fine anywhere)

`pp_clubs.py`, `pp_mixamo_tpose.py`, `pp_stylized*.py`, `pp_pebble_terrain.py`, `test_terrain_parity.py`, `ArtSource/UI/make_proshop_ui.py`, `ArtSource/UI/HUD/make_hud_flat.py`, `ArtSource/UI/HUD/make_hud_ui.py`, `ArtSource/Audio/tools/build_golf_audio.py` (except the cache default).

## Risks and gotchas (editor tools)

- **Bake Pro Shop breaks the golfer prefab.** `ProShopBaker.BakeCaleb` re-saves `Prefabs/ProShop/Caleb_Golfer.prefab` from `Caleb_Modular.fbx` alone and `BakeAnimator` resets the controller's `Idle` to the static take. Maya, `GolferHandIK`, `GolferMotion`, hair styles and the Mixamo Idle are only restored by `Characters/Bake Golfers` (which itself calls `ProShopBaker.BakeAll` first). Until then `GolferCharacterTests` fails. Consider making the Bake Pro Shop menu call `GolferCharacterBaker.BakeAll`.
- **Bake Pro Shop needs `Main.unity`**: `PlaceInMainScene` opens it unconditionally and throws if missing (then `ProShop.unity` is not written either).
- **Build Main Scene is destructive**: new empty scene, Build Settings reset to `Main.unity` only, and `ConfigurePipeline` rewrites `Assets/Settings/PC_RPAsset.asset`, `Assets/Settings/PC_Renderer.asset` and `ProjectSettings/GraphicsSettings.asset` every time (this is why those files show up as modified after a bake). It throws if `Caleb_Golfer.prefab` or `PebbleBeach_Scenery.fbx` is missing.
- **Bake Course requires the HUD SDF fonts** (`HudFonts.CondensedBold` throws if `Art/HUD/Fonts/SDF/Ringside-Condensed-Bold SDF.asset` is missing); it does not call `HudFonts.EnsureAll`.
- **Font rebuild GUID chain**: `Pro Shop/Rebuild Fonts` alone leaves HUD SDF assets with a dangling fallback (inferred from code: `HudFonts.EnsureAll` only bakes missing assets). Run HUD/Rebuild Fonts after it, then re-bake all UI prefabs and holes.
- **Volume profiles are deleted and recreated** on every bake (`Course_PostVolume.asset`, `ProShop_PostVolume.asset`): new GUIDs each time; anything else referencing them breaks.
- **Bake Golfers deletes files**: unused textures in `Art/Characters/{Caleb,Maya,Shared}/Textures`, every clip FBX in `Art/Characters/Animations/` not named in `StateClips`, and every material in `Art/Characters/Materials/` it did not create. Do not hand-place assets there.
- **Scene opens without save prompt**: `Capture Course Preview` (opens Main) and `Characters/Render Review Stills` (opens ProShop) call `EditorSceneManager.OpenScene` directly.
- **Active lighting preset is hard-coded** (`CourseEnvironmentBaker.ActivePresetPath = GoldenHourPreset`); switching time of day means editing code and re-running Build Main Scene.
- **GI bake**: off by default; `PUTTSHACK_BAKE_GI=1` or the menu. Output in `Assets/_Game/Scenes/Main/` includes two ~40 MB `.bytes` files (`Main Baking Set-Default.CellData.bytes`, `...CellOptionalData.bytes`) in plain git.
- **`ProShopBaker.WritePreviewScene`** uses `Assets/_Game/Course/Baked/Materials/Sky.mat` as the preview skybox; no current code creates `Sky.mat` (leftover from an older bake). If it is deleted the preview scene simply has no skybox.
- **Postprocessors without `GetVersion()`** (`ClubImportRules`, `GolferTextureRules`, `CourseDataPostprocessor`): editing their rules does not trigger a reimport; reimport the folders by hand.
- **HUD sprites have no postprocessor**; `HudBaker.ConfigureSprites` applies their settings only when a UI bake runs. New sprites look wrong in the inspector until then.
- **`KenneyModelPostprocessor` still matches `Resources/Models/Course/**` and `Resources/Models/Nature/**`** although the Kenney kits are gone; later postprocessors override most of it. Its comments refer to `AuthoredModelPostprocessor` and `NatureTexturePostprocessor`'s comment to `BakedTexturePostprocessor`; neither class exists.
- **`NatureTexturePostprocessor` vs the current `Resources/Models/Nature/Stylized/` (Quaternius) FBXs**: it was written for Poly Haven scans (`_diff/_nor/_rough` textures in `Resources/Models/Nature/Textures/`, which does not exist). For every material it sets URP/Lit with `_BaseColor = white` and only binds textures that exist, so flat-coloured Quaternius materials may import white. **Not verified**; open one of those FBXs and check its materials.
- **Dead Resources content**: no runtime `Resources.Load` in `Assets/_Game/Scripts` reads `Resources/UI/` (Kenney UI pack handled by `UISpritePostprocessor`; its comment names `UIFactory`/`CourseSelectMenu`, which no longer exist). Everything under `Resources/` ships in player builds. Also `Assets/_Game/Art/Unused/ResourcesLegacy`.
- **Stale credits in Assets**: `Assets/_Game/Resources/Models/CREDITS.txt` lists `Trees/`, `Props/`, `Golf/` Kenney folders that no longer exist; `Resources/Models/Nature/CREDITS.txt` describes Poly Haven scans while the folder holds Quaternius `Stylized/`.
- **Agent hooks** poll every editor tick: `Temp/hud-bake.request`, `Temp/shop-ui.request` (`HudUIPreview`), `Temp/course-review.request` (`CourseReviewRequest`). Undocumented outside the code; harmless if unused. `CourseReviewCapture.Begin` and `ClubArtBaker.ShootInHands` have no callers.
- `GetComponent<T>() ?? AddComponent<T>()` on `UnityEngine.Object` (`CourseEnvironmentBaker.cs:356,366`, `GolferCharacterBaker.cs:627,647`, `ProShopBaker.cs:260`, `NatureTexturePostprocessor.cs:212`). `??` bypasses Unity's overloaded null check; it works today (the baked `Main.unity` does contain the sun's lens flare data), but `TryGetComponent` would be safer.
- Clubs menu items have no `priority`, so `Puttshack/Clubs` sorts at the default position.

## Risks and gotchas (art pipeline)

- **Two golfer pipelines write the same file** (`ArtSource/Exports/Characters/<Name>/<Name>_Modular.fbx`): MPFB (`pp_golfer_build.py`, current, consumed by the working-tree `GolferCharacterBaker`) and stylized (`pp_stylized_rig.py`, last commit `8592497`). Old stylized textures (`*_Head_Albedo.png`, `*_Head_Mask.png`, `*_Top_Mask.png`, `*_Bottom_Mask.png`, `*_skin.txt`) still sit in `ArtSource/Exports/Characters/<Name>/Textures/` and are ignored by the bake.
- **CREDITS.md is out of date**: it credits Hunyuan3D for the golfers (now MPFB/MakeHuman), and lists Mixamo clips `Golf_Drive_Swing`, `Golf_Drive_Medium`, `Golf_Putt_Swing` while the code uses `Golf_Drive_Standard`, `Golf_Chip_Swing2`, `Golf_Putt_Medium`. It does not mention clubs, audio, fonts, or the MakeHuman assets now shipped.
- **Licence gaps**: Ringside HUD fonts (no licence file); MakeHuman community assets used by the current golfers (`namuhekam_male_polo_shirt`, `toigo_basic_tucked_t-shirt`, `toigo_wool_pants`, `shoes05`, `short02`, `long01`, skins, eyebrows, eyelashes) not verified; Hunyuan3D licence excludes EU/UK/South Korea (only relevant if the stylized golfers return).
- **Mixamo raw FBX files are in the repo** (`ArtSource/Mixamo/YBot/` 50 files, `YBot_TPose/` 51; ~107 MB for `ArtSource/Mixamo`). CREDITS.md notes Mixamo forbids redistributing raw files on their own: keep the repo private.
- **No Git LFS** (`.gitattributes` marks binaries `binary` only): `.blend` files (`PuttshackPro_Caleb_Modular.blend` 57 MB, `PuttshackPro_PebbleBeach.blend` 21 MB, stylized blends 2-11 MB), FBXs, PNGs, audio and the GI `.bytes` all live in plain git history.
- **Unity YAML merge driver** (`merge=unityyamlmerge` in `.gitattributes`) does nothing until each developer configures it in their git config (see 01-getting-started.md).
- **pp_course_props exports need a manual copy** into `Assets/_Game/Resources/Models/Course/`; copying `Oak_*`/`Pine_*` would replace the LOD trees from `pp_nature.py`.
- **`pp_pebble.build_all(models=True)` has no CLI and would overwrite the pp_nature cypress** in `Resources/Models/Course/Cypress/`.
- **Undocumented manual steps**: Poly Haven JPG -> project PNG/HDR conversion for the course textures; origin of `Resources/Models/Course/Textures/{fairway,green,rough,deeprough,bunker}_albedo.png`, `{turf,sand,rough}_normal.png` and `Resources/Models/Course/Sky/*.hdr`; Mixamo downloads (manifest exists, downloader does not); how `pp_modular.py` was invoked (no `__main__`).
- **Stale docstrings/comments**: `pp_nature.py` header (material remap via `.meta` externalObjects to `Art/Course/Nature/Materials`, and `pp_pebble.py` reusing its tree meshes; neither true); `make_hud_ui.py` header ("HudImportRules"; borders live in `HudBaker.Borders`); `NatureArtImportRules.cs:11` mentions `pp_coast.py` (does not exist); `pp_golfer_build.py:28` mentions `pp_golfer_sync` (the copy is `GolferCharacterBaker.SyncStaged`); `pp_golfer_export.py` needs `PuttshackPro_GolferAnims.blend`, which does not exist; `pp_stylized.py` docstring lists only the `prep` stage (code also has `colors`).
- **Pro Shop display items come from the old Quaternius Caleb** (`pp_proshop.py` appends `PuttshackPro_Caleb_Modular.blend`), not from the current MPFB golfers; the Unity mannequins, however, use the current Caleb's posed meshes.
- **`pp_golfer_bake.py` re-tones albedo in place**; idempotence not verified.
- **`build_golf_audio.py`** deletes all `.wav`/`.ogg` in the audio subfolders before rebuilding (renamed outputs leave orphan `.meta` files), scrapes kenney.nl for zip links (fragile), and needs network access.
- **Duplicated tables that must stay in sync by hand**: 9-slice `BORDERS` (Python) vs `HudBaker.Borders` / `ProShopImportRules.BorderFor` (C#); hair/skin tints in `GolferCharacterBaker.Looks` vs `pp_golfer.py CHARS`; `pp_pebble_terrain.py` vs `TerrainModel.cs` (guarded by the parity test).
- `ArtSource/Blender/pebble_hole.json` is unused by any script.
- ArtSource is required in the Unity checkout: `Bake Golfers`, all `Clubs/*` steps and both UI capture menus read `ArtSource/` paths relative to the project folder.

## Risks and gotchas (tests)

- Batch test runs need the editor closed; they were not executed for these docs (editor had the project open).
- Asset-dependent tests (`BakedCourseTests`, `GolferCharacterTests`, `HudRenderTests`, `TerrainParityTests`, `AudioBankTests`, all PlayMode) fail when bakes are stale, by design.
- No CI, no code-coverage package; the 80 % coverage target in the global rules is not measurable today.
- `ArtSource/Review/*/RESUME.md` (gitignored) quotes older runs ("EditMode 96/96, PlayMode 2/2"); the suites have grown since (about 147 EditMode cases and 3 PlayMode tests by attribute count).

## Repository state at time of writing

- `git status` showed ~2600 entries (modified, deleted and untracked), including untracked deliverables: `ArtSource/Audio/`, `ArtSource/Clubs/`, `ArtSource/Course/`, `ArtSource/Exports/Course/`, most of `ArtSource/Mixamo/`, `ArtSource/UI/HUD/make_hud_flat.py` and `ArtSource/UI/HUD/Brand/**` (logo, wordmark, backdrops), and scripts `pp_clubs.py`, `pp_course_props.py`, `pp_golfer*.py`, `pp_nature*.py`, `pp_pebble_scenery.py`. These docs describe the working tree; make sure it is committed before handover.
