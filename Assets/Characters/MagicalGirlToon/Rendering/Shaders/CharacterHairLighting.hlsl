#ifndef CHARACTER_HAIR_LIGHTING_INCLUDED
#define CHARACTER_HAIR_LIGHTING_INCLUDED

struct CharacterHairSettings
{
    half shadeThreshold;
    half shadeSoftness;
    half formStrength;
    half creaseStrength;
    half specWidth;
    half specShift;
    half specStrength;
    half diffuseBias;
};

struct CharacterHairLighting
{
    half form;
    half contact;
    half spec;
};

// Only use where control.b verifies V follows a strand. Derivatives follow the skinned surface.
float3 CharacterHairDirectionV(float3 positionWS, float2 uv, half3 normalWS)
{
    float3 dpdx = ddx(positionWS);
    float3 dpdy = ddy(positionWS);
    float2 duvdx = ddx(uv);
    float2 duvdy = ddy(uv);
    float determinant = duvdx.x * duvdy.y - duvdx.y * duvdy.x;
    float3 direction = dpdy * duvdx.x - dpdx * duvdy.x;
    direction *= determinant < 0.0 ? -1.0 : 1.0;
    direction -= normalWS * dot(direction, normalWS);
    float squaredLength = dot(direction, direction);
    if (abs(determinant) < 1e-12 || squaredLength < 1e-20) return float3(0, 0, 0);
    return direction * rsqrt(squaredLength);
}

// control is sampled linear texture: R=local fold, G=authored highlight, B=verified longitudinal V region.
CharacterHairLighting EvaluateCharacterHair(half3 normalWS, half3 strandWS,
    half3 viewWS, half3 lightWS, float4 control, CharacterHairSettings settings)
{
    CharacterHairLighting result;
    half ndotl = dot(normalWS, lightWS);
    half wrappedLight = saturate(ndotl * 0.5h + 0.5h + settings.diffuseBias);
    half band = smoothstep(settings.shadeThreshold - settings.shadeSoftness,
                          settings.shadeThreshold + settings.shadeSoftness, wrappedLight);
    // Preserve the base light/dark division; only the two painted fold regions add contact shading.
    result.form = lerp(band, wrappedLight, settings.formStrength);
    result.contact = saturate(control.r) * saturate(settings.creaseStrength);

    float3 strand = strandWS - normalWS * dot(strandWS, normalWS);
    float strandLength = dot(strand, strand);
    strand *= rsqrt(max(strandLength, 1e-8));
    float3 shiftedStrand = SafeNormalize(strand + normalWS * settings.specShift);
    half3 halfVector = SafeNormalize(lightWS + viewWS);
    half tangentDotHalf = dot(shiftedStrand, halfVector);
    half sinSquared = saturate(1.0h - tangentDotHalf * tangentDotHalf);
    half power = rcp(max(settings.specWidth * settings.specWidth, 0.0025h));
    half directionalResponse = pow(sinSquared, power);
    // Authored bands determine placement. Anisotropy gently varies intensity, never adds broad glints.
    half flowWeight = saturate(control.b) * step(1e-6, strandLength);
    half modulation = lerp(1.0h, 0.75h + 0.25h * directionalResponse, flowWeight);
    half lightVisibility = smoothstep(-0.35h, 0.25h, ndotl);
    result.spec = saturate(control.g) * settings.specStrength * modulation * (1.0h - result.contact) * lightVisibility;
    return result;
}

#endif
