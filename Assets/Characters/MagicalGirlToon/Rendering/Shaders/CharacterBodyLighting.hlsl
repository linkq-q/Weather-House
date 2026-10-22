#ifndef CHARACTER_BODY_LIGHTING_INCLUDED
#define CHARACTER_BODY_LIGHTING_INCLUDED

// bodyControl: skin hint, cloth contact, garment region (1 veil / 2 sash), cast mask.
// bindData is a per-vertex rest position in metres; its w stores regional softness.
half3 CharacterBodyDiffuse(half3 defaultDiffuse, half3 albedo, half3 normalWS, half3 skinNormalWS,
                          Light key, half ao, half skinMask, half4 bodyControl,
                          float4 bindData, float2 uv, half3 viewWS, inout half opacity, half environmentShadow, half sourceBias, half3 sourceTint)
{
    if (_BodyMode < 0.5h)
        return defaultDiffuse;

    // Self-projected shadow fragments break the breast arc; retain its normal-driven form and AO.
    if (_UseSkinOverlay > 0.5h)
    {
        half chestCloth = (1.0h - saturate(skinMask)) * (1.0h - saturate(bodyControl.z))
                        * smoothstep(0.040, 0.065, bindData.z)
                        * smoothstep(1.135, 1.155, bindData.y) * (1.0 - smoothstep(1.275, 1.300, bindData.y))
                        * (1.0 - smoothstep(0.100, 0.140, abs(bindData.x)));
        half clothLight = saturate(dot(normalWS, key.direction) * 0.5h + 0.5h + sourceBias);
        half clothBand = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, clothLight);
        half clothForm = lerp(clothBand, clothLight, _FormStrength);
        defaultDiffuse += albedo * key.color * key.distanceAttenuation * _DirectStrength * clothForm
                        * (_ShadowStrength * max(environmentShadow - key.shadowAttenuation, 0.0h) * chestCloth);
    }

    half3 skinNormal = normalize(lerp(normalWS, skinNormalWS, _SkinNormalStrength));
    half wrappedLight = saturate(dot(skinNormal, key.direction) * 0.5h + 0.5h + sourceBias);
    half softness = saturate(bindData.w);
    // Feather the form at the upper-arm ring without restoring a dark clothing-contact band.
    float2 armOffset = float2(abs(bindData.x) - 0.0973, bindData.y - 1.2773);
    float armAlong = dot(armOffset, float2(0.775, -0.632));
    float armAcross = abs(dot(armOffset, float2(0.632, 0.775)));
    half armBand = smoothstep(0.060, 0.090, armAlong) * (1.0 - smoothstep(0.150, 0.190, armAlong))
                 * (1.0 - smoothstep(0.035, 0.055, armAcross));
    half softWidth = lerp(0.065h, 0.095h, softness) + armBand * 0.024h;
    half softShade = 1.0h - smoothstep(_SkinShadeThreshold - softWidth,
        _SkinShadeThreshold + softWidth, wrappedLight);
    half deepWidth = lerp(0.025h, 0.045h, softness) + armBand * 0.012h;
    half deepShade = 1.0h - smoothstep(_SkinDeepThreshold - deepWidth,
        _SkinDeepThreshold + deepWidth, wrappedLight);
    half3 skinRamp = lerp(half3(1, 1, 1), _SkinShadeColor.rgb, softShade);
    skinRamp = lerp(skinRamp, _SkinDeepShadeColor.rgb, deepShade);

    half warmDistance = (wrappedLight - _SkinShadeThreshold) / softWidth;
    half warmBoundary = exp2(-5.0h * warmDistance * warmDistance);
    half contact = saturate(bodyControl.y);
    skinRamp *= 1.0h - contact * _SkinContactStrength * 0.10h;
    half contactCore = smoothstep(0.45h, 0.92h, contact) * _SkinContactStrength;
    skinRamp = lerp(skinRamp, _SkinContactColor.rgb, contactCore);
    skinRamp += half3(0.014h, 0.003h, -0.003h) * warmBoundary;
    half contactBoundary = smoothstep(0.05h, 0.35h, contact) * (1.0h - smoothstep(0.65h, 0.95h, contact));
    skinRamp += half3(0.012h, 0.002h, 0.0h) * contactBoundary;

    half shadow = lerp(1.0h, smoothstep(0.08h, 0.92h, key.shadowAttenuation), _SkinShadowStrength);
    shadow = min(shadow, lerp(1.0h, environmentShadow, _EnvironmentShadowStrength));
    half3 skinIrradiance = key.color * (key.distanceAttenuation * shadow * 0.70h)
                        + SampleSH(skinNormal) * lerp(1.0h, ao, 0.35h) * 0.70h;
    half3 color = lerp(defaultDiffuse, albedo * skinRamp * skinIrradiance, saturate(skinMask));

    // Keep the under-bust volume on the skin beneath the transparent cloth.
    float side = abs(bindData.x);
    float underBustY = bindData.y + 3.5 * bindData.x * bindData.x;
    half center = 1.0h - smoothstep(0.012, 0.033, side);
    float lowerFeather = lerp(0.008, 0.003, center);
    float upperFeather = lerp(0.015, 0.004, center);
    half underBust = smoothstep(1.151 - lowerFeather, 1.151 + lowerFeather, underBustY)
                   * (1.0 - smoothstep(1.2155 - upperFeather, 1.2155 + upperFeather, underBustY));
    half underBustSkin = saturate(skinMask) * smoothstep(0.065, 0.090, bindData.z)
                      * (1.0 - smoothstep(0.024, 0.044, side));
    // Keep a contact base, but let the volume follow the current skinned normal and light.
    half volumeResponse = lerp(0.40h, 1.0h, 1.0h - smoothstep(0.65h, 0.95h, wrappedLight));
    color *= 1.0h - underBust * underBustSkin * _BodyDetailStrength * 0.15h * volumeResponse;

    // Two low-contrast curved folds follow the actual waist sash, not the light direction.
    half sash = saturate(bodyControl.z - 1.0h);
    float curveA = 1.139 - 0.42 * side - 1.0 * side * side;
    float curveB = 1.112 - 0.28 * side - 0.8 * side * side;
    float foldDistanceA = (bindData.y - curveA) / 0.0045;
    float foldDistanceB = (bindData.y - curveB) / 0.0035;
    float foldA = exp2(-foldDistanceA * foldDistanceA * 2.0);
    float foldB = exp2(-foldDistanceB * foldDistanceB * 2.0);
    half fold = (half)(foldA * 0.11 + foldB * 0.075) * sash * _BodyDetailStrength;
    color *= 1.0h - fold;

    // The veil uses its original continuous alpha. This only adds restrained contact/form.
    half veil = saturate(1.0h - abs(bodyControl.z - 1.0h));
    float waistDistance = (bindData.y - 1.035) / 0.075;
    float waist = exp2(-waistDistance * waistDistance * 2.0);
    float edge = smoothstep(0.012, 0.033, side);
    float rippleDistance = (bindData.y - (1.070 - 1.8 * bindData.x * bindData.x)) / 0.0045;
    float ripple = exp2(-rippleDistance * rippleDistance * 2.0);
    color *= 1.0h - veil * _BodyDetailStrength * (half)(waist * edge * 0.09 + ripple * 0.035);

    if (_BodyMode > 1.5h)
    {
        // The source RGB/edge alpha paints an opaque triangle. Its centre alpha retains the horizontal folds.
        // Stretch only the fold profile vertically; preserve horizontal direction and transparent gaps.
        float2 foldUV = float2(0.05495, 0.515 + (uv.y - 0.515) / 3.0);
        half alphaCenter = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, foldUV).a;
        half alphaNear = (SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, foldUV + float2(0, 0.003)).a
                        + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, foldUV - float2(0, 0.003)).a) * 0.5h;
        half alphaFar = (SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, foldUV + float2(0, 0.014)).a
                       + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, foldUV - float2(0, 0.014)).a) * 0.5h;
        half crease = saturate(((alphaCenter + alphaNear) * 0.5h - (alphaCenter + alphaFar * 2.0h) / 3.0h) * 14.0h);
        half chestVeil = smoothstep(1.148, 1.164, bindData.y);
        half3 halfDirection = SafeNormalize(key.direction + viewWS);
        half sheen = 0.82h + 0.18h * pow(saturate(dot(normalWS, halfDirection)), 8.0h);
        half veilLight = lerp(0.35h, 1.0h, saturate(dot(normalWS, key.direction) * 0.5h + 0.5h));
        half veilShadow = lerp(1.0h, environmentShadow, _EnvironmentShadowStrength);
        half3 whiteCloth = half3(0.94h, 0.97h, 1.0h)
                         * (key.color * key.distanceAttenuation * veilLight * veilShadow * 0.72h + SampleSH(normalWS) * 0.65h);
        color = lerp(color, whiteCloth * sourceTint, chestVeil);
        opacity = lerp(opacity, (0.025h + crease * 0.30h) * sheen, chestVeil);
    }
    return color;
}

#endif
