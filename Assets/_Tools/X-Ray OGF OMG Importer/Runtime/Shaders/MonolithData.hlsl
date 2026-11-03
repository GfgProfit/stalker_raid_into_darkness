#define GetSurfaceAndBuiltinData MonolithLegacyGetSurfaceAndBuiltinData
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"
#undef GetSurfaceAndBuiltinData
#include "MonolithSkinning.hlsl"

#if defined(_MONOLITH_LEVEL_TERRAIN) && (SHADERPASS == SHADERPASS_FORWARD || defined(WRITE_NORMAL_BUFFER))
#define MONOLITH_SOURCE_TERRAIN
#include "MonolithSourceTerrain.hlsl"

void MonolithSourceTerrainSplat(float2 uv, float3 eyeVector, float viewDepth, inout float3 color, out float3 normal, out float gloss, out float height, out float ao)
{
    float2 tc = MonolithSourceXRayUv(uv);
    float4 weights = MonolithTerrainWeights(SAMPLE_TEXTURE2D(_MonolithTerrainMask, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainMaskFlipV)));
    tc *= _MonolithTerrainDetailScale;
    float4 heights = 0.0;
    bool useHeight = _MonolithHasTerrainHeight > 0.5 && (_MonolithTerrainParallax > 0.5 || _MonolithTerrainOcclusion > 0.5 || _MonolithTerrainVirtualHeight > 0.5 || _MonolithDebug > 69.5);
    [branch]
    if (useHeight)
    {
        heights.r = SAMPLE_TEXTURE2D(_MonolithTerrainHeightR, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainHeightFlipV.r)).r;
        heights.g = SAMPLE_TEXTURE2D(_MonolithTerrainHeightG, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainHeightFlipV.g)).r;
        heights.b = SAMPLE_TEXTURE2D(_MonolithTerrainHeightB, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainHeightFlipV.b)).r;
        heights.a = SAMPLE_TEXTURE2D(_MonolithTerrainHeightA, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainHeightFlipV.a)).r;
    }
    float occlusion = useHeight && _MonolithTerrainOcclusion > 0.5 ? 1.0 : 0.0;
    MonolithTerrainLayer r = MonolithTerrainInitialize(heights.r, _MonolithTerrainOffset.r, weights.r, tc, occlusion);
    MonolithTerrainLayer g = MonolithTerrainInitialize(heights.g, _MonolithTerrainOffset.g, weights.g, tc, occlusion);
    MonolithTerrainLayer b = MonolithTerrainInitialize(heights.b, _MonolithTerrainOffset.b, weights.b, tc, occlusion);
    MonolithTerrainLayer a = MonolithTerrainInitialize(heights.a, _MonolithTerrainOffset.a, weights.a, tc, occlusion);
    [branch]
    if (useHeight && _MonolithTerrainParallax > 0.5)
    {
        float2 tcStep;
        float stepSize;
        MonolithTerrainRay(eyeVector, viewDepth, _MonolithTerrainPom, tcStep, stepSize);
        MonolithTerrainParallax(TEXTURE2D_ARGS(_MonolithTerrainHeightR, sampler_MonolithTerrainBumpR), r, tcStep, stepSize, viewDepth, _MonolithTerrainPom, _MonolithTerrainOffset.r, _MonolithTerrainHeightFlipV.r);
        MonolithTerrainParallax(TEXTURE2D_ARGS(_MonolithTerrainHeightG, sampler_MonolithTerrainBumpR), g, tcStep, stepSize, viewDepth, _MonolithTerrainPom, _MonolithTerrainOffset.g, _MonolithTerrainHeightFlipV.g);
        MonolithTerrainParallax(TEXTURE2D_ARGS(_MonolithTerrainHeightB, sampler_MonolithTerrainBumpR), b, tcStep, stepSize, viewDepth, _MonolithTerrainPom, _MonolithTerrainOffset.b, _MonolithTerrainHeightFlipV.b);
        MonolithTerrainParallax(TEXTURE2D_ARGS(_MonolithTerrainHeightA, sampler_MonolithTerrainBumpR), a, tcStep, stepSize, viewDepth, _MonolithTerrainPom, _MonolithTerrainOffset.a, _MonolithTerrainHeightFlipV.a);
    }
    MonolithTerrainTextures(TEXTURE2D_ARGS(_MonolithTerrainSourceColorR, sampler_MonolithTerrainBumpR), TEXTURE2D_ARGS(_MonolithTerrainBumpR, sampler_MonolithTerrainBumpR), r, _MonolithTerrainColorFlipV.r, _MonolithTerrainBumpFlipV.r, _MonolithTerrainColorSrgb.r);
    MonolithTerrainTextures(TEXTURE2D_ARGS(_MonolithTerrainSourceColorG, sampler_MonolithTerrainBumpR), TEXTURE2D_ARGS(_MonolithTerrainBumpG, sampler_MonolithTerrainBumpR), g, _MonolithTerrainColorFlipV.g, _MonolithTerrainBumpFlipV.g, _MonolithTerrainColorSrgb.g);
    MonolithTerrainTextures(TEXTURE2D_ARGS(_MonolithTerrainSourceColorB, sampler_MonolithTerrainBumpR), TEXTURE2D_ARGS(_MonolithTerrainBumpB, sampler_MonolithTerrainBumpR), b, _MonolithTerrainColorFlipV.b, _MonolithTerrainBumpFlipV.b, _MonolithTerrainColorSrgb.b);
    MonolithTerrainTextures(TEXTURE2D_ARGS(_MonolithTerrainSourceColorA, sampler_MonolithTerrainBumpR), TEXTURE2D_ARGS(_MonolithTerrainBumpA, sampler_MonolithTerrainBumpR), a, _MonolithTerrainColorFlipV.a, _MonolithTerrainBumpFlipV.a, _MonolithTerrainColorSrgb.a);
    MonolithTerrainMix(r, g, b, a, color, color, normal, gloss, height, ao);
    color = saturate(color);
    if (_MonolithDebug < 0.5 || _MonolithDiagnosticOptions.x < 0.5) color = round(color * 255.0) / 255.0;
    if (_MonolithDebug > 69.5 && _MonolithDebug < 70.5) _MonolithDiagnosticValue = weights.rgb;
    if (_MonolithDebug > 70.5 && _MonolithDebug < 71.5) _MonolithDiagnosticValue = weights.aaa;
    if (_MonolithDebug > 71.5 && _MonolithDebug < 72.5) _MonolithDiagnosticValue = r.color;
    if (_MonolithDebug > 72.5 && _MonolithDebug < 73.5) _MonolithDiagnosticValue = g.color;
    if (_MonolithDebug > 73.5 && _MonolithDebug < 74.5) _MonolithDiagnosticValue = b.color;
    if (_MonolithDebug > 74.5 && _MonolithDebug < 75.5) _MonolithDiagnosticValue = a.color;
    if (_MonolithDebug > 75.5 && _MonolithDebug < 76.5) _MonolithDiagnosticValue = color;
    if (_MonolithDebug > 76.5 && _MonolithDebug < 77.5) _MonolithDiagnosticValue = r.normal * 0.5 + 0.5;
    if (_MonolithDebug > 77.5 && _MonolithDebug < 78.5) _MonolithDiagnosticValue = g.normal * 0.5 + 0.5;
    if (_MonolithDebug > 78.5 && _MonolithDebug < 79.5) _MonolithDiagnosticValue = b.normal * 0.5 + 0.5;
    if (_MonolithDebug > 79.5 && _MonolithDebug < 80.5) _MonolithDiagnosticValue = a.normal * 0.5 + 0.5;
    if (_MonolithDebug > 80.5 && _MonolithDebug < 81.5) _MonolithDiagnosticValue = normal * 0.5 + 0.5;
    if (_MonolithDebug > 81.5 && _MonolithDebug < 82.5) _MonolithDiagnosticValue = height.xxx;
    if (_MonolithDebug > 82.5 && _MonolithDebug < 83.5) _MonolithDiagnosticValue = float3((r.tc - tc) * 32.0 + 0.5, 0.0);
    if (_MonolithDebug > 83.5 && _MonolithDebug < 84.5) _MonolithDiagnosticValue = ao.xxx;
}
void MonolithTrueStalkerSplat(float2 uv, inout float3 color, out float3 normal, out float gloss)
{
    float2 tc = MonolithSourceXRayUv(uv);
    float4 weights = MonolithTrueStalkerWeights(SAMPLE_TEXTURE2D(_MonolithTerrainMask, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainMaskFlipV)));
    tc *= _MonolithTerrainDetailScale;
    float4 bumpR = SAMPLE_TEXTURE2D(_MonolithTerrainBumpR, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainBumpFlipV.r));
    float4 bumpG = SAMPLE_TEXTURE2D(_MonolithTerrainBumpG, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainBumpFlipV.g));
    float4 bumpB = SAMPLE_TEXTURE2D(_MonolithTerrainBumpB, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainBumpFlipV.b));
    float4 bumpA = SAMPLE_TEXTURE2D(_MonolithTerrainBumpA, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainBumpFlipV.a));
    float3 colorR = SAMPLE_TEXTURE2D(_MonolithTerrainSourceColorR, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainColorFlipV.r)).rgb;
    float3 colorG = SAMPLE_TEXTURE2D(_MonolithTerrainSourceColorG, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainColorFlipV.g)).rgb;
    float3 colorB = SAMPLE_TEXTURE2D(_MonolithTerrainSourceColorB, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainColorFlipV.b)).rgb;
    float3 colorA = SAMPLE_TEXTURE2D(_MonolithTerrainSourceColorA, sampler_MonolithTerrainBumpR, MonolithSourceTextureUv(tc, _MonolithTerrainColorFlipV.a)).rgb;
    MonolithTrueStalkerBlend(color, weights, colorR, colorG, colorB, colorA, bumpR, bumpG, bumpB, bumpA, color, normal, gloss);
    if (_MonolithDebug > 69.5 && _MonolithDebug < 70.5) _MonolithDiagnosticValue = weights.rgb;
    if (_MonolithDebug > 70.5 && _MonolithDebug < 71.5) _MonolithDiagnosticValue = weights.aaa;
    if (_MonolithDebug > 71.5 && _MonolithDebug < 72.5) _MonolithDiagnosticValue = colorR;
    if (_MonolithDebug > 72.5 && _MonolithDebug < 73.5) _MonolithDiagnosticValue = colorG;
    if (_MonolithDebug > 73.5 && _MonolithDebug < 74.5) _MonolithDiagnosticValue = colorB;
    if (_MonolithDebug > 74.5 && _MonolithDebug < 75.5) _MonolithDiagnosticValue = colorA;
    if (_MonolithDebug > 75.5 && _MonolithDebug < 76.5) _MonolithDiagnosticValue = color;
    if (_MonolithDebug > 76.5 && _MonolithDebug < 77.5) _MonolithDiagnosticValue = MonolithTrueStalkerLayerNormal(bumpR) + 0.5;
    if (_MonolithDebug > 77.5 && _MonolithDebug < 78.5) _MonolithDiagnosticValue = MonolithTrueStalkerLayerNormal(bumpG) + 0.5;
    if (_MonolithDebug > 78.5 && _MonolithDebug < 79.5) _MonolithDiagnosticValue = MonolithTrueStalkerLayerNormal(bumpB) + 0.5;
    if (_MonolithDebug > 79.5 && _MonolithDebug < 80.5) _MonolithDiagnosticValue = MonolithTrueStalkerLayerNormal(bumpA) + 0.5;
    if (_MonolithDebug > 80.5 && _MonolithDebug < 81.5) _MonolithDiagnosticValue = normal + 0.5;
    if (_MonolithDebug > 81.5 && _MonolithDebug < 82.5) _MonolithDiagnosticValue = float3(0.95, MonolithTrueStalkerQuantizeMaterial(0.95), 0.0);
    if (_MonolithDebug > 82.5 && _MonolithDebug < 83.5) _MonolithDiagnosticValue = float3(frac(tc), 0.0);
    if (_MonolithDebug > 83.5 && _MonolithDebug < 84.5) _MonolithDiagnosticValue = gloss.xxx;
}

#endif

#ifdef _MONOLITH_LEVEL_TERRAIN
float3 MonolithTerrainEncoded(float4 value)
{
    return _MonolithAlbedoSrgb > 0.5 ? LinearToSRGB(value.rgb) : value.rgb;
}

void MonolithTerrainSplat(float2 uv, inout float3 encodedAlbedo, out float3 terrainNormal, out float terrainGloss)
{
    float4 mask = SAMPLE_TEXTURE2D(_MonolithTerrainMask, sampler_MonolithTerrainMask, uv);
    mask /= max(1e-5, dot(mask, 1.0));
    float2 detailUv = float2(uv.x * _MonolithTerrainDetailScale, 1.0 - (1.0 - uv.y) * _MonolithTerrainDetailScale);
    float4 bumpR = SAMPLE_TEXTURE2D(_MonolithTerrainBumpR, sampler_MonolithTerrainBumpR, detailUv).abgr;
    float4 bumpG = SAMPLE_TEXTURE2D(_MonolithTerrainBumpG, sampler_MonolithTerrainBumpR, detailUv).abgr;
    float4 bumpB = SAMPLE_TEXTURE2D(_MonolithTerrainBumpB, sampler_MonolithTerrainBumpR, detailUv).abgr;
    float4 bumpA = SAMPLE_TEXTURE2D(_MonolithTerrainBumpA, sampler_MonolithTerrainBumpR, detailUv).abgr;
    float3 mixed = (bumpR.xyz - 0.5) * mask.r + (bumpG.xyz - 0.5) * mask.g + (bumpB.xyz - 0.5) * mask.b + (bumpA.xyz - 0.5) * mask.a;
    mixed *= float3(_MonolithTerrainBumpScale, _MonolithTerrainBumpScale, 1.0);
    mixed.y *= _MonolithFlipGreen > 0.5 ? -1.0 : 1.0;
    terrainNormal = mixed;
    terrainGloss = saturate(bumpR.w * mask.r + bumpG.w * mask.g + bumpB.w * mask.b + bumpA.w * mask.a);
    float3 detail = MonolithTerrainEncoded(SAMPLE_TEXTURE2D(_MonolithTerrainDetailR, sampler_MonolithTerrainDetailR, detailUv)) * mask.r;
    detail += MonolithTerrainEncoded(SAMPLE_TEXTURE2D(_MonolithTerrainDetailG, sampler_MonolithTerrainDetailR, detailUv)) * mask.g;
    detail += MonolithTerrainEncoded(SAMPLE_TEXTURE2D(_MonolithTerrainDetailB, sampler_MonolithTerrainDetailR, detailUv)) * mask.b;
    detail += MonolithTerrainEncoded(SAMPLE_TEXTURE2D(_MonolithTerrainDetailA, sampler_MonolithTerrainDetailR, detailUv)) * mask.a;
    encodedAlbedo = saturate(2.0 * encodedAlbedo * detail);
}
#endif

#if defined(_MONOLITH_LEVEL) || defined(_MONOLITH_LEVEL_DETAIL)
float MonolithLevelHemisphere(FragInputs input, float4 baseSample)
{
    float4 instance = MonolithInstanceData();
    #ifdef _MONOLITH_LEVEL_DETAIL
    return clamp(instance.x, 0.05, 1.0);
    #else
    if (_MonolithLevelHemiMode > 0.5 && _MonolithLevelHemiMode < 1.5)
    {
        float2 lightmapUv = input.texCoord3.xy;
        lightmapUv.y = _MonolithLevelHemiFlipV > 0.5 ? 1.0 - lightmapUv.y : lightmapUv.y;
        return SAMPLE_TEXTURE2D(_MonolithLevelHemiMap, sampler_MonolithLevelHemiMap, lightmapUv).a;
    }
    if (_MonolithLevelHemiMode > 1.5 && _MonolithLevelHemiMode < 2.5)
    {
        return input.texCoord2.y * instance.x + instance.y;
    }
    if (_MonolithLevelHemiMode > 2.5)
    {
        return baseSample.a;
    }
    return _MonolithEnvHemisphere;
    #endif
}
#endif

#if (SHADERPASS == SHADERPASS_FORWARD || defined(WRITE_NORMAL_BUFFER)) && !defined(_MONOLITH_LEVEL_DETAIL) && !defined(_MONOLITH_LEVEL_LOD) && !defined(_MONOLITH_LEVEL_TERRAIN) && !defined(_MONOLITH_LEVEL_FLORA)
#define MONOLITH_SOURCE_TEXTURE
void MonolithSourceBasis(FragInputs input, out float3 tangent, out float3 bitangent, out float3 normal)
{
    tangent = input.tangentToWorld[0];
    bitangent = input.tangentToWorld[1];
    normal = input.tangentToWorld[2];
    #ifdef _MONOLITH_ORIGINAL_TBN
    if (_MonolithOriginalTbn > 0.5 && input.color.a > 1.5)
    {
        #ifdef _MONOLITH_SKINNED_TBN
        tangent = input.color.rgb;
        bitangent = float3(input.texCoord1.xy, input.texCoord2.x);
        normal = float3(input.texCoord2.y, input.texCoord3.xy);
        #else
        tangent = TransformObjectToWorldDir(input.color.rgb, false);
        bitangent = TransformObjectToWorldDir(float3(input.texCoord1.xy, input.texCoord2.x), false);
        normal = TransformObjectToWorldDir(float3(input.texCoord2.y, input.texCoord3.xy), false);
        #endif
    }
    #endif
    #ifdef _MONOLITH_LEVEL
    float3x3 levelObjectToWorld = (float3x3)GetObjectToWorldMatrix();
    tangent = mul(levelObjectToWorld, input.color.rgb * 2.0 - 1.0);
    bitangent = mul(levelObjectToWorld, 1.0 - float3(input.texCoord1.xy, input.texCoord2.x) * 2.0);
    normal = mul(levelObjectToWorld, normalize(mul(transpose(levelObjectToWorld), input.tangentToWorld[2])));
    #endif
}
#endif

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData)
{
    if (_MonolithDebug > 62.5 && _MonolithDebug < 63.5)
    {
        _MonolithDiagnosticValue = 1.0;
    }
    if (_MonolithDebug > 68.5 && _MonolithDebug < 69.5) _MonolithDiagnosticValue = 0.0;

    _MonolithTrueStalkerActive = 0.0;
    _MonolithFloraSourceActive = 0.0;
    MonolithLegacyGetSurfaceAndBuiltinData(input, V, posInput, surfaceData, builtinData);
    float2 uv = input.texCoord0.xy * _BaseColorMap_ST.xy + _BaseColorMap_ST.zw;
    #ifdef MONOLITH_SOURCE_TEXTURE
    float sourceHeight = 0.0;
    bool sourceSteep = false;
    bool sourceDetail = false;
    float2 sourceDetailUv = float2(0.0, 0.0);
    float4 sourceDetailBump = float4(0.5, 0.5, 0.5, 0.5);
    float4 sourceDetailCorrection = float4(0.5, 0.5, 0.5, 0.5);
    float sourceDetailMode = _MonolithHasDetail > 0.5 ? min(_MonolithSourceDetail, _MonolithHasDetailBump > 0.5 ? 2.0 : 1.0) : 0.0;
    if (_MonolithSourceProfile > 0.5)
    {
        bool sourceRange = _MonolithSourceTextureRange <= 0.0 || length(posInput.positionWS) < _MonolithSourceTextureRange;
        float2 sourceTc = MonolithSourceXRayUv(uv);
        if (_MonolithSourceParallax > 0.5 && _MonolithHasBump > 0.5 && _MonolithHasBumpCorrection > 0.5)
        {
            float3 sourceTangent;
            float3 sourceBitangent;
            float3 sourceNormal;
            MonolithSourceBasis(input, sourceTangent, sourceBitangent, sourceNormal);
            float3 sourceEye = MonolithSourceEye(sourceTangent, sourceBitangent, sourceNormal, -posInput.positionWS, _MonolithFlipGreen);
            float2 parallaxTc = sourceTc;
            if (_MonolithSourceParallax > 1.5)
            {
                parallaxTc = MonolithSourceSteepParallax(TEXTURE2D_ARGS(_MonolithBumpCorrection, sampler_MonolithBump), sourceTc, sourceEye, dot(posInput.positionWS, -UNITY_MATRIX_V[2].xyz), _MonolithPom, _MonolithBumpFlipV);
                sourceHeight = SAMPLE_TEXTURE2D(_MonolithBumpCorrection, sampler_MonolithBump, MonolithSourceTextureUv(parallaxTc, _MonolithBumpFlipV)).r;
                sourceSteep = sourceRange;
            }
            else
            {
                sourceHeight = SAMPLE_TEXTURE2D(_MonolithBumpCorrection, sampler_MonolithBump, MonolithSourceTextureUv(sourceTc, _MonolithBumpFlipV)).a;
                parallaxTc = MonolithSourceParallax(sourceTc, sourceHeight, sourceEye, float2(_MonolithParallaxHeight, -0.5 * _MonolithParallaxHeight));
            }
            sourceHeight = sourceRange ? sourceHeight : 0.0;
            sourceTc = sourceRange ? parallaxTc : sourceTc;
            uv = sourceRange ? MonolithSourceXRayUv(parallaxTc) : uv;
        }
        sourceDetail = sourceDetailMode > 0.5 && sourceRange;
        sourceDetailUv = MonolithSourceTextureUv(sourceTc * _MonolithDetailScale, _MonolithDetailFlipV);
        if (sourceDetailMode > 1.5 && _MonolithHasBump > 0.5)
        {
            sourceDetailBump = SAMPLE_TEXTURE2D(_MonolithDetailBump, sampler_MonolithDetailMap, sourceDetailUv);
            sourceDetailCorrection = SAMPLE_TEXTURE2D(_MonolithDetailBumpCorrection, sampler_MonolithDetailMap, sourceDetailUv);
        }
    }
    #endif
    float2 baseUv = uv;
    float4 baseSample;
    #ifdef MONOLITH_SOURCE_TERRAIN
    bool trueStalkerTerrain = MonolithTrueStalkerTerrainEnabled();
    _MonolithTrueStalkerActive = trueStalkerTerrain ? 1.0 : 0.0;
    bool sourceTerrainActive = _MonolithSourceProfile > 0.5 && _MonolithSourceTerrain > 0.5 && _MonolithTerrainSource > 0.5 && _MonolithTerrainSource < 1.5 && _MonolithHasTerrainSourceColor > 0.5;
    [branch]
    if (sourceTerrainActive || trueStalkerTerrain)
    {
        baseUv = MonolithSourceTextureUv(MonolithSourceXRayUv(uv), _MonolithTerrainBaseFlipV);
        baseSample = SAMPLE_TEXTURE2D(_MonolithTerrainSourceBase, sampler_MonolithTerrainBumpR, baseUv);
    }
    else
    #endif
    {
        baseSample = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, baseUv);
    }
    #ifdef _MONOLITH_LEVEL_LOD
    float lodFactor = input.texCoord2.x;
    float2 lodUv = input.texCoord1.xy * _BaseColorMap_ST.xy + _BaseColorMap_ST.zw;
    baseSample = lerp(baseSample, SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, lodUv), lodFactor);
    baseSample.a *= input.texCoord2.y;
    clip(baseSample.a - 96.0 / 255.0);
    float4 lodNormal0 = SAMPLE_TEXTURE2D(_MonolithBump, sampler_MonolithBump, float2(uv.x, _MonolithBumpFlipV > 0.5 ? 1.0 - uv.y : uv.y));
    float4 lodNormal1 = SAMPLE_TEXTURE2D(_MonolithBump, sampler_MonolithBump, float2(lodUv.x, _MonolithBumpFlipV > 0.5 ? 1.0 - lodUv.y : lodUv.y));
    float4 lodNormal = lerp(lodNormal0, lodNormal1, lodFactor);
    #endif
    float3 encodedAlbedo = _MonolithAlbedoSrgb > 0.5 ? LinearToSRGB(baseSample.rgb) : baseSample.rgb;
    #ifdef MONOLITH_SOURCE_TERRAIN
    if (sourceTerrainActive || trueStalkerTerrain) encodedAlbedo = baseSample.rgb;
    #endif
    encodedAlbedo = saturate(encodedAlbedo * _BaseColor.rgb);
    #ifdef _MONOLITH_LEVEL_DETAIL
    float grassSource = _MonolithGrassSource;
    if (grassSource > 0.5 && grassSource < 1.5)
    {
        clip(MonolithInstanceExtra().w - MonolithAnomalyGrassDither(posInput.positionSS));
        float variation = input.texCoord1.x;
        encodedAlbedo = MonolithFloraUnorm8(MonolithFloraOptionalColorVariation(encodedAlbedo, variation, _MonolithFloraVariation.x, _MonolithFloraVariationEnabled));
    }
    #endif
    #ifdef _MONOLITH_LEVEL_FLORA
    float floraSource = MonolithFloraSourceMode(_MonolithFloraSource, _MonolithHasBump, _MonolithHasBumpCorrection);
    float floraVariation = 0.0;
    _MonolithFloraSourceActive = floraSource;
    if (floraSource > 0.5 && floraSource < 1.5)
    {
        float4x4 floraObjectToWorld = GetObjectToWorldMatrix();
        floraVariation = MonolithAnomalyTreeVariation(GetAbsolutePositionWS(floraObjectToWorld._m03_m13_m23), floraObjectToWorld._m00, _MonolithFloraVariation.w);
        encodedAlbedo = MonolithFloraOptionalColorVariation(encodedAlbedo, floraVariation, _MonolithFloraVariation.z, _MonolithFloraVariationEnabled);
    }
    if (floraSource > 0.5) encodedAlbedo = MonolithFloraUnorm8(encodedAlbedo);
    #endif
    #ifdef MONOLITH_SOURCE_TEXTURE
    float sourceDetailGloss = 1.0;
    if (sourceSteep)
    {
        encodedAlbedo = MonolithSourceSteepOcclusion(encodedAlbedo, sourceHeight, _MonolithPom.w);
    }
    if (_MonolithSourceProfile > 0.5 && sourceDetailMode > 0.5)
    {
        float4 sourceDetailSample = SAMPLE_TEXTURE2D(_MonolithDetailMap, sampler_MonolithDetailMap, sourceDetailUv);
        float3 sourceDetailColor = _MonolithDetailSrgb > 0.5 ? LinearToSRGB(sourceDetailSample.rgb) : sourceDetailSample.rgb;
        encodedAlbedo = sourceDetail ? saturate(encodedAlbedo * sourceDetailColor * 2.0) : encodedAlbedo;
        sourceDetailGloss = sourceDetailSample.a * 2.0;
    }
    #endif
    #ifdef _MONOLITH_LEVEL_TERRAIN
    float3 terrainNormal;
    float terrainGloss;
    bool sourceTerrain = false;
    #ifdef MONOLITH_SOURCE_TERRAIN
    float terrainHeight = 0.0;
    float terrainAo = 1.0;
    float3 terrainTangent = mul((float3x3)GetObjectToWorldMatrix(), input.color.rgb * 4.0 - 2.0);
    float3 terrainBitangent = mul((float3x3)GetObjectToWorldMatrix(), float3(input.texCoord1.xy, input.texCoord2.x) * 4.0 - 2.0);
    float3 terrainGeometric = mul((float3x3)GetObjectToWorldMatrix(), float3(input.color.a, input.texCoord3.xy) * 2.0);
    sourceTerrain = sourceTerrainActive;
    [branch]
    if (sourceTerrain)
    {
        float3 eyeVector = float3(dot(terrainTangent, -posInput.positionWS), dot(terrainBitangent, -posInput.positionWS), dot(terrainGeometric, -posInput.positionWS));
        float terrainQuality = MonolithTerrainQualityMode(_MonolithTerrainQuality, _MonolithTerrainMidAvailable, _MonolithTerrainLowAvailable);
        [branch]
        if (terrainQuality > 0.5)
        {
            terrainNormal = float3(0.0, 0.0, 1.0);
            terrainGloss = 0.001;
            [branch]
            if (terrainQuality < 1.5)
            {
                float2 tc = MonolithSourceXRayUv(uv);
                encodedAlbedo = MonolithTerrainMid(TEXTURE2D_ARGS(_MonolithTerrainMask, sampler_MonolithTerrainBumpR), TEXTURE2D_ARGS(_MonolithTerrainLodAtlas, sampler_LinearRepeat), tc, encodedAlbedo, length(posInput.positionWS), _MonolithTerrainMaskFlipV, _MonolithTerrainLodFlipV);
            }
            encodedAlbedo = saturate(encodedAlbedo);
            if (_MonolithDebug < 0.5 || _MonolithDiagnosticOptions.x < 0.5) encodedAlbedo = round(encodedAlbedo * 255.0) / 255.0;
        }
        else MonolithSourceTerrainSplat(uv, eyeVector, dot(posInput.positionWS, -UNITY_MATRIX_V[2].xyz), encodedAlbedo, terrainNormal, terrainGloss, terrainHeight, terrainAo);
    }
    else if (trueStalkerTerrain) MonolithTrueStalkerSplat(uv, encodedAlbedo, terrainNormal, terrainGloss);
    else
    #endif
    {
        MonolithTerrainSplat(uv, encodedAlbedo, terrainNormal, terrainGloss);
    }
    #endif
    #if HAVE_DECALS
    if (_EnableDecals)
    {
        float decalAlpha = 1.0;
        DecalSurfaceData decalSurfaceData = GetDecalSurfaceData(posInput, input, decalAlpha);
        float decalCoverage = saturate(1.0 - decalSurfaceData.baseColor.w);
        if (decalCoverage > 1.0 / 512.0)
        {
            float3 decalEncoded = LinearToSRGB(saturate(decalSurfaceData.baseColor.rgb / decalCoverage));
            encodedAlbedo = lerp(encodedAlbedo, decalEncoded, decalCoverage);
        }
    }
    #endif
    float2 bumpUv = float2(uv.x, _MonolithBumpFlipV > 0.5 ? 1.0 - uv.y : uv.y);
    float4 packed = SAMPLE_TEXTURE2D(_MonolithBump, sampler_MonolithBump, bumpUv);
    float gloss = _MonolithHasBump > 0.5 ? packed.r : _MonolithFallbackGloss;
    #ifdef MONOLITH_SOURCE_TEXTURE
    if (sourceDetail && _MonolithHasBump > 0.5)
    {
        gloss *= sourceDetailMode > 1.5 ? sourceDetailBump.r * 2.0 : sourceDetailGloss;
    }
    #endif
    gloss = saturate(gloss * _MonolithGlossScale);
    #ifdef _MONOLITH_LEVEL_DETAIL
    gloss = _MonolithFallbackGloss;
    #endif
    #ifdef _MONOLITH_LEVEL_TERRAIN
    gloss = terrainGloss;
    #endif
    #if defined(_MONOLITH_LEVEL) || defined(_MONOLITH_LEVEL_DETAIL)
    _MonolithLevelHemiValue = MonolithLevelHemisphere(input, baseSample);
    #ifdef MONOLITH_SOURCE_TERRAIN
    if (trueStalkerTerrain) _MonolithLevelHemiValue = baseSample.a;
    #endif
    #endif
    #ifdef _MONOLITH_LEVEL_LOD
    gloss = _MonolithFallbackGloss;
    _MonolithLevelHemiValue = saturate(lodNormal.a * input.texCoord3.x);
    #endif
    #ifdef _MONOLITH_LEVEL_FLORA
    gloss = floraSource > 1.5 ? MonolithTrueStalkerFloraGloss(packed) : floraSource > 0.5 ? MonolithAnomalyFloraGloss(_MonolithFloraFixes1, _MonolithFloraWetness, false) : MonolithFloraGloss(packed.r, _MonolithGlossScale);
    #endif
    #ifdef _MONOLITH_LEVEL_DETAIL
    if (grassSource > 0.5) gloss = grassSource > 1.5 ? 2.0 / 255.0 : MonolithAnomalyFloraGloss(_MonolithFloraFixes1, _MonolithFloraWetness, true);
    #endif
    #ifdef MONOLITH_SOURCE_TERRAIN
    if (!trueStalkerTerrain && (_MonolithDebug < 0.5 || _MonolithDiagnosticOptions.x < 0.5))
    #else
    if (_MonolithDebug < 0.5 || _MonolithDiagnosticOptions.x < 0.5)
    #endif
    {
        gloss = round(gloss * 255.0) / 255.0;
    }
    #ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
    _MonolithEnvironmentAlbedoGloss = float4(encodedAlbedo, gloss);
    #endif
    float material = MonolithDecodeMaterial(_MonolithMaterialId, _MonolithMaterialWeight);
    #ifdef _MONOLITH_LEVEL_DETAIL
    if (grassSource > 0.5) material = MonolithFloraMaterial();
    #endif
    #ifdef MONOLITH_SOURCE_TERRAIN
    if (sourceTerrain) material = 22.0 / 31.0 * 1.333333333;
    if (trueStalkerTerrain) material = 0.95;
    #endif
    #ifdef _MONOLITH_LEVEL_FLORA
    if (floraSource > 0.5) material = MonolithFloraMaterial();
    #endif
    _MonolithSourceAlbedoGloss = float4(encodedAlbedo, gloss);
    _MonolithSourceRawMaterial = material;
    if (_MonolithSourceProfile > 0.5 || _MonolithTrueStalkerTerrain > 0.5)
    {
        material = MonolithSourceMaterial(material);
    }
    float metalness = MonolithMetalness(gloss, material);
    float rough = MonolithRoughness(gloss, material, MonolithDynamicGlossInput());
    float3 tangentNormal = _MonolithHasBump > 0.5 ? MonolithDecodeNormal(packed, _MonolithFlipGreen) : float3(0.0, 0.0, 1.0);
    #ifdef MONOLITH_SOURCE_TEXTURE
    if (sourceDetail && sourceDetailMode > 1.5 && _MonolithHasBump > 0.5)
    {
        tangentNormal = MonolithSourceDetailNormal(packed, sourceDetailBump, sourceDetailCorrection);
        tangentNormal.y *= _MonolithFlipGreen > 0.5 ? -1.0 : 1.0;
    }
    #endif
    #ifdef _MONOLITH_LEVEL_TERRAIN
    tangentNormal = terrainNormal;
    #endif
    if (_MonolithHudNormal > 0.5 && _MonolithHasBump > 0.5)
    {
        float3 hudNormal = packed.abg * 2.0 - 1.0 + float3(0.0, 0.0, 1.0);
        hudNormal.y *= _MonolithFlipGreen > 0.5 ? -1.0 : 1.0;
        tangentNormal = dot(hudNormal, hudNormal) > 1e-8 ? normalize(hudNormal) : float3(0.0, 0.0, 1.0);
    }
    if (_MonolithDiagnosticOptions.y > 0.5)
    {
        tangentNormal = float3(0.0, 0.0, 1.0);
    }
    surfaceData.baseColor = MonolithAlbedo(encodedAlbedo, metalness);
    surfaceData.specularColor = MonolithSpecular(encodedAlbedo, gloss, material);
    surfaceData.perceptualSmoothness = 1.0 - rough;
    surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD | MATERIALFEATUREFLAGS_LIT_SPECULAR_COLOR;
    surfaceData.metallic = 0.0;
    surfaceData.normalWS = normalize(mul(tangentNormal, input.tangentToWorld));
    surfaceData.geomNormalWS = normalize(input.tangentToWorld[2]);
    surfaceData.tangentWS = Orthonormalize(input.tangentToWorld[0], surfaceData.normalWS);
    #ifdef _MONOLITH_ORIGINAL_TBN
    if (_MonolithOriginalTbn > 0.5 && input.color.a > 1.5)
    {
        #ifdef _MONOLITH_SKINNED_TBN
        float3 originalT = input.color.rgb;
        float3 originalB = float3(input.texCoord1.xy, input.texCoord2.x);
        float3 originalN = float3(input.texCoord2.y, input.texCoord3.xy);
        #else
        float3 originalT = TransformObjectToWorldDir(input.color.rgb, false);
        float3 originalB = TransformObjectToWorldDir(float3(input.texCoord1.xy, input.texCoord2.x), false);
        float3 originalN = TransformObjectToWorldDir(float3(input.texCoord2.y, input.texCoord3.xy), false);
        #endif
        surfaceData.normalWS = normalize(2.0 * (originalT * tangentNormal.x + originalB * tangentNormal.y + originalN * tangentNormal.z));
        surfaceData.geomNormalWS = normalize(originalN);
        surfaceData.tangentWS = Orthonormalize(originalT, surfaceData.normalWS);
    }
    #endif
    #ifdef _MONOLITH_LEVEL
    {
        float3x3 levelObjectToWorld = (float3x3)GetObjectToWorldMatrix();
        float3 levelT = mul(levelObjectToWorld, input.color.rgb * 2.0 - 1.0);
        float3 levelB = mul(levelObjectToWorld, 1.0 - float3(input.texCoord1.xy, input.texCoord2.x) * 2.0);
        float3 levelN = mul(levelObjectToWorld, normalize(mul(transpose(levelObjectToWorld), input.tangentToWorld[2])));
        surfaceData.normalWS = normalize(2.0 * (levelT * tangentNormal.x + levelB * tangentNormal.y + levelN * tangentNormal.z));
        surfaceData.geomNormalWS = normalize(levelN);
        surfaceData.tangentWS = Orthonormalize(levelT, surfaceData.normalWS);
    }
    #endif
    #ifdef _MONOLITH_LEVEL_FLORA
    {
        float3 floraNormal = MonolithFloraNormal(packed.ab);
        if (floraSource > 1.5) floraNormal = MonolithTrueStalkerFloraNormal(packed, SAMPLE_TEXTURE2D(_MonolithBumpCorrection, sampler_MonolithBump, bumpUv), _MonolithFloraWetness, 0.02);
        else if (floraSource > 0.5) floraNormal = MonolithAnomalyFloraNormal(packed, _MonolithFloraWetness);
        surfaceData.normalWS = floraNormal;
        surfaceData.geomNormalWS = floraNormal;
        surfaceData.tangentWS = Orthonormalize(abs(floraNormal.x) < 0.99 ? float3(1.0, 0.0, 0.0) : float3(0.0, 0.0, 1.0), floraNormal);
        _MonolithLevelHemiValue = floraSource > 0.5 ? MonolithFloraPackedHemisphere(clamp(_MonolithLevelHemiValue, 0.3, 1.0)) : saturate(_MonolithLevelHemiValue);
        if (_MonolithDebug > 84.5 && _MonolithDebug < 85.5) _MonolithDiagnosticValue = float3(baseSample.a * _BaseColor.a, baseSample.a * _BaseColor.a >= _AlphaCutoff ? 1.0 : 0.0, floraSource * 0.5);
        if (_MonolithDebug > 85.5 && _MonolithDebug < 86.5) _MonolithDiagnosticValue = float3(floraVariation, _MonolithLevelHemiValue, material);
        if (_MonolithDebug > 86.5 && _MonolithDebug < 87.5) _MonolithDiagnosticValue = float3(gloss, encodedAlbedo.g, floraSource > 0.5 ? 1.0 : 0.0);
    }
    #endif
    #ifdef _MONOLITH_LEVEL_DETAIL
    {
        float3 detailNormal = float3((tangentNormal.x + 0.1) * 3.0, 1.0, (tangentNormal.y + 0.1) * 3.0);
        if (_MonolithHasBump < 0.5)
        {
            detailNormal = posInput.positionWS - GetObjectToWorldMatrix()._m03_m13_m23 + float3(0.0, 0.75, 0.0);
        }
        surfaceData.normalWS = normalize(detailNormal);
        if (grassSource > 1.5 && _MonolithHasBumpCorrection > 0.5) surfaceData.normalWS = MonolithTrueStalkerFloraNormal(packed, SAMPLE_TEXTURE2D(_MonolithBumpCorrection, sampler_MonolithBump, bumpUv), _MonolithFloraWetness, 0.1);
        else if (grassSource > 0.5 && grassSource < 1.5) surfaceData.normalWS = MonolithAnomalyGrassNormal(packed, MonolithInstanceExtra().xyz, _MonolithFloraWetness);
        if (grassSource > 0.5) _MonolithLevelHemiValue = MonolithFloraPackedHemisphere(MonolithFloraGrassHemisphere(_MonolithLevelHemiValue));
        surfaceData.geomNormalWS = normalize(input.tangentToWorld[2]);
        surfaceData.tangentWS = Orthonormalize(input.tangentToWorld[0], surfaceData.normalWS);
    }
    #endif
    #ifdef _MONOLITH_LEVEL_LOD
    {
        float4x4 lodWorldToView = GetWorldToViewMatrix();
        float3 lodN = MonolithLodNormal(lodNormal.rgb, lodWorldToView[0].xyz, lodWorldToView[1].xyz, lodWorldToView[2].xyz);
        float3 lodT = abs(lodN.y) < 0.999 ? normalize(cross(float3(0.0, 1.0, 0.0), lodN)) : float3(1.0, 0.0, 0.0);
        surfaceData.normalWS = lodN;
        surfaceData.geomNormalWS = lodN;
        surfaceData.tangentWS = lodT;
    }
    #endif
    surfaceData.ambientOcclusion = 1.0;
    surfaceData.specularOcclusion = 1.0;
    #ifdef MONOLITH_SOURCE_TERRAIN
    if (sourceTerrain || trueStalkerTerrain)
    {
        float3 terrainWorldNormal = terrainTangent * tangentNormal.x + terrainBitangent * tangentNormal.y + terrainGeometric * tangentNormal.z;
        surfaceData.normalWS = trueStalkerTerrain ? MonolithTrueStalkerNormal(terrainTangent, terrainBitangent, terrainGeometric, tangentNormal) : dot(terrainWorldNormal, terrainWorldNormal) > 0.0 ? normalize(terrainWorldNormal) : normalize(terrainGeometric);
        surfaceData.geomNormalWS = normalize(terrainGeometric);
        surfaceData.tangentWS = Orthonormalize(terrainTangent, surfaceData.normalWS);
    }
    #endif
    #ifdef MONOLITH_SOURCE_TERRAIN
    if (trueStalkerTerrain)
    {
        surfaceData.baseColor = _MonolithTrueStalkerHasLookup > 0.5 ? encodedAlbedo : SRGBToLinear(encodedAlbedo);
        surfaceData.specularColor = 0.04;
        surfaceData.perceptualSmoothness = saturate(gloss);
    }
    #endif
    GetBuiltinData(input, V, posInput, surfaceData, baseSample.a * _BaseColor.a, surfaceData.normalWS, 0.0, builtinData);
    #if defined(MONOLITH_SOURCE_TERRAIN) && SHADERPASS == SHADERPASS_FORWARD
    if (trueStalkerTerrain) posInput.positionWS = MonolithTrueStalkerPosition(posInput.positionWS, surfaceData.normalWS);
    if (sourceTerrain && _MonolithHasTerrainHeight > 0.5 && _MonolithTerrainVirtualHeight > 0.5 && MonolithTerrainQualityMode(_MonolithTerrainQuality, _MonolithTerrainMidAvailable, _MonolithTerrainLowAvailable) < 0.5)
    {
        posInput.positionWS = MonolithTerrainPosition(posInput.positionWS, surfaceData.geomNormalWS, terrainHeight);
    }
    #endif
    #if defined(MONOLITH_SOURCE_TEXTURE) && SHADERPASS == SHADERPASS_FORWARD
    if (_MonolithSourceProfile > 0.5 && _MonolithSourceVirtualHeight > 0.5)
    {
        float3 sourceForward = -UNITY_MATRIX_V[2].xyz;
        float sourceDepth = dot(posInput.positionWS, sourceForward);
        float sourceVirtualHeight = _MonolithHasBump > 0.5 ? sourceHeight : 0.5;
        posInput.positionWS *= (sourceDepth + dot(surfaceData.normalWS, sourceForward) * sourceVirtualHeight * 0.05) / sourceDepth;
    }
    #endif
    if (_MonolithDebug > 33.5 && _MonolithDebug < 34.5) _MonolithDiagnosticValue = GetAbsolutePositionWS(posInput.positionWS);
    if (_MonolithDebug > 34.5 && _MonolithDebug < 35.5) _MonolithDiagnosticValue = float3(uv, 0.0);
    if (_MonolithDebug > 36.5 && _MonolithDebug < 37.5) _MonolithDiagnosticValue = MonolithInstanceData().xyz;
    #if defined(UNITY_INSTANCING_ENABLED)
    if (_MonolithDebug > 37.5 && _MonolithDebug < 38.5) _MonolithDiagnosticValue = float3(1.0, _MonolithLevelHemiMode * 0.25, input.texCoord2.y);
    #else
    if (_MonolithDebug > 37.5 && _MonolithDebug < 38.5) _MonolithDiagnosticValue = float3(0.0, _MonolithLevelHemiMode * 0.25, input.texCoord2.y);
    #endif
    if (_MonolithDebug > 0.5 && _MonolithDebug < 19.5)
    {
        float3 debugValue = surfaceData.baseColor;
        if (_MonolithDebug > 1.5) debugValue = tangentNormal * 0.5 + 0.5;
        if (_MonolithDebug > 2.5) debugValue = gloss.xxx;
        if (_MonolithDebug > 3.5) debugValue = metalness.xxx;
        if (_MonolithDebug > 4.5) debugValue = rough.xxx;
        if (_MonolithDebug > 5.5) debugValue = surfaceData.specularColor;
        if (_MonolithDebug > 6.5) debugValue = material.xxx;
        if (_MonolithDebug > 7.5) debugValue = baseSample.rgb;
        if (_MonolithDebug > 8.5) debugValue = encodedAlbedo;
        if (_MonolithDebug > 9.5) debugValue = MonolithAlbedoBoost(encodedAlbedo);
        if (_MonolithDebug > 10.5) debugValue = pow(max(0.0, MonolithAlbedoBoost(encodedAlbedo)), 2.2);
        if (_MonolithDebug > 11.5) debugValue = surfaceData.normalWS * 0.5 + 0.5;
        if (_MonolithDebug > 12.5) debugValue = tangentNormal * 0.5 + 0.5;
        if (_MonolithDebug > 13.5) debugValue = packed.rgb;
        if (_MonolithDebug > 14.5) debugValue = packed.abg;
        if (_MonolithDebug > 15.5) debugValue = float3(material, metalness, gloss);
        if (_MonolithDebug > 16.5) debugValue = input.tangentToWorld[0] * 0.5 + 0.5;
        if (_MonolithDebug > 17.5) debugValue = input.tangentToWorld[1] * 0.5 + 0.5;
        if (_MonolithDebug > 18.5) debugValue = input.tangentToWorld[2] * 0.5 + 0.5;
        _MonolithDiagnosticValue = debugValue;
        surfaceData.baseColor = debugValue;
        surfaceData.specularColor = 0.0;
        builtinData.bakeDiffuseLighting = 0.0;
        builtinData.emissiveColor = 0.0;
    }
}
