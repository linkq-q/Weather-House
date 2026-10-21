#ifndef ZZZ_CHARACTER_FACE_LIGHTING_INCLUDED
#define ZZZ_CHARACTER_FACE_LIGHTING_INCLUDED

// The head frame follows the head bone: metres, +X right, +Y up, +Z forward.
// These contours are fitted to this mesh; they are not a recovered or authored SDF.
float2 CharacterFaceUV(float2 uv, float noseShift)
{
    // The painted nose sits 0.007 UV below the mesh tip. Move only this patch.
    float2 nose = (uv - float2(0.5, 0.5203)) / float2(0.020, 0.031);
    float weight = 1.0 - smoothstep(0.55, 1.0, length(nose));
    return uv - float2(0.0, noseShift * weight);
}

half CharacterFaceShadow(float3 positionHead, float3 lightHead)
{
    float3 light = SafeNormalize(lightHead);
    float horizontalLength = max(length(light.xz), 0.0001);
    float2 horizontalLight = light.xz / horizontalLength;
    float lateral = abs(horizontalLight.x);
    float shadowSide = positionHead.x * -sign(horizontalLight.x);

    // Fit the lower jaw boundary, not an isolated region next to the eye.
    float jawX = abs(positionHead.x);
    float jawY = -0.0247 + 0.26 * jawX + 8.0 * jawX * jawX;
    float insideJaw = positionHead.y - jawY;
    float cheek = 1.0 - smoothstep(0.002, 0.012, insideJaw);
    cheek *= smoothstep(0.011, 0.028, jawX);
    cheek *= 1.0 - smoothstep(0.054, 0.066, jawX);
    cheek *= 1.0 - smoothstep(0.010, 0.022, positionHead.y);
    cheek *= smoothstep(-0.008, 0.016, shadowSide);
    cheek *= smoothstep(0.03, 0.25, lateral) * 0.65;

    // A smooth head-space face plane keeps animation and light rotation independent of mesh facets.
    float faceX = positionHead.x / 0.062;
    float faceY = (positionHead.y - 0.028) / 0.10;
    float3 faceNormal = SafeNormalize(float3(faceX, faceY * 0.55,
        sqrt(max(0.16, 1.0 - 0.72 * faceX * faceX))));
    // Preserve a rounded lit area around the mouth instead of bisecting the face with a straight line.
    float centerFill = 0.16 + 0.10 * (1.0 - smoothstep(0.004, 0.030, abs(positionHead.y - 0.008)));
    float directional = 1.0 - smoothstep(-0.055, 0.055, dot(faceNormal, light) + centerFill);
    float faceRegion = smoothstep(-0.010, 0.020, positionHead.z);
    faceRegion *= smoothstep(-0.033, -0.020, positionHead.y);
    float verticalLight = smoothstep(0.05, 0.25, horizontalLength);
    return saturate(max(cheek * verticalLight, directional) * faceRegion);
}

// Optional authored angle-threshold map. R is the required frontness (0..1),
// A blends the authored face region over the analytic fallback; G/B are unused.
// The unmirrored map is authored for a light on head +X. UVs stay separate from
// the painted-nose UV adjustment. A near-vertical light uses the old solution.
half CharacterFaceThresholdShadow(float2 uv, float3 lightHead, half fallback,
    TEXTURE2D_PARAM(sdfMap, sdfSampler), half strength, half softness, half swapSides)
{
    float3 light = SafeNormalize(lightHead);
    float horizontalLength = length(light.xz);
    float frontness = light.z / max(horizontalLength, 0.0001) * 0.5 + 0.5;
    bool mirror = light.x < 0.0;
    if (swapSides > 0.5h) mirror = !mirror;
    if (mirror) uv.x = 1.0 - uv.x;
    half4 data = SAMPLE_TEXTURE2D(sdfMap, sdfSampler, uv);
    half feather = max(softness, 0.0001h);
    // Extend the comparison at the endpoints so frontal/back light is fully
    // lit/shaded even where the authored threshold is exactly 1/0.
    frontness = lerp(-feather, 1.0h + feather, saturate(frontness));
    half shadow = 1.0h - smoothstep(data.r - feather, data.r + feather, frontness);
    half weight = saturate(strength) * data.a * smoothstep(0.05, 0.25, horizontalLength);
    return lerp(fallback, shadow, weight);
}

half CharacterFaceNoseShadow(float3 positionHead, float3 lightHead)
{
    // Anchor an upward half-ellipse to the measured nose tip and keep it on the unlit side.
    float2 fromTip = positionHead.xy - float2(0.0, 0.028702);
    float3 light = SafeNormalize(lightHead);
    // Let the single hard shape disappear before switching sides across the front light axis.
    float sideVisibility = smoothstep(0.025, 0.16, abs(light.x));
    float shadowSide = light.x >= 0.0 ? -1.0 : 1.0;
    float sideDistance = fromTip.x * shadowSide;
    float width = 0.0019 * lerp(0.12, 1.0, sideVisibility);
    float2 shape = float2(sideDistance / width, (fromTip.y - 0.0021) / 0.0021);
    // This upward graphic belongs to frontal lighting; lower and rear lights must not retain it.
    float lightValidity = smoothstep(0.0, 0.22, light.z) * smoothstep(-0.65, -0.10, light.y);
    lightValidity *= 1.0 - smoothstep(0.85, 0.98, abs(light.y));
    return step(dot(shape, shape), 1.0) * step(0.0, sideDistance)
         * step(0.0, fromTip.y) * step(0.089, positionHead.z) * sideVisibility * lightValidity;
}

float2 CharacterFaceHairUV(float2 positionHead)
{
    // Matches CharacterFaceHairShadowBuilder: x [-.092,.092], y [-.05,.21] metres.
    return (positionHead - float2(-0.092, -0.05)) / float2(0.184, 0.26);
}

half CharacterFaceHairShadow(float3 positionHead, float3 lightHead,
                             TEXTURE2D_PARAM(hairMap, hairSampler), float spread)
{
    // CharacterHeadFrame updates this head-space height field from the current skinned front hair.
    float3 light = SafeNormalize(lightHead);
    float2 slope = light.xy / max(light.z, 0.2) * spread;
    // A single projection plane avoids discontinuous jumps between overlapping bang layers.
    float2 uv = CharacterFaceHairUV(positionHead.xy + slope * max(0.112 - positionHead.z, 0.0));
    half2 shadowData = SAMPLE_TEXTURE2D_LOD(hairMap, hairSampler, uv, 0).rg;
    float sampledDepth = 0.02 + 0.12 * shadowData.g / max(shadowData.r, 0.001h);
    float inBounds = step(0.0, min(uv.x, uv.y)) * step(max(uv.x, uv.y), 1.0);
    float inFront = step(positionHead.z + 0.001, sampledDepth);
    // Front hair cannot shade a rear-lit face; avoid stretching at grazing light angles.
    float lightValidity = smoothstep(0.08, 0.25, light.z);
    return shadowData.r * inBounds * inFront * lightValidity * smoothstep(0.045, 0.055, positionHead.y);
}

half CharacterFaceInteriorOutlineWeight(float3 bindPosition, float3 viewHead)
{
    // Native lips use both facial UVs and a separate colour-palette island. The bind surface
    // is shared by both, and stays attached when facial bones or blend shapes move.
    float mouth = 1.0 - smoothstep(0.85, 1.1,
        length((bindPosition.xy - float2(0.0, 1.323)) / float2(0.024, 0.012)));
    mouth *= smoothstep(0.022, 0.035, bindPosition.z);
    float2 eyeOffset = float2(abs(bindPosition.x) - 0.034, bindPosition.y - 1.374);
    float eyes = 1.0 - smoothstep(0.85, 1.1, length(eyeOffset / float2(0.027, 0.023)));
    eyes *= smoothstep(0.006, 0.016, bindPosition.z);
    float nose = 1.0 - smoothstep(0.85, 1.1,
        length((bindPosition.xy - float2(0.0, 1.346)) / float2(0.014, 0.018)));
    nose *= smoothstep(0.046, 0.057, bindPosition.z);
    float frontView = 1.0 - smoothstep(0.78, 0.97, abs(viewHead.x));
    return 1.0 - max(mouth, max(eyes, nose) * frontView);
}

half CharacterFaceOutlineWeight(float3 positionHead, float3 normalHead, float3 viewHead)
{
    float sideView = smoothstep(0.35, 0.82, abs(viewHead.x));
    float jawSide = smoothstep(0.012, 0.028, abs(positionHead.x));
    jawSide *= 1.0 - smoothstep(0.000, 0.021, positionHead.y);
    jawSide *= smoothstep(0.028, 0.050, positionHead.z);
    // Fade the returning jaw edge in profile while preserving the chin tip.
    float returningEdge = smoothstep(-0.2, 0.4, normalHead.z);
    float jawWeight = 1.0 - 0.88 * sideView * jawSide * returningEdge;

    // Centre the front-view stroke on the actual chin contour and taper its two sides.
    float jawX = abs(positionHead.x);
    float jawY = -0.0247 + 0.26 * jawX + 8.0 * jawX * jawX;
    float chinBand = 1.0 - smoothstep(0.002, 0.010, abs(positionHead.y - jawY));
    chinBand *= smoothstep(0.043, 0.058, positionHead.z);
    float frontView = 1.0 - smoothstep(0.25, 0.70, abs(viewHead.x));
    float chinTaper = lerp(0.50, 1.05, saturate(1.0 - jawX / 0.028));
    chinTaper += 0.60 * saturate(1.0 - jawX / 0.0025);
    jawWeight *= lerp(1.0, chinTaper, chinBand * frontView);

    // The small nose is concave enough for an inverted hull to draw an interior arc.
    float noseRegion = 1.0 - smoothstep(0.010, 0.017, abs(positionHead.x));
    noseRegion *= smoothstep(0.074, 0.084, positionHead.z);
    noseRegion *= smoothstep(0.008, 0.019, positionHead.y);
    noseRegion *= 1.0 - smoothstep(0.043, 0.064, positionHead.y);
    float profile = smoothstep(0.88, 0.975, abs(viewHead.x));
    // Lip openings must not inherit the thick silhouette hull; keep the authored mouth line.
    float mouthRegion = 1.0 - smoothstep(0.011, 0.017, abs(positionHead.x));
    mouthRegion *= 1.0 - smoothstep(0.0045, 0.009, abs(positionHead.y - 0.0055));
    mouthRegion *= smoothstep(0.067, 0.075, positionHead.z);
    return jawWeight * (1.0 - max(noseRegion, mouthRegion) * (1.0 - profile));
}

#endif
