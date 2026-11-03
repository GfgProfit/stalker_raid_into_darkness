#ifndef MONOLITH_FLORA_INCLUDED
#define MONOLITH_FLORA_INCLUDED

#define MONOLITH_FLORA_AREF (200.0 / 255.0)
#define MONOLITH_FLORA_GRASS_AREF (170.0 / 255.0)
#define MONOLITH_FLORA_SOURCE_ANOMALY 1.0
#define MONOLITH_FLORA_SOURCE_TRUE_STALKER 2.0

struct MonolithWindSetup
{
    float2 direction;
    float speed;
    float sqrtSpeed;
    float treesAnimSpeed;
    float treesTrunkAnimSpeed;
    float treesBend;
    float grassAnimSpeed;
    float grassTurbulence;
    float grassPush;
    float grassWave;
};

float MonolithFloraSourceMode(float source, float hasBump, float hasCorrection)
{
    if (hasBump < 0.5) return 0.0;
    if (source > 0.5 && source < 1.5) return MONOLITH_FLORA_SOURCE_ANOMALY;
    if (source > 1.5 && source < 2.5 && hasCorrection > 0.5) return MONOLITH_FLORA_SOURCE_TRUE_STALKER;
    return 0.0;
}

float MonolithFloraWetScale(float wetness, float scale)
{
    return max(scale * wetness, 3.0);
}

float3 MonolithAnomalyFloraNormal(float4 bump, float wetness)
{
    float2 normal = (bump.ab * 2.0 - 1.0) * MonolithFloraWetScale(wetness, 5.0);
    return normalize(float3(normal.x, 1.0, normal.y));
}

float3 MonolithTrueStalkerFloraNormal(float4 bump, float4 correction, float wetness, float offset)
{
    float2 normal = bump.ab + (correction.rg - 1.0);
    normal = (normal + offset) * MonolithFloraWetScale(wetness, 5.0);
    return normalize(float3(normal.x, 1.0, normal.y));
}

float3 MonolithAnomalyGrassNormal(float4 bump, float3 terrainNormal, float wetness)
{
    float2 normal = (bump.ab * 2.0 - 1.0) * MonolithFloraWetScale(wetness, 10.0);
    return normalize(terrainNormal + normalize(float3(normal.x, 1.0, normal.y)));
}

float MonolithAnomalyFloraGloss(float4 floraFixes, float wetness, bool grass)
{
    return grass ? lerp(floraFixes.x, floraFixes.y, wetness) : lerp(floraFixes.z, floraFixes.w, wetness);
}

float MonolithTrueStalkerFloraGloss(float4 bump)
{
    return bump.r * bump.r;
}

float MonolithFloraMaterial()
{
    uint quantized = (uint)(0.15 / 1.333333333 * 31.0);
    return (quantized & 31u) / 31.0 * 1.333333333;
}

float MonolithFloraHash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 19.19);
    return frac((p3.x + p3.y) * p3.z);
}

float MonolithAnomalyTreeVariation(float3 treeTranslation, float treeAxisX, float frequency)
{
    float2 position = treeTranslation.xz + treeAxisX.xx * 10.0;
    return MonolithFloraHash12(floor(position * frequency));
}

float MonolithAnomalyGrassVariation(float3 position, float instanceAxisX, float frequency)
{
    return MonolithFloraHash12(floor((position.xz + instanceAxisX.xx * 2.0) * frequency));
}

float3 MonolithFloraColorVariation(float3 color, float variation, float intensity)
{
    return saturate(color + sin(variation * 4.0 + float3(0.0, 0.33, 0.66) * 6.28) * 0.5 * intensity);
}

float3 MonolithFloraOptionalColorVariation(float3 color, float variation, float intensity, float enabled)
{
    return MonolithFloraColorVariation(color, variation, enabled > 0.5 ? intensity : 0.0);
}

float MonolithFloraTreeHemisphere(float raw, float scale, float bias)
{
    return clamp(raw * scale + bias, 0.3, 1.0);
}

float MonolithFloraGrassHemisphere(float hemi)
{
    return clamp(hemi, 0.05, 1.0);
}

float MonolithFloraPackedHemisphere(float hemi)
{
    return (uint)(saturate(hemi) * 255.9) / 254.8;
}

float3 MonolithFloraUnorm8(float3 value)
{
    return round(saturate(value) * 255.0) / 255.0;
}

float MonolithAnomalyGrassDither(float2 pixel)
{
    float thresholds[16] = { 1.0 / 17.0, 9.0 / 17.0, 3.0 / 17.0, 11.0 / 17.0, 13.0 / 17.0, 5.0 / 17.0, 15.0 / 17.0, 7.0 / 17.0, 4.0 / 17.0, 12.0 / 17.0, 2.0 / 17.0, 10.0 / 17.0, 16.0 / 17.0, 8.0 / 17.0, 14.0 / 17.0, 6.0 / 17.0 };
    uint index = ((uint)pixel.x % 4) * 4 + (uint)pixel.y % 4;
    return thresholds[index];
}

float3 MonolithTrueStalkerOmniUnshadowedNormal(float3 position, float3 lightPosition)
{
    return -normalize(position - lightPosition);
}

MonolithWindSetup MonolithSsfxWindSetup(float4 windParams, float4 grassSetup, float4 treesSetup)
{
    MonolithWindSetup setup;
    float r = -windParams.x + 1.57079;
    setup.direction = float2(cos(r), sin(r));
    setup.speed = max(treesSetup.w, saturate(windParams.y * 0.001));
    setup.sqrtSpeed = saturate(sqrt(setup.speed * 1.66));
    setup.grassAnimSpeed = grassSetup.x;
    setup.grassTurbulence = grassSetup.y;
    setup.grassPush = grassSetup.z;
    setup.grassWave = grassSetup.w;
    setup.treesAnimSpeed = treesSetup.x;
    setup.treesTrunkAnimSpeed = treesSetup.y;
    setup.treesBend = treesSetup.z;
    return setup;
}

float3 MonolithSsfxWindTrunk(float treeBaseHeight, float treeHeight, float4 anim, MonolithWindSetup setup)
{
    float phase = treeBaseHeight + anim.z * setup.treesTrunkAnimSpeed;
    float wave = (cos(phase) * sin(phase * 5.0) + 0.5) * setup.treesBend;
    float speed = saturate(setup.sqrtSpeed * 1.5);
    float bend = speed * 0.006 * saturate(1.0 - treeHeight * 0.005);
    bend *= treeHeight * treeHeight * wave * speed;
    return float3(bend.xx * setup.direction, saturate((wave + 1.0) * 0.5));
}

float3 MonolithSsfxWindBranches(TEXTURE2D_PARAM(waves, wavesSampler), float3 position, float treeBaseHeight, float treeHeight, float tcY, float4 anim, MonolithWindSetup setup)
{
    float2 offset = -anim.xy * setup.treesAnimSpeed;
    float3 flow = SAMPLE_TEXTURE2D_LOD(waves, wavesSampler, (position.xz + offset) * 0.02, 0).rgb;
    float3 flow2 = SAMPLE_TEXTURE2D_LOD(waves, wavesSampler, (position.xz + offset * 0.2) * 0.1, 0).rgb;
    float3 motion = float3(flow.x, flow2.y, flow.y) * 2.0 - 1.0;
    float3 trunk = MonolithSsfxWindTrunk(treeBaseHeight, treeHeight, anim, setup);
    motion.xz *= trunk.z * clamp(treeHeight * 0.1, 1.0, 2.5);
    motion.xz += flow2.z * setup.direction;
    motion.y *= saturate(treeHeight * 0.1);
    motion *= (1.0 - tcY) * setup.speed;
    motion.xz += trunk.xy;
    return motion;
}

float3 MonolithSsfxWindGrass(TEXTURE2D_PARAM(waves, wavesSampler), float3 position, float height, float4 anim, MonolithWindSetup setup)
{
    float limit = saturate(height * height - 0.01) * saturate(1.0 - height * 0.1);
    float2 offset = -anim.xy * setup.grassAnimSpeed;
    float3 flow = SAMPLE_TEXTURE2D_LOD(waves, wavesSampler, (position.xz + offset) * 0.018, 0).rgb;
    float2 motion = (flow.xy * 2.0 - 1.0) * setup.grassTurbulence;
    float2 push = flow.z * setup.direction * setup.grassPush;
    return float3(motion.x + push.x, flow.z * setup.grassWave, motion.y + push.y) * setup.speed * limit;
}

float MonolithFloraCyclic(float x)
{
    float f = 2.8284271 * frac(x) - 1.4142136;
    return f * f - 1.0;
}

float2 MonolithXRayTreeWind(float3 position, float baseHeight, float4 wave, float4 wind, float rigidity, float amplitude)
{
    float dp = MonolithFloraCyclic(wave.w + dot(position, wave.xyz));
    float intensity = (position.y - baseHeight) * dp;
    return lerp(float2(0.0, 0.0), wind.xz * intensity * amplitude, rigidity);
}

float2 MonolithXRayGrassWind(float3 position, float baseHeight, float4 wave, float4 direction, float rigidity)
{
    float dp = MonolithFloraCyclic(dot(float4(position, 1.0), wave));
    float intensity = (position.y - baseHeight) * dp;
    return lerp(float2(0.0, 0.0), direction.xz * intensity, rigidity);
}

#endif
