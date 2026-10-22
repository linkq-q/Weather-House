#ifndef ZZZ_CHARACTER_OUTLINE_INCLUDED
#define ZZZ_CHARACTER_OUTLINE_INCLUDED

float3 CharacterOutlineNormalWS(float3 normalOS, float4 tangentOS, float4 outlineControl)
{
    float3 normal = normalize(TransformObjectToWorldNormal(normalOS));
    if (outlineControl.w < 0.5) return normal;
    // Unity skins normal/tangent attributes; rebuild the baked direction in that frame.
    float3 tangent = TransformObjectToWorldDir(tangentOS.xyz);
    tangent = SafeNormalize(tangent - normal * dot(tangent, normal));
    float3 bitangent = cross(normal, tangent) * tangentOS.w * GetOddNegativeScale();
    return SafeNormalize(tangent * outlineControl.x + bitangent * outlineControl.y + normal * outlineControl.z);
}

float4 CharacterOutlinePositionCS(float4 positionCS, float3 positionWS, float3 normalWS,
                                 float width, float fullWidthDistance, float weight, out half coverage)
{
    float viewDepth = abs(TransformWorldToView(positionWS).z);
    float pixelsPerMetre = 0.5 * abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y;
    pixelsPerMetre /= unity_OrthoParams.w > 0.5 ? 1.0 : max(viewDepth, 0.01);
    // Calibrate the distance control at the lookdev's 1080p / 39 degree reference camera.
    float referenceScale = 1080.0 / (2.0 * tan(radians(39.0) * 0.5) * fullWidthDistance);
    float basePixels = width * smoothstep(0.0, 1.0, pixelsPerMetre / referenceScale);
    float targetPixels = basePixels * weight;
    // Subpixel lines fade by coverage instead of leaving an opaque one-pixel floor.
    float pixels = basePixels > 0.0 ? max(0.8, basePixels) * weight : 0.0;
    coverage = saturate(targetPixels / max(pixels, 0.8));
    // Extrude one coherent shell, including its depth; normalizing XY alone
    // moves nearly face-on thin surfaces sideways by a full line width.
    float worldWidth = pixels / max(pixelsPerMetre, 0.0001);
    positionCS = TransformWorldToHClip(positionWS + normalWS * worldWidth);
    return positionCS;
}

half CharacterOutlineOpacity(float3 normalWS, float3 viewWS, half innerOpacity)
{
    // The outside silhouette stays solid; only the returning inner surface fades.
    float facing = abs(dot(normalize(normalWS), normalize(viewWS)));
    float filterWidth = max(fwidth(facing), 0.005);
    return lerp(1.0, innerOpacity, smoothstep(0.03 + filterWidth, 0.30 + filterWidth, facing));
}

#endif
