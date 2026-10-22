#define GetSurfaceAndBuiltinData MonolithLegacyGetSurfaceAndBuiltinData
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"
#undef GetSurfaceAndBuiltinData
#include "MonolithSkinning.hlsl"

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData)
{
    MonolithLegacyGetSurfaceAndBuiltinData(input, V, posInput, surfaceData, builtinData);
    float2 uv = input.texCoord0.xy * _BaseColorMap_ST.xy + _BaseColorMap_ST.zw;
    float4 baseSample = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, uv);
    float3 encodedAlbedo = _MonolithAlbedoSrgb > 0.5 ? LinearToSRGB(baseSample.rgb) : baseSample.rgb;
    encodedAlbedo = saturate(encodedAlbedo * _BaseColor.rgb);
    float2 bumpUv = float2(uv.x, _MonolithBumpFlipV > 0.5 ? 1.0 - uv.y : uv.y);
    float4 packed = SAMPLE_TEXTURE2D(_MonolithBump, sampler_MonolithBump, bumpUv);
    float gloss = _MonolithHasBump > 0.5 ? packed.r : _MonolithFallbackGloss;
    gloss = saturate(gloss * _MonolithGlossScale);
    gloss = round(gloss * 255.0) / 255.0;
    #ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
    _MonolithEnvironmentAlbedoGloss = float4(encodedAlbedo, gloss);
    #endif
    float material = MonolithDecodeMaterial(_MonolithMaterialId, _MonolithMaterialWeight);
    float metalness = MonolithMetalness(gloss, material);
    float rough = MonolithRoughness(gloss, material, _MonolithDynamicGloss);
    float3 tangentNormal = _MonolithHasBump > 0.5 ? MonolithDecodeNormal(packed, _MonolithFlipGreen) : float3(0.0, 0.0, 1.0);
    if (_MonolithHudNormal > 0.5 && _MonolithHasBump > 0.5)
    {
        float3 hudNormal = packed.abg * 2.0 - 1.0 + float3(0.0, 0.0, 1.0);
        hudNormal.y *= _MonolithFlipGreen > 0.5 ? -1.0 : 1.0;
        tangentNormal = dot(hudNormal, hudNormal) > 1e-8 ? normalize(hudNormal) : float3(0.0, 0.0, 1.0);
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
    surfaceData.ambientOcclusion = 1.0;
    surfaceData.specularOcclusion = 1.0;
    GetBuiltinData(input, V, posInput, surfaceData, baseSample.a * _BaseColor.a, surfaceData.normalWS, 0.0, builtinData);
}
