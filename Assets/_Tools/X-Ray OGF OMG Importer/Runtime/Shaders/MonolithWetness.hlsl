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
float _MonolithWetReflectionIntensity;
static float2 _MonolithPrecipitationSurface;

float2 MonolithCellMoisture(int2 pixel, float3 worldPosition, float3 geometricNormal, uint2 size)
{
    pixel = clamp(pixel, 0, (int2)size - 1);
    MonolithWetCell cell = _MonolithWetnessCells[pixel.y * size.x + pixel.x];
    if (cell.heights.x < cell.heights.y) return 0.0;
    float2 center = _MonolithWetnessMapping.xy + (pixel + 0.5) * _MonolithWetnessCellSize.xy;
    float surfaceHeight = worldPosition.y;
    float planeWeight = smoothstep(0.15, 0.4, abs(geometricNormal.y));
    surfaceHeight -= dot(geometricNormal.xz, center - worldPosition.xz) * planeWeight / (abs(geometricNormal.y) > 0.0001 ? geometricNormal.y : 1.0);
    float exposed = smoothstep(cell.heights.x - _MonolithWetnessSettings.z, cell.heights.x, surfaceHeight);
    return lerp(cell.moisture.xy, cell.moisture.zw, exposed);
}

float2 MonolithSurfaceMoisture(float3 worldPosition, float3 geometricNormal)
{
    if (_MonolithWetnessActive < 0.5) return 0.0;
    float2 projected = worldPosition.xz + geometricNormal.xz * max(_MonolithWetnessCellSize.x, _MonolithWetnessCellSize.y) * (0.75 * saturate(1.0 - abs(geometricNormal.y) * 4.0));
    float2 uv = (projected - _MonolithWetnessMapping.xy) * _MonolithWetnessMapping.zw;
    if (any(uv < 0.0) || any(uv >= 1.0)) return 0.0;
    uint2 size = (uint2)_MonolithWetnessCellSize.zw;
    float2 coordinate = uv * size - 0.5;
    int2 pixel = (int2)floor(coordinate);
    float2 blend = frac(coordinate);
    float2 lower = lerp(MonolithCellMoisture(pixel, worldPosition, geometricNormal, size), MonolithCellMoisture(pixel + int2(1, 0), worldPosition, geometricNormal, size), blend.x);
    float2 upper = lerp(MonolithCellMoisture(pixel + int2(0, 1), worldPosition, geometricNormal, size), MonolithCellMoisture(pixel + int2(1, 1), worldPosition, geometricNormal, size), blend.x);
    return lerp(lower, upper, blend.y);
}

float MonolithWetNoise(float2 worldPosition)
{
    float2 uv = worldPosition * _MonolithPuddleSettings.y;
    float broad = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, uv * 0.09, 0.0, 0.0).r;
    float medium = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, float2(uv.x + uv.y, uv.y - uv.x) * 0.21 + 0.37, 0.0, 0.0).r;
    float fine = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, uv * 0.83 - 0.19, 0.0, 0.0).r;
    return broad * 0.55 + medium * 0.3 + fine * 0.15;
}

float2 MonolithWetSurface(float3 worldPosition, float3 geometricNormal, float2 uv, float microHeight, float2 moisture, bool flora, inout float3 encodedAlbedo, inout float gloss)
{
    if (_MonolithWetnessActive < 0.5 || _MonolithWetResponse.w < 0.5 || all(moisture <= 0.0)) return 0.0;
    float noise = MonolithWetNoise(worldPosition.xz);
    float wetness = saturate(moisture.x * _MonolithWetnessSettings.x * lerp(0.8, 1.2, noise));
    float puddles = 0.0;
    if (!flora && moisture.y > 0.0 && _MonolithWetnessSettings.y > 0.0)
    {
        float reservoir = noise;
        if (_MonolithPuddleMaskSlice >= 2.0 && _MonolithPuddleMaskSlice < _MonolithPuddleSettings.w)
        {
            float2 sourceUv = float2(uv.x, 1.0 - uv.y);
            sourceUv.y = _MonolithPuddleMaskFlipV > 0.5 ? 1.0 - sourceUv.y : sourceUv.y;
            float sourceHeight = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, sourceUv, _MonolithPuddleMaskSlice, 0.0).r;
            reservoir = lerp(noise, sourceHeight, 0.55);
        }
        reservoir = reservoir * 0.65 + saturate(microHeight) * 0.35;
        float slope = smoothstep(0.94, 0.995, geometricNormal.y);
        float waterLevel = saturate(moisture.y * _MonolithWetResponse.z * _MonolithWetnessSettings.y) * 0.6 - 0.1;
        puddles = smoothstep(reservoir - 0.035, reservoir + 0.035, waterLevel) * slope;
    }
    if (wetness <= 0.0 && puddles <= 0.0) return 0.0;
    encodedAlbedo *= lerp(1.0, _MonolithWetResponse.x, wetness);
    if (!flora)
    {
        float wetGloss = (0.15 + saturate(_MonolithWetnessSettings.w) * 0.05) * saturate(wetness * 2.0);
        gloss = saturate(gloss + wetGloss * saturate(geometricNormal.y * 1.5) * _MonolithWetResponse.y);
    }
    encodedAlbedo *= lerp(1.0, float3(0.92, 0.95, 0.97), puddles * saturate(moisture.y));
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
    float2 wave = (first.xy + second.xy - 1.0) * 0.5;
    float2 coordinate = worldPosition.xz * 1.8;
    float2 cell = floor(coordinate);
    float2 ripple = 0.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 neighbor = cell + float2(x, y);
            float phase = MonolithRippleHash(neighbor);
            float2 center = neighbor + float2(phase, MonolithRippleHash(neighbor.yx + 29.0)) * 0.6 + 0.2;
            float2 delta = coordinate - center;
            float radius = length(delta);
            float age = frac(_MonolithPuddleSettings.z * 0.85 + phase);
            float ring = sin((radius - age * 0.8) * 40.0) * saturate(1.0 - abs(radius - age * 0.8) * 12.0) * Sq(1.0 - age) * smoothstep(0.0, 0.12, age) * saturate(1.0 - radius);
            ripple += delta / max(radius, 0.001) * ring;
        }
    }
    float2 perturbation = (wave * (0.12 + rain * 0.35) + ripple * rain) * _MonolithPuddleSettings.x;
    float3 waterNormal = normalize(float3(perturbation.x, 1.0, perturbation.y));
    return normalize(lerp(film, waterNormal, saturate(wetness.y * wetness.y * 1.8)));
}

#endif
