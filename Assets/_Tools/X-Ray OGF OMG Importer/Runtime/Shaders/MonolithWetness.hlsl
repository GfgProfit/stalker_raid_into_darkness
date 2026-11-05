#ifndef MONOLITH_WETNESS_INCLUDED
#define MONOLITH_WETNESS_INCLUDED

struct MonolithWetCell
{
    float4 moisture;
    float2 heights;
};

StructuredBuffer<MonolithWetCell> _MonolithWetnessCells;
TEXTURE2D_ARRAY(_MonolithWetSurfaceTextures);
float _MonolithWetnessActive;
float4 _MonolithWetnessMapping;
float4 _MonolithWetnessCellSize;
float4 _MonolithWetnessSettings;
float4 _MonolithPuddleSettings;

float2 MonolithSurfaceMoisture(float3 worldPosition, float3 geometricNormal)
{
    if (_MonolithWetnessActive < 0.5) return 0.0;
    float2 projected = worldPosition.xz + geometricNormal.xz * max(_MonolithWetnessCellSize.x, _MonolithWetnessCellSize.y) * 0.75;
    float2 uv = (projected - _MonolithWetnessMapping.xy) * _MonolithWetnessMapping.zw;
    if (any(uv < 0.0) || any(uv >= 1.0)) return 0.0;
    uint2 size = (uint2)_MonolithWetnessCellSize.zw;
    uint2 pixel = min((uint2)(uv * size), size - 1);
    MonolithWetCell cell = _MonolithWetnessCells[pixel.y * size.x + pixel.x];
    if (cell.heights.x < cell.heights.y) return 0.0;
    return worldPosition.y >= cell.heights.x - _MonolithWetnessSettings.z ? cell.moisture.zw : cell.moisture.xy;
}

float MonolithWetNoise(float2 worldPosition)
{
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, worldPosition * (0.05 * _MonolithPuddleSettings.y), 0.0, 0.0).r;
}

float2 MonolithWetSurface(float3 worldPosition, float3 geometricNormal, float2 uv, float microHeight, float2 moisture, bool flora, inout float3 encodedAlbedo, inout float gloss)
{
    if (_MonolithWetnessActive < 0.5 || _MonolithWetResponse.w < 0.5) return 0.0;
    float wetness = saturate(moisture.x * _MonolithWetnessSettings.x);
    float puddles = 0.0;
    if (!flora && moisture.y > 0.0 && _MonolithWetnessSettings.y > 0.0)
    {
        float mask = 1.0 - MonolithWetNoise(worldPosition.xz);
        if (_MonolithPuddleMaskSlice >= 2.0 && _MonolithPuddleMaskSlice < _MonolithPuddleSettings.w)
        {
            float2 sourceUv = float2(uv.x, 1.0 - uv.y);
            sourceUv.y = _MonolithPuddleMaskFlipV > 0.5 ? 1.0 - sourceUv.y : sourceUv.y;
            mask = 1.0 - SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, sourceUv, _MonolithPuddleMaskSlice, 0.0).r;
        }
        float slope = saturate(1.0 - max(abs(geometricNormal.x), abs(geometricNormal.z)) - 0.9) * 13.0;
        float heightFill = saturate(saturate(moisture.y) * 0.5 - microHeight) * 4.0;
        puddles = saturate(heightFill * mask * slope * step(0.9, geometricNormal.y) * _MonolithWetResponse.z * _MonolithWetnessSettings.y);
    }
    if (wetness <= 0.0 && puddles <= 0.0) return 0.0;
    encodedAlbedo *= lerp(1.0, _MonolithWetResponse.x, wetness);
    if (!flora)
    {
        float wetGloss = (0.15 + saturate(_MonolithWetnessSettings.w) * 0.05) * saturate(wetness * 2.0);
        gloss = saturate(gloss + wetGloss * saturate(geometricNormal.y * 1.5) * _MonolithWetResponse.y);
    }
    encodedAlbedo = lerp(encodedAlbedo, encodedAlbedo * float3(0.66, 0.63, 0.60 * 0.955), puddles);
    gloss = lerp(gloss, 0.94, puddles);
    return float2(wetness, puddles);
}

float MonolithRippleHash(float2 cell)
{
    return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
}

float3 MonolithWetNormal(float3 worldPosition, float3 geometricNormal, float3 normal, float2 wetness)
{
    if (wetness.x <= 0.0 && wetness.y <= 0.0) return normal;
    float3 film = normalize(lerp(normal, geometricNormal, wetness.x * 0.2));
    if (wetness.y <= 0.0) return film;
    float rain = saturate(_MonolithWetnessSettings.w * 1.5);
    float animation = _MonolithPuddleSettings.z * (0.01 + rain * 0.008);
    float2 waveUv = worldPosition.xz * (0.25 * _MonolithPuddleSettings.y);
    float3 first = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, waveUv + float2(0.0, animation), 1.0, 0.0).xyz;
    float3 second = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, waveUv - float2(0.33, animation), 1.0, 0.0).xyz;
    float3 wave = normalize(float3(first.xy + second.xy, first.z * second.z)) * 2.0 - 1.0;
    float2 cell = floor(worldPosition.xz * 1.8);
    float phase = MonolithRippleHash(cell);
    float2 center = float2(phase, MonolithRippleHash(cell.yx + 29.0)) * 0.6 + 0.2;
    float2 delta = frac(worldPosition.xz * 1.8) - center;
    float radius = length(delta);
    float age = frac(_MonolithPuddleSettings.z * 0.85 + phase);
    float ring = sin((radius - age * 0.8) * 40.0) * saturate(1.0 - abs(radius - age * 0.8) * 12.0) * (1.0 - age);
    float2 ripple = delta / max(radius, 0.001) * ring * rain;
    float2 perturbation = (wave.xy * (0.05 + rain) + ripple) * _MonolithPuddleSettings.x;
    float3 waterNormal = normalize(float3(perturbation.x, 1.0, perturbation.y));
    return normalize(lerp(film, waterNormal, saturate(wetness.y * wetness.y * 1.8)));
}

#endif
