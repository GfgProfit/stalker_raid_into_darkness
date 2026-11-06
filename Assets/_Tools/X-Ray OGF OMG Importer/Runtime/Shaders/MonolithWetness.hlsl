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
float4 _MonolithPuddleShape;
float4 _MonolithPuddleOptics;
float4 _MonolithPuddleRipple;
float _MonolithPuddleRainTime;
float _MonolithWetReflectionIntensity;
static float2 _MonolithPrecipitationSurface;
static float4 _MonolithPrecipitationResponse;
static float _MonolithPrecipitationDepth;

float4 MonolithResolvedWetResponse()
{
    return _MonolithPrecipitationResponse.w > 0.0 ? _MonolithPrecipitationResponse : _MonolithWetResponse;
}

float MonolithWetBsdfWeight(float2 surface)
{
    float film = surface.x * saturate(MonolithResolvedWetResponse().y);
    return max(surface.y, smoothstep(0.0, 0.35, film));
}

float3 MonolithWetSpecular(float3 drySpecular, float2 surface)
{
    return lerp(lerp(drySpecular, 0.04, MonolithWetBsdfWeight(surface)), 0.0204, surface.y);
}

float4 MonolithResolvedPuddleShape()
{
    return _MonolithPuddleShape.x > 0.0 ? _MonolithPuddleShape : float4(0.055, 0.004, 2.0, 0.085);
}

float4 MonolithResolvedPuddleOptics()
{
    return _MonolithPuddleOptics.z > 0.0 ? _MonolithPuddleOptics : float4(5.0, 0.32, 0.12, 1.0);
}

float MonolithWetTextureLod(float2 uv)
{
    #if defined(SHADER_STAGE_FRAGMENT)
    uint width;
    uint height;
    uint slices;
    _MonolithWetSurfaceTextures.GetDimensions(width, height, slices);
    float2 footprint = max(abs(ddx(uv)), abs(ddy(uv))) * float2(width, height);
    return max(0.0, log2(max(1.0, max(footprint.x, footprint.y))));
    #else
    return 0.0;
    #endif
}

float MonolithWetGroundHeight(int2 pixel, float referenceHeight, uint2 size)
{
    pixel = clamp(pixel, 0, (int2)size - 1);
    float2 heights = _MonolithWetnessCells[pixel.y * size.x + pixel.x].heights;
    if (heights.x < heights.y) return referenceHeight;
    float height = abs(heights.x - referenceHeight) < abs(heights.y - referenceHeight) ? heights.x : heights.y;
    float continuity = 1.0 - smoothstep(0.25, 0.5, abs(height - referenceHeight));
    return lerp(referenceHeight, height, continuity);
}

float MonolithWetGroundPlane(float3 worldPosition)
{
    if (_MonolithPuddleShape.x <= 0.0 || any(_MonolithWetnessCellSize.xy <= 0.0)) return worldPosition.y;
    uint2 size = (uint2)_MonolithWetnessCellSize.zw;
    if (any(size < 1)) return worldPosition.y;
    int radius = max(1, (int)round(_MonolithPuddleShape.z));
    float2 coordinate = ((worldPosition.xz - _MonolithWetnessMapping.xy) / _MonolithWetnessCellSize.xy - 0.5) / radius;
    int2 pixel = (int2)floor(coordinate) * radius;
    float2 blend = frac(coordinate);
    float lower = lerp(MonolithWetGroundHeight(pixel, worldPosition.y, size), MonolithWetGroundHeight(pixel + int2(radius, 0), worldPosition.y, size), blend.x);
    float upper = lerp(MonolithWetGroundHeight(pixel + int2(0, radius), worldPosition.y, size), MonolithWetGroundHeight(pixel + int2(radius, radius), worldPosition.y, size), blend.x);
    return lerp(lower, upper, blend.y);
}

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
    float broad = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, uv * 0.09, 0.0, MonolithWetTextureLod(uv * 0.09)).r;
    float medium = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, float2(uv.x + uv.y, uv.y - uv.x) * 0.21 + 0.37, 0.0, MonolithWetTextureLod(uv * 0.21)).r;
    float fine = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, uv * 0.83 - 0.19, 0.0, MonolithWetTextureLod(uv * 0.83)).r;
    return broad * 0.55 + medium * 0.3 + fine * 0.15;
}

float2 MonolithWetSurface(float3 worldPosition, float3 geometricNormal, float2 uv, float microHeight, float2 moisture, bool flora, inout float3 encodedAlbedo, inout float gloss)
{
    float4 response = MonolithResolvedWetResponse();
    _MonolithPrecipitationDepth = 0.0;
    if (_MonolithWetnessActive < 0.5 || response.w < 0.5 || all(moisture <= 0.0)) return 0.0;
    float4 shape = MonolithResolvedPuddleShape();
    float4 optics = MonolithResolvedPuddleOptics();
    float noise = MonolithWetNoise(worldPosition.xz);
    float wetness = saturate(moisture.x * _MonolithWetnessSettings.x * lerp(0.8, 1.2, noise));
    float puddles = 0.0;
    if (!flora && moisture.y > 0.0 && _MonolithWetnessSettings.y > 0.0)
    {
        float reservoir = saturate(microHeight);
        float sourceSupport = 1.0;
        if (_MonolithPuddleMaskSlice >= 2.0 && _MonolithPuddleMaskSlice < _MonolithPuddleSettings.w)
        {
            float2 sourceUv = float2(uv.x, 1.0 - uv.y);
            sourceUv.y = _MonolithPuddleMaskFlipV > 0.5 ? 1.0 - sourceUv.y : sourceUv.y;
            float sourceHeight = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, sourceUv, _MonolithPuddleMaskSlice, MonolithWetTextureLod(sourceUv)).r;
            sourceSupport = 1.0 - saturate(sourceHeight);
        }
        float slope = smoothstep(0.97, 0.998, geometricNormal.y);
        float coverageScale = _MonolithPuddleRipple.w > 0.0 ? _MonolithPuddleRipple.w : 1.0;
        float fill = saturate(moisture.y * response.z * _MonolithWetnessSettings.y * coverageScale);
        float macroDepth = clamp(MonolithWetGroundPlane(worldPosition) - worldPosition.y, -shape.x, shape.x);
        float waterLevel = fill * shape.x;
        float depth = min(shape.x, waterLevel + macroDepth * fill - reservoir * shape.w - (noise - 0.5) * shape.y);
        puddles = smoothstep(0.0, shape.y, depth) * slope * saturate(fill * 16.0) * sourceSupport;
        _MonolithPrecipitationDepth = max(0.0, depth) * slope * sourceSupport;
        float dampRim = smoothstep(-shape.y * 4.0, shape.y, depth) * fill * slope * sourceSupport;
        wetness = max(wetness, dampRim);
    }
    if (wetness <= 0.0 && puddles <= 0.0) return 0.0;
    encodedAlbedo *= lerp(1.0, response.x, wetness);
    float3 absorption = exp(-_MonolithPrecipitationDepth * optics.x * float3(0.8, 1.1, 1.6));
    encodedAlbedo *= lerp(1.0, pow(absorption, 1.0 / 2.2), puddles);
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
    float4 optics = MonolithResolvedPuddleOptics();
    float rain = _MonolithPuddleShape.x > 0.0 ? saturate(_MonolithPuddleRipple.x) : saturate(_MonolithWetnessSettings.w);
    float animation = _MonolithPuddleSettings.z * (0.01 + rain * 0.008);
    float2 waveUv = worldPosition.xz * (0.25 * _MonolithPuddleSettings.y);
    float waveLod = MonolithWetTextureLod(waveUv);
    float3 first = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, waveUv + float2(0.0, animation), 1.0, waveLod).xyz;
    float3 second = SAMPLE_TEXTURE2D_ARRAY_LOD(_MonolithWetSurfaceTextures, s_linear_repeat_sampler, waveUv - float2(0.33, animation), 1.0, waveLod).xyz;
    float2 wave = (first.xy + second.xy - 1.0) * 0.5;
    float2 coordinate = worldPosition.xz * 1.8;
    float2 cell = floor(coordinate);
    float2 ripple = 0.0;
    float ringAntialias = 1.0;
    #if defined(SHADER_STAGE_FRAGMENT)
    float footprint = max(length(ddx(coordinate)), length(ddy(coordinate)));
    ringAntialias = 1.0 - smoothstep(0.035, 0.12, footprint);
    #endif
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 neighbor = cell + float2(x, y);
            float phase = MonolithRippleHash(neighbor);
            float2 center = neighbor + float2(phase, MonolithRippleHash(neighbor.yx + 29.0)) * 0.6 + 0.2;
            float2 delta = coordinate - center;
            float radius = length(delta);
            float birthTime = _MonolithPuddleShape.x > 0.0 ? min(_MonolithPuddleSettings.z, _MonolithPuddleRainTime) : _MonolithPuddleSettings.z;
            float birthCycle = floor(birthTime * 0.85 + phase);
            float age = _MonolithPuddleSettings.z * 0.85 + phase - birthCycle;
            if (age >= 1.0) continue;
            float eventSeed = MonolithRippleHash(neighbor + birthCycle * 17.0);
            float occurrence = 1.0 - smoothstep(rain * 0.85, rain * 0.85 + 0.1, eventSeed);
            float ring = sin((radius - age * 0.8) * 40.0) * saturate(1.0 - abs(radius - age * 0.8) * 12.0) * Sq(1.0 - age) * smoothstep(0.0, 0.12, age) * saturate(1.0 - radius);
            ripple += delta / max(radius, 0.001) * ring * occurrence;
        }
    }
    float wind = max(0.02, _MonolithPuddleRipple.z);
    float2 perturbation = (wave * (wind + rain * 0.16) + ripple * rain * ringAntialias) * _MonolithPuddleSettings.x * optics.w;
    float3 waterNormal = normalize(float3(perturbation.x, 1.0, perturbation.y));
    return normalize(lerp(film, waterNormal, saturate(wetness.y * wetness.y * 1.8)));
}

#endif
