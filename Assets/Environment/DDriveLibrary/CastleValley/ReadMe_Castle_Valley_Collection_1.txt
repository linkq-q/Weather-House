THANKS for your support!

Thank you for purchasing Castle Valley Collection #1!  These rock formations were scanned in the San Rafael Swell, a heavily eroded upwelling of Jurrasic sandstone in central Utah, not far from Moab.  Thanks goes out to the folks at 3DF Zephyr and 3D Coat and Unity for their great software.  Thanks also goes to my pack goats Shelby GT, Woodstock, Bacchus, Vincent VanGoat, Barry Goatalo, and also Luna the German Shep, for their assistance carrying equipment out in the desert.  Collection #2 is well underway and will compliment and be compatible with Collection #1.

----------------------------

SUPPORT

Web Page:    http://www.goatogrammetry.com/
Email:  goatogrammetry@gmail.com

----------------------------



----------------------------

SETUP

This version of Castle Valley Collection #1 is meant for Unity's default standard render pipeline.  Because there are no scripts or settings that will override or interfere with any of your existing work, it should be safe to install it directly into your current project.

A scene in the Demo directory "Castle Valley Layout Scene" will show all of the prefabs and tiling textures in this pack lined up in rows.  There is also a PVP style demo map with baked lighting.  To save space, feel free to delete it when you've finished looking it over.  

If you are not using URP, you can delete the URP.zip to save space

****************************
URP USERS!  Instructions for URP conversion are at the bottom of this file.  Dont let Unity try to convert materials on its own-- Please follow the instructions below.
****************************

Thats it for setup!

-----------------------------

TIPS:

* PBR shaders require reflection probes to work.  If you don't have probes added (and also baked), your cliffs and rocks will look too glossy or plastic-like as they reflect the skybox rather than what is actually around them.  Often the skybox has bright colors even on the lower half, which will reflect upwards onto the undersides of shadowy overhangs, giving weird "plastic" looking results.  If you drop some cliffs from this pack into a new, empty scene, and the shadows are harsh and very black, its because there is no ambient lighting at all.  Once the scene has correct lighting, you'll be pleasantly surprised at the improved realism of these scanned assets.

* Some of the tiniest rocks and plants get their ambient lighting from light probes rather than baked lightmaps.  If you do not have your light probes set up,  you may find these prefabs are rendering too dark or bright.  When you add light probes, be sure to place them near any prefabs that need them, especially if there is a strong shadow boundary in the area. 

* Keep in mind that prefabs that use the "Cliff" prefix are very large and look best with a little distance, even though they use a 2nd 'detail texture' to enhance the micro-surface.  Because of their large size, their pixel density is lower than the other, smaller prefabs.  They look fine up close, but due to their scale there's no reasonable way to make them look quite as detailed as some of the other, smaller items when the camera is very close to the surface.

-----------------------------


LOD (Level Of Detail) or "LOD Group" component:

* All meshes come with 4 or more hand-adjusted LODs built in.  

* As a rule, LOD1 has half the triangle count as LOD0, etc.  

* Remember that you can adjust global LOD distance ratio in your project settings (LOD bias).

* Special Note:  You may see a problem with the baked lighting on LODs in older versions of Unity using Progressive GPU-- See the Light Maps section below for details.

-----------------------------

COLLIDERS

* All prefabs contain custom-made low poly, but accurate, convex mesh colliders.

* The colliders are not contained within the rock's .fbx file, but rather in Meshes/Colliders.

* The physics material for Sandstone is in Meshes/Colliders.  I just guessed on the values, so adjust as you see fit.

-----------------------------

MATERIALS AND TEXTURES

* Sometimes different meshes share the same material's bitmap for optimization.

* Mesh filenames have a naming convention that links them to their matching material.  

* An example:  Mesh_Boulder_01_AJ.FBX uses Material_AJ_01.mat which gets its colors from AJ_Color_01.png (Note the AJ in each)

* Color & Normal textures are generally 4096x4096.  The "smoothness" grayscale is in the color(albedo) alpha channel.

* Occlusion maps are generally lower resolution to save video memory, since occlusion isn't usually ultra detailed.

* All the rock materials use a detail texture.

-----------------------------

LIGHT MAPS

* Any meshes that share the same texture have a 2nd UV set for light maps.  This 2nd UV set fills the whole UV space to prevent wasted texels in the lightmaps.

* Some of the tiniest rocks, plants, and the tree are set to take their lighting from the light probes rather than light maps.  Its easy to change, but they're set this way to prevent crazy-long bake times and huge lightmap data.

* All of the "Cliff" prefabs have their light mapping scale set to 1/3 normal texel density due to their large size.  Because you're dealing with large meshes, its safe to reduce this to even smaller fractions and still have a great looking final light-bake!

* "Cliff" prefabs often have "Cap" meshes on the top and back.  These do not bake light maps and use light probes instead.

* "Cap" meshes on cliffs are meant to help cast shadows but are not really made to be seen.  Hide them with capstones etc if necessary.

* Sometimes the cliff's cap meshes will throw warnings from the light baker, saying they have overlapping UVs.  Ignore this, since the UV size is set to 0 (not meant to be seen) and is thus ignored by the indirect light baking algorithm entirely.  
-----------------------------

TERRAIN

* When adding "Terrain Layers" (materials) to paint on the terrain, you'll find them in the Materials/Tiling directory.

-----------------------------------

UPDATE HISTORY


Version 1.8 Final large update for CVC#1

New "Assemblies" can be found in the Prefab directory.  These are nested prefabs that let you drag and drop entire mountains or other formations into your scene allowing you to create a large map in minutes.  This is how CVC#1 was always meant to be used!  

You can now TINT all of the rocks in a scene at once using the "CastleValleyGlobalShaderControl.cs" script found in the materials/shaders directory.  This feature replaces all of the rock materials with versions that use an Amplify Shader that looks at the script (placed on a dummy object etc. anywhere in the scene).  To install this feature, first be sure that if you are using URP, you have already installed the URP zip.  Second, find the "Materials_Scripted_Tint_Shaders.zip" in the Castle Valley Collection 1 directory and "Extract files here".  Allow the old files to be overwritten if prompted.  Your rocks may look black at first because you need to add the script in the scene to tell the shaders what color to use.  You can control the "Global Tint" which is actually a 'soft-light' tint operation.  "Global Smooth" will multiply against the texture's existing roughness values, making shinier things even shinier.  "Global Contrast" helps control aspects of brightening or darkening the texture that the Global Tint cant handle.  If you have Amplify Shader Editor, you will find its easy to adjust the shader to get other, perhaps even more useful effects.  To UNDO the scripted shaders, I've included the Materials_Default_Unity_Shaders.zip to allow you to quickly revert to the old default shaders.  Its easy and harmless to switch back and forth!

LODs have been improved on some of the more important formations.  Feel free to adjust the prefab's LOD Components to change even closer to the camera now!  Nobody will notice and you'll save a lot of triangles.  The cost, though, is a few more materials and normal maps, but its worth it!  I actually did add some new geometry, so you'll want to re-bake any light-maps.  

I cut up the juniper tree so you can use the branches as smaller trees.

Decals work in URP now.  

The vulture's script got a fix for a missing time*delta-time line of code that made them zoom around way too fast if the frame-rate was high.  

-----------------------------------

Version 1.5  Improvements everywhere!

Many of the rock formations have new versions that have been distorted to have more slope, taper, or other useful shapes.  For instance, some pillars now have curved versions that make them useful for creating arches.  Cliffs now have sloped versions that let you move away from purely vertical sandstone ledges.  You'll love these new shapes!  See them all in the layout scene in the "Demo" directory.

New detail textures!  You'll now see 5 different detail textures applied to the various materials to enhance the rocks up close.  These new detail textures were created using high quality surface scanning techniques, so they're more authentic feeling than before.

A few adjustments were made to some of the lightmap scale values in some of the prefabs.  You should probably re-bake your lighting if you're still using baked lights.

More decals have been added, but all decals have been moved into the URP zip, since decals are not actually supported for the Standard Pipeline. 

LOD improvements have been made to many of the most important rock formations, such as the main cliffs.  To reduce popping, lower poly LODs were given their own normal maps that actually match the mesh geometry rather than re-using LOD0's normal texture.  This makes LOD transitions almost invisible and lets you switch to lower poly LODs closer to the camera, but costs some extra state-changes on the video card, and a little video memory.  You can always revert back to the un-improved LODs by dropping LOD0's material on all the LODs in the LOD Group.

-------------------------------------

Version 1.1

Converted from 2019.3 to be compatible with 2018.4

Cliff_Corner_03's prefab somehow had the mesh offset from the colliders by .4 meters.  Its fixed, but you'll want to make sure it didnt introduce a gap in your own maps.  If so, just move it a bit.

Special Note:  If you're converting from the Standard 1.0 to 1.1+, or to HDRP or back, you may have to change the meta ID# for the vulture, obstacle1, rabbit brush, and living tree prefabs to match.  I had to rebuild them from scratch and I forgot to paste-in the right prefab ID, and now people have been using the new patch, so its kinda out of hand.  Sorry about that!

-----------------------------

All prefabs with problems with the UV2 (baked lighting) problems are fixed.  But since UVs got changed, you'll have to re-bake your maps :(  Sorry!

By popular demand, I added an example scene (sort of a PVP deathmatch map) found in the demo directory.  I suggest adding the simple camera controller script that comes with HDRP so you can move around in play mode.  Progressive lightmapper choked and died so I used Bakery.

The sage brush and trees have been changed to NOT be static.  I found out static terrain trees are a no-no.  All plants will take their lighting from probes.  Also Sage's most distant LOD3 no longer casts shadows just for optimization's sake.

I added a version of the sage brush with the vertex normals pointing upward.  This evens out the shading on the branches.  It may or may not be an improvement for your scenes, so try it out!  Also, I've added more shader options for the sage brush.  There's now a version that randomizes the tint between 2 colors based on location, and also a transmission shader (based on the example Amplify Shader with my branch movement added).  Some of these shaders cause some artifacting with the forward render path's ambient occlusion post FX, but I guess thats normal and you can try them out and see which work best for your project.

Obstacle 10-13's albedo texture was slightly too bright and didn't match the rest perfectly.  Its been adjusted.

I've turned off the vulture script's debug message flag.  I left it on originally-- Annoying!

Baked occlusion sliders for the ledge and rock pile materials has been lowered slightly-- It was too intense originally.

Remember to leave a review!  Thanks!!!




****************************
URP Instructions:

There is a zip file called "URP.zip" in the "Castle Valley Collection 1" directory.  Unzip it into your "Assets" directory so it can overwrite some of the files in the "Castle Valley Collection 1" directory.  

Here's what its going to do:

Overwrite the whole "Materials" directory with the new URP versions.  This is going to include shaders for the plant leaves.

Add the decal textures and materials.

Overwrite the detail textures.  Apparently URP centers the detail shaders on 192 gray rather than 128 gray like Standard Pipeline and HDRP.  Until now the rocks would draw too dark in URP.  This will fix it and look identical to the other pipelines.  

Now you need to make sure you activate the decals and decal layers:

https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@13.1/manual/urp-renderer-feature-how-to-add.html

****************************
