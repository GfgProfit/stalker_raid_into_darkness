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
    if (_MonolithTrueStalkerActive > 0.5)
    {
        if (_MonolithTrueStalkerHasLookup > 0.5) data.energyCompensation = 0.0;
        return data;
    }
    float nv = max(1e-5, dot(bsdfData.normalWS, V));
    data.specularFGD = MonolithEnvGGX(bsdfData.fresnel0, bsdfData.perceptualRoughness, nv) * Sq(saturate(dot(bsdfData.normalWS, V) * 2.0));
    data.diffuseFGD = MonolithEnvBurley(bsdfData.perceptualRoughness, nv);
    data.energyCompensation = 0.0;
    if (_MonolithSourceProfile > 0.5)
    {
        data.specularFGD *= MonolithSourceAmbientSpecularScale(_MonolithSourceRawMaterial);
    }
    return data;
}

CBSDF MonolithEvaluateBSDF(float3 V, float3 L, PreLightData preLightData, BSDFData bsdfData)
{
    CBSDF data;
    ZERO_INITIALIZE(CBSDF, data);
    if (_MonolithTrueStalkerActive > 0.5)
    {
        if (_MonolithTrueStalkerHasLookup < 0.5) return EvaluateBSDF(V, L, preLightData, bsdfData);
        float4 lookup = MonolithTrueStalkerDirect(_MonolithTrueStalkerMaterialLut, s_trilinear_clamp_sampler, _MonolithSourceRawMaterial, bsdfData.normalWS, V, L, _MonolithSourceLocalLight > 0.5, 1.0);
        data.diffR = lookup.rgb;
        data.specR = (lookup.w * _MonolithSourceAlbedoGloss.a).xxx;
        return data;
    }
    if (_MonolithSourceProfile > 0.5)
    {
        float3 sourceColor = _MonolithCurrentSourceLightActive > 0.5 ? _MonolithCurrentSourceLight : _MonolithSourceLight.rgb;
        MonolithSourceDirect(bsdfData.normalWS, V, L, _MonolithSourceAlbedoGloss.rgb, _MonolithSourceAlbedoGloss.a, _MonolithSourceRawMaterial, MonolithDynamicGlossInput(), sourceColor, MonolithLightSetupInput(), MonolithSunColorInput(), MonolithFloraFixesInput(), _MonolithSourceLocalLight, data.diffR, data.specR);
        if (_MonolithDebug > 87.5 && _MonolithDebug < 88.5)
        {
            data.diffR = MonolithSourceFlora(MonolithSourceMaterial(_MonolithSourceRawMaterial), 0.04) ? MonolithFloraSubsurface(normalize(bsdfData.normalWS), V, L, MonolithSunColorInput(), MonolithFloraFixesInput()) : 0.0;
            data.specR = 0.0;
        }
        return data;
    }
    float3 N = bsdfData.normalWS;
    if (_MonolithFloraSourceActive > 1.5 && _MonolithSourceLocalLight > 1.5) N = L;
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

bool MonolithFindBoundLight(float type, float3 positionRWS, float3 forward, float range, out float3 sourceColor, out float localMode)
{
    sourceColor = 0.0;
    localMode = 1.0;
    if ((_MonolithSourceProfile < 0.5 && !(_MonolithTrueStalkerActive > 0.5 && _MonolithTrueStalkerHasLookup > 0.5)) || _MonolithSourceLightingMode < 1.5) return false;
    float3 position = GetAbsolutePositionWS(positionRWS);
    int matches = 0;
    for (int i = 0; i < min(_MonolithBoundLightCount, 64); i++)
    {
        float4 p = _MonolithBoundPositionType64[i];
        float4 d = _MonolithBoundDirectionRange64[i];
        bool samePosition = type < 0.5 || dot(position - p.xyz, position - p.xyz) < 0.000001;
        bool sameDirection = type > 0.5 && type < 1.5 || dot(forward - d.xyz, forward - d.xyz) < 0.000001;
        if (abs(type - p.w) < 0.1 && samePosition && sameDirection && (type < 0.5 || abs(range - d.w) < 0.0001))
        {
            sourceColor = _MonolithBoundColor64[i].rgb;
            localMode = _MonolithBoundColor64[i].w;
            matches++;
        }
    }
    return matches == 1;
}

DirectLighting MonolithBoundLighting(float3 V, float3 L, PreLightData preLightData, BSDFData bsdfData, float3 sourceColor, float3 lightmap, float3 shadow, float attenuation, float diffuseDimmer, float specularDimmer)
{
    _MonolithCurrentSourceLight = sourceColor;
    _MonolithCurrentSourceLightActive = 1.0;
    CBSDF data = MonolithEvaluateBSDF(V, L, preLightData, bsdfData);
    float3 color = (_MonolithTrueStalkerActive > 0.5 ? sourceColor * lightmap * shadow : MonolithSourceAccumulation(sourceColor, lightmap, shadow)) * attenuation * _MonolithBoundRadianceScale;
    DirectLighting lighting;
    ZERO_INITIALIZE(DirectLighting, lighting);
    lighting.diffuse = data.diffR * color * diffuseDimmer;
    lighting.specular = data.specR * color * specularDimmer;
    _MonolithCurrentSourceLightActive = 0.0;
    return lighting;
}

DirectLighting EvaluateBSDF_Directional(LightLoopContext lightLoopContext, float3 V, PositionInputs posInput, PreLightData preLightData, DirectionalLightData lightData, BSDFData bsdfData, BuiltinData builtinData)
{
    float3 sourceColor = 0.0;
    float localMode = 1.0;
    if (MonolithFindBoundLight(0.0, lightData.positionRWS, lightData.forward, 0.0, sourceColor, localMode))
    {
        float3 shadow = EvaluateShadow_Directional(lightLoopContext, posInput, lightData, builtinData, GetNormalForShadowBias(bsdfData));
        if (_MonolithDebug > 62.5 && _MonolithDebug < 63.5) _MonolithDiagnosticValue = shadow;
        float3 cookie = 1.0;
        if (lightData.cookieMode != COOKIEMODE_NONE) cookie = EvaluateCookie_Directional(lightLoopContext, lightData, posInput.positionWS - lightData.positionRWS);
        return MonolithBoundLighting(V, -lightData.forward, preLightData, bsdfData, sourceColor, cookie, shadow, 1.0, lightData.diffuseDimmer, lightData.specularDimmer);
    }
    if (_MonolithSourceProfile > 0.5 && _MonolithSourceLightingMode > 0.5 && _MonolithSourceLightingMode < 1.5)
    {
        _MonolithCurrentSourceLight = MonolithCalibratedSourceColor(lightData.color, _MonolithLightCalibration.x);
        _MonolithCurrentSourceLightActive = 1.0;
    }
    if (_MonolithDebug > 62.5 && _MonolithDebug < 63.5)
    {
        _MonolithDiagnosticValue = EvaluateShadow_Directional(lightLoopContext, posInput, lightData, builtinData, GetNormalForShadowBias(bsdfData));
    }

    DirectLighting lighting = MonolithShadeSurface_Directional(lightLoopContext, posInput, builtinData, preLightData, lightData, bsdfData, V);
    _MonolithCurrentSourceLightActive = 0.0;
    return lighting;
}

DirectLighting EvaluateBSDF_Punctual(LightLoopContext lightLoopContext, float3 V, PositionInputs posInput, PreLightData preLightData, LightData lightData, BSDFData bsdfData, BuiltinData builtinData)
{
    _MonolithSourceLocalLight = lightData.lightType == GPULIGHTTYPE_POINT && lightData.shadowIndex < 0 ? 2.0 : 1.0;
    float3 terrainDiffuseScale = 1.0;
    if (_MonolithSourceProfile < 0.5 && _MonolithTrueStalkerTerrain > 0.5 && _MonolithTrueStalkerActive < 0.5)
    {
        float material = _MonolithSourceRawMaterial;
        float gloss = _MonolithSourceAlbedoGloss.a;
        float3 originalDiffuse = MonolithAlbedo(_MonolithSourceAlbedoGloss.rgb, MonolithMetalness(gloss, material));
        terrainDiffuseScale = originalDiffuse / max(bsdfData.diffuseColor, 1e-20);
        bsdfData.diffuseColor = originalDiffuse;
        bsdfData.fresnel0 = MonolithSpecular(_MonolithSourceAlbedoGloss.rgb, gloss, material);
        bsdfData.perceptualRoughness = MonolithRoughness(gloss, material, _MonolithDynamicGloss);
    }
    float3 sourceColor = 0.0;
    float localMode = 1.0;
    bool bound = lightData.lightType <= GPULIGHTTYPE_SPOT && MonolithFindBoundLight(lightData.lightType, lightData.positionRWS, lightData.forward, lightData.range, sourceColor, localMode);
    if (_MonolithDebug > 68.5 && _MonolithDebug < 69.5) _MonolithDiagnosticValue += float3(1.0, bound ? 1.0 : 0.0, lightData.shadowIndex >= 0 ? 1.0 : 0.0) * 0.125;
    if (bound)
    {
        _MonolithSourceLocalLight = localMode;
        float3 L = 0.0;
        float4 distances = 0.0;
        GetPunctualLightVectors(posInput.positionWS, lightData, L, distances);
        float3 delta = posInput.positionWS - lightData.positionRWS;
        float attenuation = MonolithSourceAttenuation(dot(delta, delta), lightData.range);
        float3 shadow = EvaluateShadow_Punctual(lightLoopContext, posInput, lightData, builtinData, GetNormalForShadowBias(bsdfData), L, distances);
        float4 cookie = 1.0;
        if (lightData.cookieMode != COOKIEMODE_NONE) cookie = EvaluateCookie_Punctual(lightLoopContext, lightData, delta);
        if (lightData.lightType == GPULIGHTTYPE_SPOT) cookie.rgb *= AngleAttenuation(dot(-L, lightData.forward), lightData.angleScale, lightData.angleOffset);
        DirectLighting lighting = MonolithBoundLighting(V, L, preLightData, bsdfData, sourceColor, cookie.rgb * cookie.a, shadow, attenuation, lightData.diffuseDimmer, lightData.specularDimmer);
        _MonolithSourceLocalLight = 0.0;
        return lighting;
    }
    if (_MonolithSourceProfile > 0.5 && _MonolithSourceLightingMode > 0.5 && _MonolithSourceLightingMode < 1.5)
    {
        _MonolithCurrentSourceLight = MonolithCalibratedSourceColor(lightData.color, _MonolithLightCalibration.y);
        _MonolithCurrentSourceLightActive = 1.0;
    }
    DirectLighting lighting = MonolithShadeSurface_Punctual(lightLoopContext, posInput, builtinData, preLightData, lightData, bsdfData, V);
    lighting.diffuse *= terrainDiffuseScale;
    _MonolithCurrentSourceLightActive = 0.0;
    _MonolithSourceLocalLight = 0.0;
    return lighting;
}

#if defined(_MONOLITH_ORIGINAL_ENVIRONMENT) || defined(_MONOLITH_LEVEL) || defined(_MONOLITH_LEVEL_DETAIL) || defined(_MONOLITH_LEVEL_LOD)
#ifndef _MONOLITH_ORIGINAL_ENVIRONMENT
TEXTURECUBE(_MonolithWeatherEnvironment0);
TEXTURECUBE(_MonolithWeatherEnvironment1);
TEXTURECUBE(_MonolithWeatherEnvironment2);
TEXTURECUBE(_MonolithWeatherEnvironment3);
#endif

float3 MonolithWeatherFxIrradiance(float3 normal)
{
    float3 magnitude = abs(normal);
    float3 direction = normal / max(1e-5, max(magnitude.x, max(magnitude.y, magnitude.z)));
    direction.y = direction.y * 2.0 - 1.0;
    float3 cube = 0.0;
    [branch] if (_MonolithWeatherEnvironmentWeights.x > 0.0) cube += SAMPLE_TEXTURECUBE_LOD(_MonolithWeatherEnvironment0, s_trilinear_clamp_sampler, direction, 6.0).rgb * _MonolithWeatherEnvironmentWeights.x;
    [branch] if (_MonolithWeatherEnvironmentWeights.y > 0.0) cube += SAMPLE_TEXTURECUBE_LOD(_MonolithWeatherEnvironment1, s_trilinear_clamp_sampler, direction, 6.0).rgb * _MonolithWeatherEnvironmentWeights.y;
    [branch] if (_MonolithWeatherEnvironmentWeights.z > 0.0) cube += SAMPLE_TEXTURECUBE_LOD(_MonolithWeatherEnvironment2, s_trilinear_clamp_sampler, direction, 6.0).rgb * _MonolithWeatherEnvironmentWeights.z;
    [branch] if (_MonolithWeatherEnvironmentWeights.w > 0.0) cube += SAMPLE_TEXTURECUBE_LOD(_MonolithWeatherEnvironment3, s_trilinear_clamp_sampler, direction, 6.0).rgb * _MonolithWeatherEnvironmentWeights.w;
    return pow(max(0.0, cube * _MonolithWeatherEnvironmentColor.rgb * MonolithBaseHemisphere() + _MonolithWeatherAmbient.rgb), 2.2);
}

float3 MonolithWeatherFxAdaptation(float3 irradiance)
{
    float peak = max(irradiance.r, max(irradiance.g, irradiance.b));
    float gain = min(_MonolithWeatherFxAmbient.z / (1.0 + peak / 0.025), _MonolithWeatherFxAmbient.w / max(peak, 1e-5));
    return irradiance * gain;
}
#endif

void PostEvaluateBSDF(LightLoopContext lightLoopContext, float3 V, PositionInputs posInput, PreLightData preLightData, BSDFData bsdfData, BuiltinData builtinData, AggregateLighting lighting, out LightLoopOutput lightLoopOutput)
{
    #ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
    MonolithEnvironmentData environmentData;
    ZERO_INITIALIZE(MonolithEnvironmentData, environmentData);
    bool originalEnvironment = _MonolithEnvironmentMode > 0.5 && (_MonolithTrueStalkerActive < 0.5 || _MonolithTrueStalkerHasLookup > 0.5);
    if (originalEnvironment)
    {
        environmentData = MonolithEvaluateEnvironment(bsdfData.normalWS, V, _MonolithEnvironmentAlbedoGloss);
        builtinData.bakeDiffuseLighting = 0.0;
        builtinData.backBakeDiffuseLighting = 0.0;
        lighting.indirect.specularReflected = 0.0;
        lighting.indirect.specularTransmitted = 0.0;
    }
    #endif
    #if defined(_MONOLITH_LEVEL) || defined(_MONOLITH_LEVEL_DETAIL) || defined(_MONOLITH_LEVEL_LOD)
    bool levelAmbientHemi = _MonolithLevelAmbientHemi > 0.5;
    #ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
    levelAmbientHemi = levelAmbientHemi && !originalEnvironment;
    #endif
    if (levelAmbientHemi)
    {
        if (_MonolithSourceProfile < 0.5 || _IndirectDiffuseMode == INDIRECTDIFFUSEMODE_OFF) builtinData.bakeDiffuseLighting *= _MonolithLevelHemiValue;
        lighting.indirect.specularReflected *= _MonolithLevelHemiValue;
    }
    #endif
    if (_MonolithDiagnosticOptions.z > 0.5)
    {
        builtinData.bakeDiffuseLighting = 0.0;
        lighting.indirect.specularReflected = 0.0;
    }
    if (_MonolithDiagnosticOptions.w > 0.5)
    {
        lighting.direct.diffuse = 0.0;
        lighting.direct.specular = 0.0;
    }
    MonolithLegacyPostEvaluateBSDF(lightLoopContext, V, posInput, preLightData, bsdfData, builtinData, lighting, lightLoopOutput);
    #ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
    if (originalEnvironment && _MonolithDiagnosticOptions.z < 0.5) lightLoopOutput.diffuseLighting += environmentData.combined;
    #endif
    #if defined(_MONOLITH_ORIGINAL_ENVIRONMENT) || defined(_MONOLITH_LEVEL) || defined(_MONOLITH_LEVEL_DETAIL) || defined(_MONOLITH_LEVEL_LOD)
    [branch] if (_MonolithWeatherFxAmbient.x > 0.0 && _MonolithWeatherActive > 0.5 && _MonolithWeatherEnvironmentActive > 0.5 && _MonolithTrueStalkerActive < 0.5 && _MonolithDebug < 0.5 && _MonolithDiagnosticOptions.z < 0.5)
    {
        #ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
        bool alreadyUsesWeatherEnvironment = originalEnvironment && _MonolithSourceProfile > 0.5;
        float3 irradiance = alreadyUsesWeatherEnvironment ? environmentData.diffuseIrradiance : MonolithWeatherFxIrradiance(normalize(bsdfData.normalWS));
        float3 diffuse = alreadyUsesWeatherEnvironment ? 0.0 : irradiance;
        #else
        float3 irradiance = MonolithWeatherFxIrradiance(normalize(bsdfData.normalWS));
        float3 diffuse = irradiance;
        #endif
        diffuse += MonolithWeatherFxAdaptation(irradiance);
        lightLoopOutput.diffuseLighting += bsdfData.diffuseColor * diffuse * bsdfData.ambientOcclusion * (_MonolithWeatherFxAmbient.x * _MonolithWeatherFxAmbient.y);
    }
    #endif
    if (_MonolithWeatherActive > 0.5 && _MonolithTrueStalkerActive < 0.5 && _MonolithDebug < 0.5 && _MonolithDiagnosticOptions.z < 0.5)
    {
        lightLoopOutput.diffuseLighting += bsdfData.diffuseColor * _MonolithWeatherNightAmbient.rgb * bsdfData.ambientOcclusion;
    }
    if (_MonolithDebug > 0.5 && _MonolithDebug < 19.5)
    {
        lightLoopOutput.diffuseLighting = _MonolithDiagnosticValue * GetInverseCurrentExposureMultiplier();
        lightLoopOutput.specularLighting = 0.0;
    }
    if (_MonolithDebug >= 19.5)
    {
        float3 diagnostic = float3(bsdfData.perceptualRoughness, bsdfData.fresnel0.r, bsdfData.diffuseColor.r);
        if (_MonolithDebug > 20.5) diagnostic = bsdfData.fresnel0;
        if (_MonolithDebug > 21.5) diagnostic = bsdfData.normalWS * 0.5 + 0.5;
        if (_MonolithDebug > 22.5) diagnostic = float3(preLightData.diffuseFGD, preLightData.specularFGD.r, preLightData.energyCompensation);
        if (_MonolithDebug > 23.5) diagnostic = float3(0.125, 0.5, 0.875);
        if (_MonolithDebug > 29.5) diagnostic = bsdfData.diffuseColor * lighting.direct.diffuse;
        if (_MonolithDebug > 30.5) diagnostic = lighting.direct.specular;
        if (_MonolithDebug > 31.5) diagnostic = builtinData.bakeDiffuseLighting;
        if (_MonolithDebug > 32.5) diagnostic = lighting.indirect.specularReflected;
        if ((_MonolithDebug > 33.5 && _MonolithDebug < 35.5) || (_MonolithDebug > 36.5 && _MonolithDebug < 38.5)) diagnostic = _MonolithDiagnosticValue;
        #ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
        if (originalEnvironment && _MonolithDebug > 31.5 && _MonolithDebug < 33.5) diagnostic = _MonolithDebug > 32.5 ? environmentData.specular : environmentData.diffuse;
        if (originalEnvironment && _MonolithDebug >= 39.5) diagnostic = MonolithEnvironmentDiagnostic(environmentData, (int)round(_MonolithDebug));
        if (originalEnvironment && _MonolithDebug > 60.5 && _MonolithDebug < 61.5) diagnostic = GetCurrentExposureMultiplier().xxx;
        if (originalEnvironment && _MonolithDebug > 61.5 && _MonolithDebug < 62.5) diagnostic = lightLoopOutput.specularLighting;
        #endif
        if (_MonolithDebug > 62.5 && _MonolithDebug < 63.5) diagnostic = _MonolithDiagnosticValue;
        if (_MonolithDebug > 64.5 && _MonolithDebug < 65.5) diagnostic = builtinData.bakeDiffuseLighting * GetCurrentExposureMultiplier();
        if (_MonolithDebug > 65.5 && _MonolithDebug < 66.5) diagnostic = LOAD_TEXTURE2D_X(_IndirectDiffuseTexture, posInput.positionSS).xyz;
        if (_MonolithDebug > 66.5 && _MonolithDebug < 67.5) diagnostic = (LinearEyeDepth(LoadCameraDepth(posInput.positionSS), _ZBufferParams) * 0.05).xxx;
        if (_MonolithDebug > 67.5 && _MonolithDebug < 68.5)
        {
            NormalData normalData;
            DecodeFromNormalBuffer(posInput.positionSS, normalData);
            diagnostic = normalData.normalWS * 0.5 + 0.5;
        }
        if (_MonolithDebug > 68.5 && _MonolithDebug < 69.5) diagnostic = _MonolithDiagnosticValue;
        if (_MonolithDebug > 69.5 && _MonolithDebug < 87.5) diagnostic = _MonolithDiagnosticValue;
        if (_MonolithDebug > 87.5 && _MonolithDebug < 88.5) diagnostic = bsdfData.diffuseColor * lighting.direct.diffuse;
        lightLoopOutput.diffuseLighting = diagnostic * GetInverseCurrentExposureMultiplier();
        lightLoopOutput.specularLighting = 0.0;
    }
    #if defined(_MONOLITH_LEVEL) || defined(_MONOLITH_LEVEL_DETAIL) || defined(_MONOLITH_LEVEL_LOD)
    if (_MonolithDebug > 35.5 && _MonolithDebug < 36.5)
    {
        lightLoopOutput.diffuseLighting = _MonolithLevelHemiValue.xxx * GetInverseCurrentExposureMultiplier();
        lightLoopOutput.specularLighting = 0.0;
    }
    #endif
}
