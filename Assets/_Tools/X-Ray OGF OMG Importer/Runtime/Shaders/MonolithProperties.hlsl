#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
#include "MonolithLightInputs.hlsl"
TEXTURE2D(_EmissiveColorMap);
SAMPLER(sampler_EmissiveColorMap);
#ifndef LAYERED_LIT_SHADER
TEXTURE2D(_DiffuseLightingMap);
SAMPLER(sampler_DiffuseLightingMap);
TEXTURE2D(_BaseColorMap);
SAMPLER(sampler_BaseColorMap);
TEXTURE2D(_MaskMap);
SAMPLER(sampler_MaskMap);
TEXTURE2D(_BentNormalMap);
SAMPLER(sampler_BentNormalMap);
TEXTURE2D(_BentNormalMapOS);
SAMPLER(sampler_BentNormalMapOS);
TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);
TEXTURE2D(_NormalMapOS);
SAMPLER(sampler_NormalMapOS);
TEXTURE2D(_DetailMap);
SAMPLER(sampler_DetailMap);
TEXTURE2D(_HeightMap);
SAMPLER(sampler_HeightMap);
TEXTURE2D(_TangentMap);
SAMPLER(sampler_TangentMap);
TEXTURE2D(_TangentMapOS);
SAMPLER(sampler_TangentMapOS);
TEXTURE2D(_AnisotropyMap);
SAMPLER(sampler_AnisotropyMap);
TEXTURE2D(_SubsurfaceMaskMap);
SAMPLER(sampler_SubsurfaceMaskMap);
TEXTURE2D(_TransmissionMaskMap);
SAMPLER(sampler_TransmissionMaskMap);
TEXTURE2D(_ThicknessMap);
SAMPLER(sampler_ThicknessMap);
TEXTURE2D(_IridescenceThicknessMap);
SAMPLER(sampler_IridescenceThicknessMap);
TEXTURE2D(_IridescenceMaskMap);
SAMPLER(sampler_IridescenceMaskMap);
TEXTURE2D(_SpecularColorMap);
SAMPLER(sampler_SpecularColorMap);
TEXTURE2D(_TransmittanceColorMap);
SAMPLER(sampler_TransmittanceColorMap);
TEXTURE2D(_CoatMaskMap);
SAMPLER(sampler_CoatMaskMap);
#else
#define PROP_DECL(type, name) type name##0, name##1, name##2, name##3
#define PROP_DECL_TEX2D(name)\
    TEXTURE2D(CALL_MERGE_NAME(name, 0)); \
    SAMPLER(CALL_MERGE_NAME(CALL_MERGE_NAME(sampler, name), 0)); \
    TEXTURE2D(CALL_MERGE_NAME(name, 1)); \
    SAMPLER(CALL_MERGE_NAME(CALL_MERGE_NAME(sampler, name), 1)); \
    TEXTURE2D(CALL_MERGE_NAME(name, 2)); \
    SAMPLER(CALL_MERGE_NAME(CALL_MERGE_NAME(sampler, name), 2)); \
    TEXTURE2D(CALL_MERGE_NAME(name, 3)); \
    SAMPLER(CALL_MERGE_NAME(CALL_MERGE_NAME(sampler, name), 3))
PROP_DECL_TEX2D(_BaseColorMap);
PROP_DECL_TEX2D(_MaskMap);
PROP_DECL_TEX2D(_BentNormalMap);
PROP_DECL_TEX2D(_NormalMap);
PROP_DECL_TEX2D(_NormalMapOS);
PROP_DECL_TEX2D(_DetailMap);
PROP_DECL_TEX2D(_HeightMap);
PROP_DECL_TEX2D(_SubsurfaceMaskMap);
PROP_DECL_TEX2D(_TransmissionMaskMap);
PROP_DECL_TEX2D(_ThicknessMap);
TEXTURE2D(_LayerMaskMap);
SAMPLER(sampler_LayerMaskMap);
TEXTURE2D(_LayerInfluenceMaskMap);
SAMPLER(sampler_LayerInfluenceMaskMap);
#endif
TEXTURE2D(_MonolithBump);
SAMPLER(sampler_MonolithBump);
TEXTURE2D(_MonolithBumpCorrection);
TEXTURE2D(_MonolithDetailMap);
SAMPLER(sampler_MonolithDetailMap);
TEXTURE2D(_MonolithDetailBump);
TEXTURE2D(_MonolithDetailBumpCorrection);
TEXTURE2D(_MonolithLevelHemiMap);
SAMPLER(sampler_MonolithLevelHemiMap);
TEXTURE2D(_MonolithTerrainMask);
SAMPLER(sampler_MonolithTerrainMask);
TEXTURE2D(_MonolithTerrainDetailR);
SAMPLER(sampler_MonolithTerrainDetailR);
TEXTURE2D(_MonolithTerrainDetailG);
TEXTURE2D(_MonolithTerrainDetailB);
TEXTURE2D(_MonolithTerrainDetailA);
TEXTURE2D(_MonolithTerrainBumpR);
SAMPLER(sampler_MonolithTerrainBumpR);
TEXTURE2D(_MonolithTerrainBumpG);
TEXTURE2D(_MonolithTerrainBumpB);
TEXTURE2D(_MonolithTerrainBumpA);
TEXTURE2D(_MonolithTerrainHeightR);
TEXTURE2D(_MonolithTerrainHeightG);
TEXTURE2D(_MonolithTerrainHeightB);
TEXTURE2D(_MonolithTerrainHeightA);
TEXTURE2D(_MonolithTerrainSourceBase);
TEXTURE2D(_MonolithTerrainLodAtlas);
TEXTURE2D(_MonolithTerrainSourceColorR);
TEXTURE2D(_MonolithTerrainSourceColorG);
TEXTURE2D(_MonolithTerrainSourceColorB);
TEXTURE2D(_MonolithTerrainSourceColorA);
static float3 _MonolithDiagnosticValue;
static float _MonolithLevelHemiValue = 1.0;
static float4 _MonolithSourceAlbedoGloss;
static float _MonolithSourceRawMaterial;
static float _MonolithSourceLocalLight = 0.0;
static float3 _MonolithCurrentSourceLight = 0.0;
static float _MonolithCurrentSourceLightActive = 0.0;
float _MonolithWeatherActive;
float4 _MonolithWeatherState;
float4 _MonolithWeatherSun;
float4 _MonolithWeatherLightSetup;
float4 _MonolithWeatherFloraFixes;
float _MonolithSourceLightingMode;
float _MonolithBoundRadianceScale;
float4 _MonolithLightCalibration;
int _MonolithBoundLightCount;
float4 _MonolithBoundPositionType64[64];
float4 _MonolithBoundDirectionRange64[64];
float4 _MonolithBoundColor64[64];
float4 _MonolithDetailWave1;
float4 _MonolithDetailWind1;
float4 _MonolithDetailWave2;
float4 _MonolithDetailWind2;

CBUFFER_START(UnityPerMaterial)
float _MonolithHasBump;
float _MonolithBumpFlipV;
float _MonolithMaterialId;
float _MonolithMaterialWeight;
float _MonolithFallbackGloss;
float _MonolithGlossScale;
float _MonolithDynamicGloss;
float _MonolithFlipGreen;
float _MonolithOriginalTbn;
float _MonolithSkinningActive;
float _MonolithBoneCount;
float _MonolithHudNormal;
float _MonolithEnvironmentMode;
float4 _MonolithEnvColor;
float4 _MonolithEnvAmbient;
float _MonolithEnvHemisphere;
float _MonolithEnvHudMask;
float _MonolithEnvHudHemi;
float _MonolithEnvCombineClamp;
float4 _MonolithEnvTestNormal;
float4 _MonolithEnvTestView;
float _MonolithAlbedoSrgb;
float _MonolithDebug;
float4 _MonolithDiagnosticOptions;
float4 _MonolithLightSetup;
float4 _MonolithSourceLight;
float _MonolithSourceProfile;
float _MonolithTrueStalkerTerrain;
float4 _MonolithSunColor;
float4 _MonolithFloraFixes2;
float _MonolithFloraSubsurface;
float _MonolithSourceDetail;
float _MonolithSourceParallax;
float _MonolithSourceVirtualHeight;
float _MonolithSourceTextureRange;
float _MonolithHasBumpCorrection;
float _MonolithHasDetail;
float _MonolithHasDetailBump;
float _MonolithDetailScale;
float _MonolithDetailSrgb;
float _MonolithDetailFlipV;
float _MonolithParallaxHeight;
float4 _MonolithPom;
float _MonolithLevel;
float _MonolithLevelDetail;
float _MonolithLevelHemiMode;
float _MonolithLevelHemiFlipV;
float _MonolithLevelAmbientHemi;
float _MonolithLevelWind;
float _MonolithLevelTerrain;
float _MonolithTerrainDetailScale;
float _MonolithTerrainBumpScale;
float _MonolithSourceTerrain;
float _MonolithTerrainQuality;
float _MonolithTerrainMidAvailable;
float _MonolithTerrainLowAvailable;
float _MonolithTerrainLodFlipV;
float _MonolithTerrainSource;
float _MonolithTerrainParallax;
float _MonolithTerrainOcclusion;
float _MonolithTerrainVirtualHeight;
float _MonolithHasTerrainHeight;
float _MonolithHasTerrainSourceColor;
float4 _MonolithTerrainPom;
float4 _MonolithTerrainOffset;
float4 _MonolithTerrainColorFlipV;
float4 _MonolithTerrainBumpFlipV;
float4 _MonolithTerrainHeightFlipV;
float4 _MonolithTerrainColorSrgb;
float _MonolithTerrainMaskFlipV;
float _MonolithTerrainBaseFlipV;
float _AlphaCutoff;
float _UseShadowThreshold;
float _AlphaCutoffShadow;
float _AlphaCutoffPrepass;
float _AlphaCutoffPostpass;
float4 _DoubleSidedConstants;
float _BlendMode;
float _EnableBlendModePreserveSpecularLighting;
float _PPDMaxSamples;
float _PPDMinSamples;
float _PPDLodThreshold;
float3 _EmissiveColor;
float _AlbedoAffectEmissive;
float _EmissiveExposureWeight;
int  _SpecularOcclusionMode;
float3 _TransmittanceColor;
float _Ior;
float _ATDistance;
float3 _EmissionColor;
float4 _EmissiveColorMap_ST;
float _TexWorldScaleEmissive;
float4 _UVMappingMaskEmissive;
float _ObjectSpaceUVMappingEmissive;
float4 _InvPrimScale;
float _EnableGeometricSpecularAA;
float _SpecularAAScreenSpaceVariance;
float _SpecularAAThreshold;
float _RayTracing;
#ifndef LAYERED_LIT_SHADER
float4 _BaseColor;
float4 _BaseColorMap_ST;
float4 _BaseColorMap_TexelSize;
float _Metallic;
float _MetallicRemapMin;
float _MetallicRemapMax;
float _Smoothness;
float _SmoothnessRemapMin;
float _SmoothnessRemapMax;
float _AlphaRemapMin;
float _AlphaRemapMax;
float _AORemapMin;
float _AORemapMax;
float _NormalScale;
float4 _DetailMap_ST;
float _DetailAlbedoScale;
float _DetailNormalScale;
float _DetailSmoothnessScale;
float4 _HeightMap_TexelSize;
float _HeightAmplitude;
float _HeightCenter;
float _Anisotropy;
float _DiffusionProfileHash;
float _SubsurfaceMask;
float _TransmissionMask;
float _Thickness;
float4 _ThicknessRemap;
float _IridescenceThickness;
float4 _IridescenceThicknessRemap;
float _IridescenceMask;
float _CoatMask;
float4 _SpecularColor;
float _EnergyConservingSpecularColor;
float _TexWorldScale;
float _InvTilingScale;
float4 _UVMappingMask;
float4 _UVDetailsMappingMask;
float _LinkDetailsWithBase;
float _ObjectSpaceUVMapping;
#else
PROP_DECL(float4, _BaseColor);
float4 _BaseColorMap0_ST;
float4 _BaseColorMap1_ST;
float4 _BaseColorMap2_ST;
float4 _BaseColorMap3_ST;
float4 _BaseColorMap0_TexelSize;
PROP_DECL(float, _Metallic);
PROP_DECL(float, _MetallicRemapMin);
PROP_DECL(float, _MetallicRemapMax);
PROP_DECL(float, _Smoothness);
PROP_DECL(float, _SmoothnessRemapMin);
PROP_DECL(float, _SmoothnessRemapMax);
PROP_DECL(float, _AlphaRemapMin);
PROP_DECL(float, _AlphaRemapMax);
PROP_DECL(float, _AORemapMin);
PROP_DECL(float, _AORemapMax);
PROP_DECL(float, _NormalScale);
float4 _NormalMap0_TexelSize;
float4 _HeightMap0_TexelSize;
float4 _HeightMap1_TexelSize;
float4 _HeightMap2_TexelSize;
float4 _HeightMap3_TexelSize;
float4 _DetailMap0_ST;
float4 _DetailMap1_ST;
float4 _DetailMap2_ST;
float4 _DetailMap3_ST;
PROP_DECL(float, _UVDetail);
PROP_DECL(float, _DetailAlbedoScale);
PROP_DECL(float, _DetailNormalScale);
PROP_DECL(float, _DetailSmoothnessScale);
PROP_DECL(float, _HeightAmplitude);
PROP_DECL(float, _HeightCenter);
PROP_DECL(float, _DiffusionProfileHash);
PROP_DECL(float, _SubsurfaceMask);
PROP_DECL(float, _TransmissionMask);
PROP_DECL(float, _Thickness);
PROP_DECL(float4, _ThicknessRemap);
PROP_DECL(float, _OpacityAsDensity);
float _InheritBaseNormal1;
float _InheritBaseNormal2;
float _InheritBaseNormal3;
float _InheritBaseHeight1;
float _InheritBaseHeight2;
float _InheritBaseHeight3;
float _InheritBaseColor1;
float _InheritBaseColor2;
float _InheritBaseColor3;
PROP_DECL(float, _HeightOffset);
float _HeightTransition;
float4 _LayerMaskMap_ST;
float _TexWorldScaleBlendMask;
PROP_DECL(float, _TexWorldScale);
PROP_DECL(float, _InvTilingScale);
float4 _UVMappingMaskBlendMask;
PROP_DECL(float4, _UVMappingMask);
PROP_DECL(float4, _UVDetailsMappingMask);
PROP_DECL(float, _LinkDetailsWithBase);
#endif
#ifdef TESSELLATION_ON
float _TessellationFactor;
float _TessellationFactorMinDistance;
float _TessellationFactorMaxDistance;
float _TessellationFactorTriangleSize;
float _TessellationShapeFactor;
float _TessellationBackFaceCullEpsilon;
#endif
UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

float MonolithDynamicGlossInput()
{
    return MonolithResolveDynamicGloss(_MonolithSourceProfile, _MonolithWeatherActive, _MonolithDynamicGloss, _MonolithWeatherState.x);
}

float3 MonolithSunColorInput()
{
    return _MonolithSourceProfile > 0.5 && _MonolithWeatherActive > 0.5 ? _MonolithWeatherSun.rgb : _MonolithSunColor.rgb;
}

float4 MonolithLightSetupInput()
{
    return _MonolithSourceProfile > 0.5 && _MonolithWeatherActive > 0.5 ? _MonolithWeatherLightSetup : _MonolithLightSetup;
}

float4 MonolithFloraFixesInput()
{
    return MonolithResolveFloraFixes(_MonolithFloraFixes2, _MonolithWeatherFloraFixes, _MonolithSourceProfile, _MonolithWeatherActive, _MonolithFloraSubsurface);
}
#if defined(UNITY_INSTANCING_ENABLED)
UNITY_INSTANCING_BUFFER_START(MonolithInstance)
UNITY_DEFINE_INSTANCED_PROP(float4, _MonolithInstanceData)
UNITY_INSTANCING_BUFFER_END(MonolithInstance)
#endif
float4 MonolithInstanceData()
{
    #if defined(UNITY_INSTANCING_ENABLED)
    return UNITY_ACCESS_INSTANCED_PROP(MonolithInstance, _MonolithInstanceData);
    #else
    return float4(1.0, 0.0, 0.0, 0.0);
    #endif
}
float MonolithBaseHemisphere()
{
    #if defined(_MONOLITH_LEVEL) || defined(_MONOLITH_LEVEL_DETAIL) || defined(_MONOLITH_LEVEL_LOD)
    return _MonolithLevelHemiValue;
    #else
    return _MonolithEnvHemisphere;
    #endif
}
int _ObjectId;
int _PassValue;
float4 _SelectionID;
