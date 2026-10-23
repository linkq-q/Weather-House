# EmaceArt Roadside Tales Free — Modular Bridge

Official author and download page: https://emaceart.itch.io/roadside-tales-free-modular-bridge-narrative-pack

Retrieved locally: 2026-10-24, Asia/Hong_Kong. The public free-download flow required no account, payment, or personal data. Downloaded upload 18733587, `FBX No Lods.zip` (474395 bytes). Only `.fbx` entries were extracted from that archive. No Unity package, tool scripts, add-ons, or executables were imported or run.

Archive SHA-256: `CAC38A6D0DC6A30B8ADB76B2A2228E1AA328830609F0870179E5F5CD3E5F8AE1`.

The official page states “No generative AI was used.” This is an author statement, not independent provenance analysis.

## Author license and source

> EmacEArt Asset License - free to use in your own projects, personal and commercial alike. No credit is required

The remaining terms on the official page welcome optional credit and prohibit resale or redistribution as standalone files or as part of another asset pack. The source URL above contains the complete author wording.

This is the author's own asset license, not CC0. Keep these source assets inside the game project; do not publish them as a reusable model pack. The source page describes Unity Built-in support; URP materials and exact bridge fit need local verification.

The downloaded archive contains 23 static FBX files named `EA03_Prop_Pier_*`, and no separate texture files or tool scripts. The source-page count of 21 prefabs is therefore not treated as a verified FBX count. Model geometry, colors and actual span dimensions must be checked before replacing a crossing.

`model_inventory.json` was measured with Blender 5.1.1 FBX import in background mode, with automatic script execution disabled. Its bounds are in Blender X/Y/Z coordinates, where Z is vertical; they are not Unity X/Y/Z bounds. The inventory is a geometry/material audit, not a Unity visual acceptance result.

The FBX meshes contain 152–1198 triangles per file, no vertex-color attributes and no image references. Their material names refer to `EA02_LPNP-Slavika_Colorsheet_v1.png`. Most straight pieces are approximately 6.6–7 m long; `EA03_Prop_Pier_05a.fbx` is 13.65 m long. Corner pieces have an L-shaped footprint, so their longest bounding dimension is not a straight crossing span.

The official `glb.zip` (upload 18825005) was also acquired to check for the missing image. SHA-256: `BED93DE64E11FF02077A383566E5005788CD980AF31B06AC472AE87719E69E33`. Inspection of `EA03_Prop_Pier_05a.glb` found no images or textures and an empty PBR definition; it does not solve the missing palette. The GLB files were not extracted into the Unity project.

## Original palette recovered

The official free Unity archive (upload 18733586, 1263895 bytes) was downloaded to a temporary directory and inspected as a gzip/tar archive without importing it into Unity. Archive SHA-256: `CABFFEDF8D3760FD908BF36613040BF22E6D9069DF30DAB0992E898B482B07BD`.

Only one PNG was extracted: `Assets/EmaceArt/Roadside Tales Bridges/Textures/EA02_LPNP-Slavika_Colorsheet_v1.png`. It is now alongside the FBX files, byte-identical to the author's image (1509 bytes; SHA-256 `5a31019eca65cbbc9346ed9441d7bb660853e6eef72fce823225e36bf2cc0b97`). `package_image_audit.json` lists all original package paths and the extracted image. No scripts were present, and no package prefabs, materials, editor settings or code were imported.

Use this original palette with the existing FBX UVs in a new URP material. This restores the author's texture rather than inventing a replacement. The bridge family is flat-palette low-poly, so compare it with HIGHLANDS' painterly timber before final placement.
