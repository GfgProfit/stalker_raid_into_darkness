#define EvaluateBSDF_Directional MonolithLegacyEvaluateBSDF_Directional
#define EvaluateBSDF_Punctual MonolithLegacyEvaluateBSDF_Punctual
#define GetPreLightData MonolithLegacyGetPreLightData
#define PostEvaluateBSDF MonolithLegacyPostEvaluateBSDF
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
#undef EvaluateBSDF_Directional
#undef EvaluateBSDF_Punctual
#undef GetPreLightData
#undef PostEvaluateBSDF

PreLightData GetPreLightData(float3 V, PositionInputs posInput, inout BSDFData bsdfData)
{
    PreLightData data = MonolithLegacyGetPreLightData(V, posInput, bsdfData);
    float nv = max(1e-5, dot(bsdfData.normalWS, V));
    data.specularFGD = MonolithEnvGGX(bsdfData.fresnel0, bsdfData.perceptualRoughness, nv) * Sq(saturate(dot(bsdfData.normalWS, V) * 2.0));
    data.diffuseFGD = MonolithEnvBurley(bsdfData.perceptualRoughness, nv);
    data.energyCompensation = 0.0;
    return data;
}

CBSDF MonolithEvaluateBSDF(float3 V, float3 L, PreLightData preLightData, BSDFData bsdfData)
{
    CBSDF data;
    ZERO_INITIALIZE(CBSDF, data);
    float3 N = bsdfData.normalWS;
    float3 H = SafeNormalize(V + L);
    float dotNV = dot(N, V);
    float nv = max(1e-5, dotNV);
    float nl = saturate(dot(N, L));
    float nh = saturate(dot(N, H));
    float vh = saturate(dot(V, H));
    float rough = bsdfData.perceptualRoughness;
    data.diffR = MonolithBurley(nl, nv, vh, rough) * nl * nl * PI;
    data.specR = MonolithGGX(nl, nh, nv, vh, bsdfData.fresnel0, rough) * Sq(saturate(dotNV * 2.0));
    float3 light = saturate(_MonolithSourceLight.rgb);
    data.specR *= 1.0 - (light.r + light.g + light.b) / 3.0 * (1.0 - _MonolithLightSetup.x);
    data.specR *= lerp(1.0, _MonolithSourceLight.rgb, _MonolithLightSetup.y) * nl * nl * PI;
    return data;
}

#define EvaluateBSDF MonolithEvaluateBSDF
#define ShouldEvaluateThickObjectTransmission MonolithShouldEvaluateThickObjectTransmission
#define ShadeSurface_Infinitesimal MonolithShadeSurface_Infinitesimal
#define ShadeSurface_Directional MonolithShadeSurface_Directional
#define EvaluateTransmittance_Punctual MonolithEvaluateTransmittance_Punctual
#define ShadeSurface_Punctual MonolithShadeSurface_Punctual
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/SurfaceShading.hlsl"
#undef EvaluateBSDF
#undef ShouldEvaluateThickObjectTransmission
#undef ShadeSurface_Infinitesimal
#undef ShadeSurface_Directional
#undef EvaluateTransmittance_Punctual
#undef ShadeSurface_Punctual

DirectLighting EvaluateBSDF_Directional(LightLoopContext lightLoopContext, float3 V, PositionInputs posInput, PreLightData preLightData, DirectionalLightData lightData, BSDFData bsdfData, BuiltinData builtinData)
{
    return MonolithShadeSurface_Directional(lightLoopContext, posInput, builtinData, preLightData, lightData, bsdfData, V);
}

DirectLighting EvaluateBSDF_Punctual(LightLoopContext lightLoopContext, float3 V, PositionInputs posInput, PreLightData preLightData, LightData lightData, BSDFData bsdfData, BuiltinData builtinData)
{
    return MonolithShadeSurface_Punctual(lightLoopContext, posInput, builtinData, preLightData, lightData, bsdfData, V);
}

void PostEvaluateBSDF(LightLoopContext lightLoopContext, float3 V, PositionInputs posInput, PreLightData preLightData, BSDFData bsdfData, BuiltinData builtinData, AggregateLighting lighting, out LightLoopOutput lightLoopOutput)
{
    #ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
    MonolithEnvironmentData environmentData;
    ZERO_INITIALIZE(MonolithEnvironmentData, environmentData);
    bool originalEnvironment = _MonolithEnvironmentMode > 0.5;
    if (originalEnvironment)
    {
        environmentData = MonolithEvaluateEnvironment(bsdfData.normalWS, V, _MonolithEnvironmentAlbedoGloss);
        builtinData.bakeDiffuseLighting = 0.0;
        builtinData.backBakeDiffuseLighting = 0.0;
        lighting.indirect.specularReflected = 0.0;
        lighting.indirect.specularTransmitted = 0.0;
    }
    #endif
    MonolithLegacyPostEvaluateBSDF(lightLoopContext, V, posInput, preLightData, bsdfData, builtinData, lighting, lightLoopOutput);
    #ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
    if (originalEnvironment) lightLoopOutput.diffuseLighting += environmentData.combined;
    #endif
}
