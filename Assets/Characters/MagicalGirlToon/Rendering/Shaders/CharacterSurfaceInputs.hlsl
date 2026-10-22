#ifndef ZZZ_CHARACTER_SURFACE_INPUTS_INCLUDED
#define ZZZ_CHARACTER_SURFACE_INPUTS_INCLUDED

// Source M.R selects parameter slots, not fixed skin/hair material classes.
half4 CharacterSourceSlotParameters(half materialID, half4 slot1, half4 slot2, half4 slot3, half4 slot4, half4 slot5)
{
    if (materialID < 0.2h) return slot5;
    if (materialID < 0.4h) return slot4;
    if (materialID < 0.6h) return slot3;
    if (materialID < 0.8h) return slot2;
    return slot1;
}

// ZZZ packed N stores tangent XY in RG; B is diffuse bias, not normal Z.
half3 CharacterPackedNormalWS(float3 positionWS, float2 uv, half3 normalWS, half2 packedRG, half strength)
{
    if (strength <= 0.0h) return normalWS;
    float3 dx = ddx(positionWS), dy = ddy(positionWS);
    float2 ux = ddx(uv), uy = ddy(uv);
    float determinant = ux.x * uy.y - ux.y * uy.x;
    float3 tangent = (dx * uy.y - dy * ux.y) * sign(determinant);
    tangent -= normalWS * dot(tangent, normalWS);
    if (abs(determinant) < 1e-12 || dot(tangent, tangent) < 1e-16) return normalWS;
    tangent = normalize(tangent);
    float3 originalBitangent = (dy * ux.x - dx * uy.x) * sign(determinant);
    float3 bitangent = cross(normalWS, tangent);
    bitangent *= dot(bitangent, originalBitangent) < 0.0 ? -1.0 : 1.0;
    half2 xy = (packedRG * 2.0h - 1.0h) * strength;
    half3 normalTS = normalize(half3(xy, sqrt(saturate(1.0h - dot(xy, xy)))));
    return normalize(tangent * normalTS.x + bitangent * normalTS.y + normalWS * normalTS.z);
}

// A local, wavelength-dependent wrap approximation. No screen-space diffusion
// or measured thickness is available; cast/environment shadows still gate it.
half3 CharacterSkinScatter(half ndotl, half radius)
{
    half3 spread = max(radius, 0.001h) * half3(1.0h, 0.45h, 0.20h);
    half3 wrapped = saturate((ndotl + spread) / (1.0h + spread));
    return max(wrapped - saturate(ndotl), 0.0h);
}

#endif
