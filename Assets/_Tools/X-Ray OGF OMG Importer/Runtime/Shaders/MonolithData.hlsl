#define GetSurfaceAndBuiltinData MonolithLegacyGetSurfaceAndBuiltinData
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"
#undef GetSurfaceAndBuiltinData
#include "MonolithSkinning.hlsl"

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

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData)
{
    if (_MonolithDebug > 62.5 && _MonolithDebug < 63.5)
    {
        _MonolithDiagnosticValue = 1.0;
    }

    MonolithLegacyGetSurfaceAndBuiltinData(input, V, posInput, surfaceData, builtinData);
    float2 uv = input.texCoord0.xy * _BaseColorMap_ST.xy + _BaseColorMap_ST.zw;
    float4 baseSample = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, uv);
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
    encodedAlbedo = saturate(encodedAlbedo * _BaseColor.rgb);
    #ifdef _MONOLITH_LEVEL_TERRAIN
    float3 terrainNormal;
    float terrainGloss;
    MonolithTerrainSplat(uv, encodedAlbedo, terrainNormal, terrainGloss);
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
    gloss = saturate(gloss * _MonolithGlossScale);
    #ifdef _MONOLITH_LEVEL_DETAIL
    gloss = _MonolithFallbackGloss;
    #endif
    #ifdef _MONOLITH_LEVEL_TERRAIN
    gloss = terrainGloss;
    #endif
    #if defined(_MONOLITH_LEVEL) || defined(_MONOLITH_LEVEL_DETAIL)
    _MonolithLevelHemiValue = MonolithLevelHemisphere(input, baseSample);
    #endif
    #ifdef _MONOLITH_LEVEL_LOD
    gloss = _MonolithFallbackGloss;
    _MonolithLevelHemiValue = saturate(lodNormal.a * input.texCoord3.x);
    #endif
    #ifdef _MONOLITH_LEVEL_FLORA
    gloss = MonolithFloraGloss(packed.r, _MonolithGlossScale);
    #endif
    if (_MonolithDebug < 0.5 || _MonolithDiagnosticOptions.x < 0.5)
    {
        gloss = round(gloss * 255.0) / 255.0;
    }
    #ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
    _MonolithEnvironmentAlbedoGloss = float4(encodedAlbedo, gloss);
    #endif
    float material = MonolithDecodeMaterial(_MonolithMaterialId, _MonolithMaterialWeight);
    float metalness = MonolithMetalness(gloss, material);
    float rough = MonolithRoughness(gloss, material, _MonolithDynamicGloss);
    float3 tangentNormal = _MonolithHasBump > 0.5 ? MonolithDecodeNormal(packed, _MonolithFlipGreen) : float3(0.0, 0.0, 1.0);
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
        surfaceData.normalWS = floraNormal;
        surfaceData.geomNormalWS = floraNormal;
        surfaceData.tangentWS = Orthonormalize(abs(floraNormal.x) < 0.99 ? float3(1.0, 0.0, 0.0) : float3(0.0, 0.0, 1.0), floraNormal);
        _MonolithLevelHemiValue = saturate(_MonolithLevelHemiValue);
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
    GetBuiltinData(input, V, posInput, surfaceData, baseSample.a * _BaseColor.a, surfaceData.normalWS, 0.0, builtinData);
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
