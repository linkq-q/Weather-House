Gobkit Free Animal Pack Vol.7 — 10 CC0 rigged & animated low-poly animals (GLB)
https://gobkit.com/freebies

Animals: Archerfish, Kingfisher, Zebra foal, Poitou donkey, Bird-of-paradise, Cape ground squirrel, Brahma Chicken, Eland, Tarsier, Moorish idol
Each .glb is one model with its texture baked in, plus the shared atlas Texture001.png.

Animation: 4 clips per model @ 24 fps:
  idle (loop) | attack (loop) | dead (play once, hold) | walk (loop)
  Play by name — do not slice by frame range.

Three.js quick start:
  import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
  new GLTFLoader().load('Archerfish.glb', (g) => { scene.add(g.scene);
    const mixer = new THREE.AnimationMixer(g.scene);
    mixer.clipAction(THREE.AnimationClip.findByName(g.animations, 'walk')).play(); });

How to put it in your game
--------------------------
Each .glb holds the whole model: one mesh, its skeleton, its texture and its clips. Y is up, the model faces +Z and
stands on its origin. Sizes are in glTF units (1 unit = 1 metre in Unity, Godot and Unreal); scale each model to the
size your game needs. Play the clips that come with each model, by name.

  Unity     Package Manager > + > Add package by name > com.unity.cloud.gltfast, then drop the .glb into Assets.
            In its Inspector set Animation Method to Mecanim (Animator) or Legacy (Animation); the clips keep their names.
  Godot 4   Drop the .glb into the FileSystem dock and drag it into a scene; the clips are in its AnimationPlayer:
            $AnimationPlayer.play("walk"). To loop a clip: select the .glb > Import dock > Advanced... > the clip >
            Loop Mode: Linear > Reimport.
  Unreal 5  Drag the .glb into the Content Browser (glTF import is built in). On 5.8 set Skeletal Meshes > Combine
            Skeletal Meshes to "Combine All That Use Same Skeleton".
  Blender   File > Import > glTF 2.0 (.glb/.gltf); the clips are Actions (Dope Sheet > Action Editor).
  Three.js  The quick start above; to make a model h units tall:
            const b = new THREE.Box3().setFromObject(g.scene, true); g.scene.scale.multiplyScalar(h / (b.max.y - b.min.y));

Sizes in the rest pose (height x width x length, units):
  Archerfish 3.8 x 4.1 x 5.5  |  Bird-of-paradise 4.0 x 3.8 x 5.4  |  BrahmaChicken 4.4 x 3.6 x 4.6
  Capegroundsquirrel 5.1 x 3.5 x 4.3  |  Eland 5.3 x 4.0 x 4.4  |  Kingfisher 3.9 x 3.7 x 5.4
  Moorishidol 4.5 x 4.0 x 5.2  |  Poitoudonkey 5.1 x 3.9 x 4.7  |  Tarsier 4.0 x 4.6 x 5.0
  Zebrafoal 4.6 x 3.6 x 4.7

License: CC0 1.0 (public domain) — any use, commercial or personal, no attribution required.
More free CC0 assets: https://gobkit.com/freebies
