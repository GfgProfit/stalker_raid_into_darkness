#ifndef MONOLITH_SOURCE_TEXTURE_INCLUDED
#define MONOLITH_SOURCE_TEXTURE_INCLUDED

float2 MonolithSourceXRayUv(float2 uv)
{
    return float2(uv.x, 1.0 - uv.y);
}

float2 MonolithSourceTextureUv(float2 tc, float flipV)
{
    return float2(tc.x, flipV > 0.5 ? tc.y : 1.0 - tc.y);
}

float3 MonolithSourceEye(float3 tangent, float3 bitangent, float3 normal, float3 toCamera, float flipGreen)
{
    return float3(dot(tangent, toCamera), dot(bitangent, toCamera) * (flipGreen > 0.5 ? -1.0 : 1.0), dot(normal, toCamera));
}

float2 MonolithSourceParallax(float2 tc, float height, float3 eye, float2 parallax)
{
    return tc + (height * parallax.x + parallax.y) * normalize(eye).xy;
}

float2 MonolithSourceSteepParallax(TEXTURE2D_PARAM(heightMap, heightSampler), float2 tc, float3 eyeVector, float viewDepth, float4 pom, float flipV)
{
    float distanceFade = 1.0 - smoothstep(pom.y * 0.8, pom.y, viewDepth);
    if (viewDepth >= pom.y)
    {
        return tc;
    }

    float3 eye = normalize(eyeVector);
    float viewAngle = abs(eye.z);
    float stepSize = rcp(lerp(pom.x, 3.0, viewAngle)) * 0.5;
    float2 viewDirection = eye.xy / (abs(eye.z) + 0.41);
    float2 tcStep = stepSize * viewDirection * pom.z * distanceFade;
    float currentStep = 0.0;
    float2 parallaxTc = tc;
    float previousHeight = 0.0;
    float currentHeight = 0.5;
    do
    {
        previousHeight = currentHeight;
        parallaxTc -= tcStep;
        currentStep += stepSize;
        currentHeight = 0.5 - SAMPLE_TEXTURE2D_LOD(heightMap, heightSampler, MonolithSourceTextureUv(parallaxTc, flipV), 0.0).a;
    }
    while (currentHeight >= currentStep);
    parallaxTc += tcStep;
    currentStep -= stepSize;
    currentHeight = previousHeight;
    stepSize /= 3.0;
    tcStep /= 3.0;
    do
    {
        previousHeight = currentHeight;
        parallaxTc -= tcStep;
        currentStep += stepSize;
        currentHeight = 0.5 - SAMPLE_TEXTURE2D_LOD(heightMap, heightSampler, MonolithSourceTextureUv(parallaxTc, flipV), 0.0).a;
    }
    while (currentHeight >= currentStep);
    float currentDifference = currentHeight - currentStep;
    float ratio = currentDifference / (currentDifference - saturate(previousHeight - currentStep + stepSize));
    return lerp(parallaxTc, parallaxTc + tcStep, ratio);
}

float3 MonolithSourceSteepOcclusion(float3 base, float height, float pomOcclusion)
{
    return base * lerp(1.0, saturate((height + 0.15) * 2.0), pomOcclusion);
}

float3 MonolithSourceDetailNormal(float4 bump, float4 detailBump, float4 detailCorrection)
{
    float3 baseNormal = bump.abg * 2.0 - 1.0;
    float3 detailNormal = detailBump.abg * 2.0 - 1.0 + (detailCorrection.rgb * 2.0 - 1.0);
    return normalize(float3(baseNormal.xy + detailNormal.xy, baseNormal.z * detailNormal.z));
}

#endif
