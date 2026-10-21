#ifndef ALBUM_MAGIC_FIELD_INCLUDED
#define ALBUM_MAGIC_FIELD_INCLUDED

sampler2D _NoiseTex;
float _Reveal, _NoiseSize, _EdgeWidth, _Softness, _GlowIntensity, _GlowSpread;
float4 _NoiseOrigin, _NoiseOffset, _AmberColor, _GoldColor, _IvoryColor;

// Every source graphic, aura and particle samples this same field in book-local space.
// Bounds affect aura clipping only; they never restart or redirect the noise.
float AlbumFrontDistance(float2 position)
{
    float noise = tex2D(_NoiseTex, position / max(_NoiseSize,1) + _NoiseOffset.xy).r;
    return lerp(-_Softness, 1+_Softness, _Reveal) - noise;
}

float AlbumLightEnvelope()
{
    return smoothstep(.015,.13,_Reveal) * (1-smoothstep(.86,.995,_Reveal));
}

half3 AlbumGoldRamp(float heat)
{
    return heat < .65 ? lerp(_AmberColor.rgb,_GoldColor.rgb,saturate(heat/.65))
        : lerp(_GoldColor.rgb,_IvoryColor.rgb,saturate((heat-.65)/.35));
}
#endif
