# ArtSource credits

Every third-party asset used in `ArtSource/` renders and exports, with source and licence.

## Shipped in exports

| Asset | Used for | Source | Licence |
|---|---|---|---|
| Quaternius — Ultimate Modular Men (Feb 2022) | Caleb base meshes (`Casual2_Body/Legs/Feet`, `Casual_Legs`, `Casual_Head`), `CharacterArmature` rig, Idle/Walk/Wave clips. Restyled, recoloured, re-rigged to FK, split into modular parts. | https://quaternius.com/packs/ultimatemodularcharacters.html (Google Drive folder linked from that page) | CC0 1.0 |

Everything else in the Caleb exports (collar, buttons, logo, belt, buckle, socks, colour variants, shoe accent split) was authored in Blender for this project.

## Used for review renders only (not in exports)

| Asset | Used for | Source | Licence |
|---|---|---|---|
| Poly Haven — Kloofendal 48d Partly Cloudy (Pure Sky), 2k HDR | Sky and lighting in `Review/*.png` | https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky | CC0 1.0 |

## Evaluated, not used in current deliverables

These were downloaded for experiments and aren't part of any export:

| Asset | Source | Licence |
|---|---|---|
| Quaternius — Universal Base Characters | https://quaternius.itch.io/universal-base-characters | CC0 1.0 |
| Quaternius — Universal Animation Library | https://quaternius.itch.io/universal-animation-library | CC0 1.0 |
| Kenney — Mini Characters | https://kenney.nl/assets/mini-characters | CC0 1.0 |
| MPFB 2 (Blender extension) | https://extensions.blender.org/add-ons/mpfb/ | GPL-3.0-or-later (tool; its output is not GPL) |
| MakeHuman asset packs (system assets, shirts01, pants01, shoes01, hats01, clothes materials) | http://files.makehumancommunity.org/asset_packs/ | CC0 1.0 |

## Existing project assets (read-only reference)

The Kenney Minigolf/Nature/Furniture kits and the Quaternius Stylized Nature MegaKit under `Assets/_Game/Resources/Models/` are credited in their own `CREDITS.txt` files there.

## Pro Shop (`Assets/_Game/Art/ProShop/Models/`, `Blender/PuttshackPro_ProShop.blend`)

All Pro Shop geometry is procedurally modelled by `Blender/scripts/pp_proshop*.py`: the room, the course outside the windows, the props, the folded stacks, socks, belt, clubs and all three golf bags. No new third-party assets were downloaded.

| Export | Derived from | Licence |
|---|---|---|
| `Display_Polo_Hanger`, `Display_Tee_Hanger`, `Display_Shoes_Pair` | Caleb modular parts (`Top_Polo`, `Top_Tee`, `Shoes_Golf`), which build on Quaternius Ultimate Modular Men (see above). Posed, flattened and re-meshed for display. | CC0 1.0 |
| `Display_MannequinHead` | Lathe profile sized from the Caleb head proportions | CC0 1.0 |

The Pro Shop review renders under `Review/ProShop/blender/` use the Poly Haven Kloofendal HDRI listed above as the sky (CC0 1.0).
