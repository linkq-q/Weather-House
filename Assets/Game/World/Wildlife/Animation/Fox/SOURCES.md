# Fox rest and eating animation sources

The game continues to use its existing fox model, texture, skeleton, vertex weights and locomotion. These files add animation to that skeleton. No rights in the original game model are transferred by this notice.

## Downloaded eating motion

Author: Quaternius. Original asset: Fox, Ultimate Animated Animal Pack.

- Author page: https://quaternius.com/packs/ultimateanimatedanimals.html
- Distribution page: https://poly.pizza/m/Bc97C66HKi
- License: CC0 1.0, https://creativecommons.org/publicdomain/zero/1.0/
- Original download SHA256: `9917771e1414c46557b011cbaaee6de82a827605008227a364c3862f0be62ddc`

The Eating take was downloaded and retargeted to the existing 32-bone fox. Fixed bone-length IK preserves contact. A disclosed final 0.25-second pose correction closes a measured discontinuity in the source take. Duration remains 2.5 seconds. This source contains no native sleeping animation.

## Derived lying-rest sequence

The lie/sleep pose configuration and sampling code come from lhns/steam-frame-nix, commit `881c1de6a9e813b7c269be80a9bf91baf588f5f2`, under Apache License 2.0. The upstream fox model remains Quaternius CC0.

- Repository: https://github.com/lhns/steam-frame-nix/tree/881c1de6a9e813b7c269be80a9bf91baf588f5f2
- Source files: `modules/pet/models/fox/model.json`, `modules/pet/bake_gltf.py`, `modules/pet/rig.py`
- The complete Apache 2.0 license accompanies this notice as `APACHE-2.0.txt`.

Changes made for Weather House: bake source poses to clips; ground each source pose; author a separate staggered rise; retarget to the unchanged existing 32-bone skeleton; adapt the desktop-pet pose to supported paws and a belly near the ground; preserve the accepted S-shaped tail by applying source tail motion relative to its Idle pose; add a 2.5 mm breathing body rise because this skeleton has no separate chest chain. The three clips are lying down, lying rest with breathing, and getting up. The original model has no eyelid channel: this is eyes-open rest, not a claim of closed-eye sleep.

Original downloads, build scripts, raw source samples and validation reports are retained in `JourneyReview/Revision_20261024/AnimationInstall`.


## 2026-10-25 relaxed resting posture refinement

The original fox rig, bone lengths, mesh, UVs, vertex weights and paw targets remain unchanged. The target hind-leg bend plane folds toward the front and side instead of arching upward. During the three rest takes the torso eases by up to 14 degrees around the front-shoulder pivot to lower the haunches; measured belly support and fixed-length limb solving retain floor contact. This is an explicit local pose adaptation of the credited source poses, not an additional downloaded native sleeping clip. The full breathing loop and matching down/up endpoints were regenerated together. Original models and source downloads remain preserved.
