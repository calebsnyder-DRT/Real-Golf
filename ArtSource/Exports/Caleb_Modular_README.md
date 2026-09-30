# Caleb — modular golfer (Puttshack Pro)

One skinned rig, swappable clothing parts, colour variants as materials.
Source: `ArtSource/Blender/PuttshackPro_Caleb_Modular.blend` (scene `PP_CC0`, collection `PP_Caleb_Modular`).
Builder script: `ArtSource/Blender/scripts/pp_modular.py`.

## Files

| File | Contents |
|---|---|
| `Caleb_Modular.fbx` / `.glb` | Rig + **every** part + `Caleb_MaterialSwatches` (carries all variant materials) + clips |
| `Caleb_ClassicPolo.fbx` | Blue polo, khaki chinos, brown belt, white/cyan shoes |
| `Caleb_SummerTee.fbx` | White tee, navy shorts, white crew socks |
| `Caleb_GreenClub.fbx` | Green polo, white chinos, black belt, black shoes |
| `Caleb_TealCoral.fbx` | Teal polo, coral shorts, white belt, cyan crew socks |

The preset FBXs contain only the visible parts and their materials.

## Specs

- Height 1.815 m, feet at y = 0, origin at the feet, facing +Z in Unity (Blender −Y).
- Rest pose: T-pose. Scale 1.0 (exported with *FBX Units Scale*, Forward −Z, Up Y, no leaf bones, deform bones only).
- 60 bones, all deform. Hierarchy: `Root > Body > Hips > (Abdomen > Torso > Chest > Neck > Head | Shoulder > UpperArm > LowerArm > Wrist > fingers)`, `Hips > UpperLeg > LowerLeg > Foot`. The original pack's IK controls (`PT.L/R` pole targets, feet parented to Root) were removed; clips were re-baked to FK.
- Max 4 bone influences per vertex, normalized.
- Flat-colour materials (no textures), so there's nothing to copy alongside the FBX.

## Parts (one SkinnedMeshRenderer each)

| Slot | Part | Tris | Material slots (in order) |
|---|---|---|---|
| always on | `Head_Caleb` | 4,219 | Skin, Hair, Eye |
| always on | `Body_Arms` (arms + hands) | 4,156 | Skin |
| always on | `Shoes_Golf` | 2,208 | Shoes, ShoeAccent, Sole, Socks (inner liner shows above the shoe) |
| Top | `Top_Polo` (collar + buttons + logo) | 4,484 | Top, Collar, Logo |
| Top | `Top_Tee` (no collar) | 3,172 | Top, Logo |
| Bottom | `Bottom_Chinos` | 2,988 | Bottom |
| Bottom | `Bottom_Shorts` (includes bare lower legs) | 3,052 | Skin, Bottom |
| Socks (optional) | `Socks_Ankle` | 432 | Socks |
| Socks (optional) | `Socks_Crew` | 736 | Socks |
| Belt (optional) | `Belt` (with buckle; fits both bottoms) | 1,728 | Belt, Buckle |
| helper | `Caleb_MaterialSwatches` | 70 | every `Mat_*` — **disable or delete in the prefab** |

Enable exactly one Top and one Bottom. Socks and Belt are optional (0 or 1 each).
Assembled combos run 17.5k–20.6k tris (budget 25k).

## Variant materials

| Group | Variants |
|---|---|
| `Mat_Top_*` | Blue, White, Green, Purple, Gold, Teal |
| `Mat_Collar_*` | White, Navy |
| `Mat_Bottom_*` | Khaki, Navy, White, Grey, Black, Coral |
| `Mat_Shoes_*` | White, Black |
| `Mat_ShoeAccent_*` | Cyan, White, Black, Grey |
| `Mat_Socks_*` | White, Black, Cyan, Navy |
| `Mat_Belt_*` | Brown, Black, White |
| `Mat_Logo_*` | Cyan, Orange |
| fixed | `Mat_Skin_Caleb`, `Mat_Hair_Caleb`, `Mat_Eye_Dark`, `Mat_Sole_Dark`, `Mat_Buckle_Silver` |

`Mat_Socks_*` is used by both the sock parts and the shoe's inner liner, so swap it on both to keep them matching.

## Unity setup

1. Import `Caleb_Modular.fbx`. In **Rig**, set Animation Type = **Humanoid**, Avatar = *Create From This Model*, then **Configure** and confirm the mapping:
   - `Hips` → Hips; `Abdomen` → Spine; `Torso` → Chest; `Chest` → UpperChest; `Neck`, `Head`.
   - `Shoulder.L/R` → Shoulder; `UpperArm` → Upper Arm; `LowerArm` → Lower Arm; `Wrist` → Hand.
   - `UpperLeg`/`LowerLeg`/`Foot` → leg bones; Toes are empty (optional).
   - Fingers: `Thumb1-3`, `Index1-3`, etc.
2. In **Materials**, click *Extract Materials…* into a folder so they become editable/shareable assets. Convert them to URP/Lit if needed; they're plain base colour + smoothness.
3. In **Animation**, the takes are `Caleb_Rig|Idle` (0–50), `Caleb_Rig|Walk` (0–40) and `Caleb_Rig|Wave` (0–50). Rename them to `Idle`/`Walk`/`Wave` and tick *Loop Time* on Idle and Walk.
4. Drag the model into a scene, disable `Caleb_MaterialSwatches`, and save a prefab.

### Swapping at runtime

```csharp
// parts: enable one per slot
topPolo.gameObject.SetActive(wantCollar);
topTee.gameObject.SetActive(!wantCollar);

// colours: replace the material in the slot listed above
var mats = topPolo.sharedMaterials;   // [Top, Collar, Logo]
mats[0] = topGreen;                   // Mat_Top_Green
topPolo.sharedMaterials = mats;
```

All parts share the same bones, so any combination animates together with no extra setup.
