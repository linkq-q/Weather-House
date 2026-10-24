# Dog Eating animation source

Original animation: Eating, 2.6667 seconds, by Quaternius, CC0 1.0.
Author model page: https://poly.pizza/m/wcWiuEqwzq
Pack: https://quaternius.com/packs/ultimateanimatedanimals.html
License: https://creativecommons.org/publicdomain/zero/1.0/
Original downloaded GLB SHA256: a1e107ec4c2c62ce8cad86f750a7f6f9f2065c691dc6f88fce39667d3f4877eb

Adaptation: retargeted onto the project's current 36-bone dog, retaining model, UVs, weights, bone hierarchy and S-tail. Existing Idle tail plus source motion deltas; fixed-length contact correction. Body rotations and body/foot travel are adapted to 35% of the reference amplitude around the current accepted Idle pose; the final head joint retains the authored Eating rhythm. This target-specific reduction improves the standalone clip pose. The existing mixer still needs the opt-in WildlifeForageContact foot correction during entry and exit; it retains the original blend timing and bone lengths. Final head joint bend is limited only as needed to keep the longer target muzzle above the floor. A final-quarter-second loop closure is explicit; source Eating already has matching endpoint meshes.

The downloaded source and original target remain unchanged. Only the animation clip is used; do not instantiate this FBX's duplicate mesh. Offline evidence: AdditionalSources/DogEating/dense-verification-v3.json and ReviewV3/dog_eating_review.jpg. Controlled Unity Play evidence: CandidateV3/Unity/20261025-014753/runtime-pass.json. Main-map natural-AI evidence: CandidateV3/MainRuns/20261024_193947_424/sample.json. All three original dogs entered and exited Forage without forcing states or moving their roots. Runtime blend/contact limits and remaining original Idle/crowding observations are recorded in Docs/WildlifeJourney/DogEatingVerification.json.
