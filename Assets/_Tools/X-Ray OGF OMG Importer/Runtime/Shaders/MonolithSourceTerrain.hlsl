#ifndef MONOLITH_SOURCE_TERRAIN_INCLUDED
#define MONOLITH_SOURCE_TERRAIN_INCLUDED
#include "MonolithSourceTexture.hlsl"

struct MonolithTerrainLayer
{
    float3 color;
    float3 normal;
    float gloss;
    float weight;
    float height;
    float occlusion;
    float2 tc;
};

float4 MonolithTerrainWeights(float4 mask)
{
    float sum = dot(mask, 1.0);
    return sum > 0.0 ? mask / sum : 0.0;
}

MonolithTerrainLayer MonolithTerrainInitialize(float height, float offset, float weight, float2 tc, float ao)
{
    MonolithTerrainLayer layer = (MonolithTerrainLayer)0;
    layer.height = saturate(height + offset);
    layer.occlusion = (0.125 - layer.height * layer.height * 0.125) * ao;
    layer.weight = weight;
    layer.tc = tc;
    return layer;
}

void MonolithTerrainParallax(TEXTURE2D_PARAM(heightMap, heightSampler), inout MonolithTerrainLayer layer, float2 tcStep, float stepSize, float viewDepth, float4 pom, float offset, float flipV)
{
    if (layer.weight <= 0.0 || viewDepth >= pom.y)
    {
        return;
    }

    float currentStep = 0.0;
    float2 tc = layer.tc;
    float previousHeight = 0.0;
    float currentHeight = 0.5 - layer.height * layer.weight;
    float previousSample = 0.0;
    float currentSample = layer.height;
    do
    {
        previousHeight = currentHeight;
        previousSample = currentSample;
        tc -= tcStep;
        currentStep += stepSize;
        currentSample = saturate(SAMPLE_TEXTURE2D_LOD(heightMap, heightSampler, MonolithSourceTextureUv(tc, flipV), 0.0).r + offset);
        currentHeight = 0.5 - currentSample * layer.weight;
    } while (currentHeight > currentStep);

    tc += tcStep;
    currentStep -= stepSize;
    currentHeight = previousHeight;
    stepSize /= 3.0;
    tcStep /= 3.0;
    do
    {
        previousHeight = currentHeight;
        previousSample = currentSample;
        tc -= tcStep;
        currentStep += stepSize;
        currentSample = saturate(SAMPLE_TEXTURE2D_LOD(heightMap, heightSampler, MonolithSourceTextureUv(tc, flipV), 0.0).r + offset);
        currentHeight = 0.5 - currentSample * layer.weight;
    } while (currentHeight >= currentStep);

    float currentDifference = currentHeight - currentStep;
    float ratio = currentDifference / (currentDifference - saturate(previousHeight - currentStep + stepSize));
    layer.tc = lerp(tc, tc + tcStep, ratio);
    layer.height = lerp(currentSample, previousSample, ratio);
}

void MonolithTerrainRay(float3 eyeVector, float viewDepth, float4 pom, out float2 tcStep, out float stepSize)
{
    float distanceFade = 1.0 - smoothstep(16.0, 20.0, viewDepth);
    float3 eye = normalize(eyeVector);
    stepSize = rcp(lerp(pom.x, 3.0, abs(eye.z))) * 0.5;
    float2 viewDirection = eye.xy / (abs(eye.z) + 0.41);
    tcStep = stepSize * viewDirection * pom.z * distanceFade;
}

void MonolithTerrainTextures(TEXTURE2D_PARAM(colorMap, colorSampler), TEXTURE2D_PARAM(bumpMap, bumpSampler), inout MonolithTerrainLayer layer, float colorFlipV, float bumpFlipV, float srgb)
{
    if (layer.weight <= 0.0)
    {
        return;
    }

    float3 color = SAMPLE_TEXTURE2D(colorMap, colorSampler, MonolithSourceTextureUv(layer.tc, colorFlipV)).rgb;
    float4 bump = SAMPLE_TEXTURE2D(bumpMap, bumpSampler, MonolithSourceTextureUv(layer.tc, bumpFlipV));
    layer.color = (srgb > 0.5 ? LinearToSRGB(color) : color) * (1.0 - layer.occlusion);
    layer.normal = bump.abg * 2.0 - 1.0;
    layer.gloss = bump.r;
}

void MonolithTerrainMix(MonolithTerrainLayer r, MonolithTerrainLayer g, MonolithTerrainLayer b, MonolithTerrainLayer a, float3 baseColor, out float3 color, out float3 normal, out float gloss, out float height, out float ao)
{
    color = (r.color * r.weight + g.color * g.weight + b.color * b.weight + a.color * a.weight) * baseColor * 2.0;
    normal = r.normal * r.weight + g.normal * g.weight + b.normal * b.weight + a.normal * a.weight;
    gloss = r.gloss * r.weight + g.gloss * g.weight + b.gloss * b.weight + a.gloss * a.weight;
    height = r.height * r.weight + g.height * g.weight + b.height * b.weight + a.height * a.weight;
    ao = 1.0 - (r.occlusion * r.weight + g.occlusion * g.weight + b.occlusion * b.weight + a.occlusion * a.weight);
}

float3 MonolithTerrainPosition(float3 position, float3 geometricNormal, float height)
{
    float3 eye = normalize(position);
    float d1 = dot(geometricNormal, geometricNormal * (1.0 - height * 1.5) * -0.11);
    float d2 = dot(eye, geometricNormal);
    return position + eye * (d1 / min(d2, -0.3));
}

#endif
