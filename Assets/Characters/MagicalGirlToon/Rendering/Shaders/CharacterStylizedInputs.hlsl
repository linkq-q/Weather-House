#ifndef ZZZ_CHARACTER_STYLIZED_INPUTS_INCLUDED
#define ZZZ_CHARACTER_STYLIZED_INPUTS_INCLUDED

// Project-authored masks only: R = ambient visibility, G = specular weight,
// B = diffuse-ramp weight, A = reserved. White is neutral. Source *_M maps
// have NOT been identified as this layout; inspect their channels first.
half4 CharacterStylizedMask(float2 uv, TEXTURE2D_PARAM(maskMap, maskSampler), half strength)
{
    return lerp(half4(1, 1, 1, 1), SAMPLE_TEXTURE2D(maskMap, maskSampler, uv), saturate(strength));
}

// RGB stores colour multipliers, left = unlit, right = lit. This colours the
// existing diffuse result, retaining face, hair, skin and garment shaping.
half3 CharacterDiffuseRamp(half lightLevel, TEXTURE2D_PARAM(rampMap, rampSampler), half strength)
{
    half3 tint = SAMPLE_TEXTURE2D(rampMap, rampSampler, float2(saturate(lightLevel), 0.5)).rgb;
    return lerp(half3(1, 1, 1), tint, saturate(strength));
}

#endif
