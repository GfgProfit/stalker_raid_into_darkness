Shader "HDRP/MonolithLit"
{
    Properties
    {
        _MonolithBump("X-Ray Packed Bump (Linear)", 2D) = "white" {}
        _MonolithBumpCorrection("X-Ray Bump Correction (Reserved for POM)", 2D) = "black" {}
        _MonolithHasBump("Use Packed Bump", Float) = 0
        _MonolithBumpFlipV("Packed DDS Requires V Flip", Float) = 0
        _MonolithMaterialId("THM Material ID (Fallback 1)", Float) = 1
        _MonolithMaterialWeight("THM Material Weight (Fallback 0)", Float) = 0
        _MonolithFallbackGloss("Fallback Gloss (No Bump)", Range(0,1)) = 0.1
        _MonolithGlossScale("Gloss Scale", Range(0,2)) = 1
        _MonolithDynamicGloss("Ldynamic Color W", Range(0,0.99)) = 0.71864
        _MonolithFlipGreen("Flip Tangent Y", Float) = 1
        [Toggle(_MONOLITH_ORIGINAL_TBN)] _MonolithOriginalTbn("Original X-Ray TBN (Static Experimental Stream)", Float) = 0
        [Toggle(_MONOLITH_SKINNED_TBN)] _MonolithSkinnedTbn("Original X-Ray TBN Skinning (1L Experiment)", Float) = 0
        [HideInInspector] _MonolithSkinningActive("Original TBN Buffer Active", Float) = 0
        [HideInInspector] _MonolithBoneCount("Original TBN Bone Count", Float) = 0
        [Toggle] _MonolithHudNormal("HUD Normal Correction (Dry)", Float) = 0
        [Toggle(_MONOLITH_ORIGINAL_ENVIRONMENT)] _MonolithEnvironmentMode("Original hmodel Environment (Experimental)", Float) = 0
        _MonolithEnv0("Original env_s0 (Encoded Values)", Cube) = "black" {}
        _MonolithEnv1("Original env_s1 (Encoded Values)", Cube) = "black" {}
        _MonolithEnvColor("Original env_color (RGB Tint, W Blend)", Vector) = (1,1,1,0)
        _MonolithEnvAmbient("Original L_ambient (Encoded RGB)", Vector) = (0,0,0,0)
        _MonolithEnvHemisphere("Original Decoded Hemisphere", Float) = 1
        _MonolithEnvHudMask("Original HUD Mask", Float) = 0
        _MonolithEnvHudHemi("Original ssfx_hud_hemi.x", Float) = 0
        [Toggle] _MonolithEnvCombineClamp("Original combine_1 Environment Clamp", Float) = 1
        [HideInInspector] _MonolithEnvTestNormal("Environment Diagnostic Normal", Vector) = (0,0,1,0)
        [HideInInspector] _MonolithEnvTestView("Environment Diagnostic View and Roughness", Vector) = (0,0,1,-1)
        _MonolithAlbedoSrgb("Albedo Uses Hardware sRGB Decode", Float) = 1
        _MonolithDebug("Debug: 0 Lit 1 Albedo 2 Normal 3 Gloss 4 Metal 5 Rough 6 F0 7 ID", Float) = 0
        _MonolithDiagnosticOptions("Diagnostics: Skip Quantization, Disable Normal, Disable Environment, Disable Direct", Vector) = (0,0,0,0)
        _MonolithLightSetup("ssfx_lightsetup_1", Vector) = (0.5,1,0,0)
        _MonolithSourceLight("Source Ldynamic RGB (Unit Adaptation)", Color) = (1,1,1,1)
        _MonolithSourceProfile("Source Profile: 0 Monolith 1 Anomaly SSS 23.5 + ES 1.10 lmodel", Float) = 0
        [Toggle] _MonolithTrueStalkerTerrain("True Stalker Terrain ID Correction (Directional and Environment)", Float) = 0
        _MonolithSunColor("Source L_sun_color (Flora SSS)", Vector) = (1,1,1,0)
        _MonolithFloraFixes2("ssfx_florafixes_2", Vector) = (1.5,1,0,0)
        _MonolithFloraSubsurface("Flora Subsurface Mask", Range(0,1)) = 1
        [Enum(Legacy, 0, Anomaly SSS 23.5, 1, True Stalker 1.5, 2)] _MonolithFloraSource("Flora Source Surface (Explicit, 0 Legacy)", Float) = 0
        [Enum(Disabled, 0, Verified Anomaly SSFX Wind, 1, Verified True Stalker X Ray Wind, 2)] _MonolithFloraWind("Flora Wind Animation (Explicit, 0 Disabled)", Float) = 0
        [Enum(Branch, 0, Trunk, 1)] _MonolithFloraWindPart("Flora Wind Vertex Shader (tree_branch or tree)", Float) = 0
        [NoScaleOffset] _MonolithWindWaves("SSFX s_waves (fx wind_wave, Linear)", 2D) = "grey" {}
        _MonolithWindWavesFlipV("Wind Waves Require V Flip", Float) = 0
        _MonolithFloraWetness("Flora rain_params.y (Explicit Wetness)", Range(0,1)) = 0
        _MonolithWetResponse("Precipitation Response (Darkening, Gloss, Puddles, Enabled)", Vector) = (0.8,0.75,0.35,0)
        _MonolithWetTerrainResponseR("Precipitation Terrain R Response", Vector) = (0,0,0,0)
        _MonolithWetTerrainResponseG("Precipitation Terrain G Response", Vector) = (0,0,0,0)
        _MonolithWetTerrainResponseB("Precipitation Terrain B Response", Vector) = (0,0,0,0)
        _MonolithWetTerrainResponseA("Precipitation Terrain A Response", Vector) = (0,0,0,0)
        _MonolithPuddleMask("Original Terrain Puddle Mask", 2D) = "white" {}
        [HideInInspector] _MonolithHasPuddleMask("Original Puddle Mask Bound", Float) = 0
        [HideInInspector] _MonolithPuddleMaskSlice("Original Puddle Mask Slice", Float) = -1
        [HideInInspector] _MonolithPuddleMaskFlipV("Original Puddle Mask Flip V", Float) = 0
        _MonolithFloraFixes1("ssfx_florafixes_1", Vector) = (0.3,0.38,0.5,0.55)
        [Toggle] _MonolithFloraVariationEnabled("Flora Color Variation (Explicit)", Float) = 0
        _MonolithFloraVariation("ssfx_floravariation", Vector) = (0.025,0.1,0.025,0.05)
        _MonolithSourceDetail("Source Detail (Profile 1): 0 Off 1 USE_TDETAIL 2 USE_TDETAIL_BUMP", Float) = 0
        _MonolithSourceParallax("Source Parallax (Profile 1): 0 Off 1 USE_PARALLAX 2 USE_STEEPPARALLAX", Float) = 0
        [Toggle] _MonolithSourceVirtualHeight("Source def_virtualh Lighting Position (Profile 1)", Float) = 0
        _MonolithSourceTextureRange("Source HQ Element Range (r_dtex_range, 0 Unlimited)", Float) = 50
        [HideInInspector] _MonolithSourceDetailRecommended("Source Detail Recommended By THM", Float) = 0
        [HideInInspector] _MonolithSourceParallaxRecommended("Source Parallax Recommended By THM And Blender", Float) = 0
        [HideInInspector] _MonolithHasBumpCorrection("Bump Correction Bound (bump#)", Float) = 0
        [HideInInspector] _MonolithHasDetail("Detail Texture Bound", Float) = 0
        [HideInInspector] _MonolithHasDetailBump("Detail Bump And Detail Bump# Bound", Float) = 0
        _MonolithDetailMap("X-Ray Detail Texture (THM detail_name)", 2D) = "grey" {}
        _MonolithDetailBump("X-Ray Detail Packed Bump (Linear)", 2D) = "grey" {}
        _MonolithDetailBumpCorrection("X-Ray Detail Bump Correction (Linear)", 2D) = "grey" {}
        _MonolithDetailScale("Detail Scale (dt_params)", Float) = 1
        _MonolithDetailSrgb("Detail Uses Hardware sRGB Decode", Float) = 0
        _MonolithDetailFlipV("Detail DDS Requires V Flip", Float) = 0
        _MonolithParallaxHeight("r2_parallax_h", Float) = 0.02
        _MonolithPom("ssfx_pom (Samples, Range, Height, AO)", Vector) = (36,12,0.035,0.5)
        [Toggle(_MONOLITH_LEVEL)] _MonolithLevel("X-Ray Level Static Stream", Float) = 0
        [Toggle(_MONOLITH_LEVEL_DETAIL)] _MonolithLevelDetail("X-Ray Level Detail Object", Float) = 0
        [Toggle(_MONOLITH_LEVEL_LOD)] _MonolithLevelLod("X-Ray Level MT_LOD Impostor", Float) = 0
        [Toggle(_MONOLITH_LEVEL_FLORA)] _MonolithLevelFlora("X-Ray Flora Normal (TS deffer_base_aref_bump-hq)", Float) = 0
        _MonolithLevelHemiMode("Level Hemisphere: 0 Constant 1 Lightmap 2 Vertex 3 Albedo Alpha", Float) = 0
        _MonolithLevelHemiMap("Level Hemisphere Lightmap (A)", 2D) = "white" {}
        _MonolithLevelHemiFlipV("Level Lightmap V Flip", Float) = 1
        [HideInInspector] _MonolithInstanceData("Level Instance Hemisphere Scale Bias", Vector) = (1,0,0,0)
        _MonolithLevelAmbientHemi("Scale HDRP Ambient By Level Hemisphere", Float) = 1
        _MonolithLevelWind("Detail Wind", Float) = 1
        [HideInInspector] _MonolithGrassSource("Source Detail Vegetation", Float) = 0
        [HideInInspector] _MonolithGrassRuntime("Runtime Detail Vegetation", Float) = 0
        [HideInInspector] _MonolithGrassAlign("Source Grass Alignment", Float) = 0
        [HideInInspector] _MonolithGrassHeight("Runtime Detail Size", Float) = 1
        [HideInInspector] _MonolithGrassHeightPrevious("Previous Detail Size", Float) = 1
        [HideInInspector] _MonolithWindGrassSetup("SSFX Grass Wind", Vector) = (0,0,0,0)
        [Toggle(_MONOLITH_LEVEL_TERRAIN)] _MonolithLevelTerrain("X-Ray Terrain 4-Detail Splat", Float) = 0
        _MonolithTerrainMask("Terrain Mask (Linear)", 2D) = "black" {}
        _MonolithTerrainDetailR("Terrain Detail R", 2D) = "grey" {}
        _MonolithTerrainDetailG("Terrain Detail G", 2D) = "grey" {}
        _MonolithTerrainDetailB("Terrain Detail B", 2D) = "grey" {}
        _MonolithTerrainDetailA("Terrain Detail A", 2D) = "grey" {}
        _MonolithTerrainBumpR("Terrain Detail Bump R (Linear)", 2D) = "grey" {}
        _MonolithTerrainBumpG("Terrain Detail Bump G (Linear)", 2D) = "grey" {}
        _MonolithTerrainBumpB("Terrain Detail Bump B (Linear)", 2D) = "grey" {}
        _MonolithTerrainBumpA("Terrain Detail Bump A (Linear)", 2D) = "grey" {}
        _MonolithTerrainDetailScale("Terrain Detail Scale (dt_params)", Float) = 1
        _MonolithTerrainBumpScale("Terrain Bump XY Intensity", Float) = 0.6
        [Toggle] _MonolithTrueStalkerSourceTerrain("True Stalker Dry Source Surface (HDRP Lighting)", Float) = 0
        [HideInInspector] _MonolithTrueStalkerVerified("Verified TS HLSL Contract", Float) = 0
        [HideInInspector] _MonolithTrueStalkerHasLookup("Explicit TS Material LUT Bound", Float) = 0
        _MonolithTrueStalkerMaterialLut("TS Material LUT (Explicit Source Input)", 3D) = "" {}
        [Toggle] _MonolithSourceTerrain("Source Terrain (Profile 1, Explicit Bindings)", Float) = 0
        [Enum(HQ, 0, MID, 1, LOW, 2)] _MonolithTerrainQuality("Source Terrain Quality (Explicit)", Float) = 0
        [HideInInspector] _MonolithTerrainMidAvailable("Verified MID Atlas Bound", Float) = 0
        [HideInInspector] _MonolithTerrainLowAvailable("Verified LOW Source", Float) = 0
        [HideInInspector] _MonolithTerrainLodFlipV("Source Terrain Atlas Flip V", Float) = 0
        _MonolithTerrainLodAtlas("Source Terrain MID Atlas (Linear Encoded)", 2D) = "grey" {}
        [HideInInspector] _MonolithTerrainSource("Terrain Source: 0 Unknown 1 Anomaly Atmospherics 2 True Stalker", Float) = 0
        [Toggle] _MonolithTerrainParallax("Source Terrain POM", Float) = 0
        [Toggle] _MonolithTerrainOcclusion("Source Terrain Height AO", Float) = 0
        [Toggle] _MonolithTerrainVirtualHeight("Source Terrain Lighting Position", Float) = 0
        [HideInInspector] _MonolithHasTerrainHeight("Source Terrain Heights Bound", Float) = 0
        [HideInInspector] _MonolithHasTerrainSourceColor("Source Terrain UNORM Colors Bound", Float) = 0
        _MonolithTerrainSourceBase("Source Terrain Base (Linear Encoded)", 2D) = "black" {}
        _MonolithTerrainSourceColorR("Source Terrain Color R (Linear Encoded)", 2D) = "grey" {}
        _MonolithTerrainSourceColorG("Source Terrain Color G (Linear Encoded)", 2D) = "grey" {}
        _MonolithTerrainSourceColorB("Source Terrain Color B (Linear Encoded)", 2D) = "grey" {}
        _MonolithTerrainSourceColorA("Source Terrain Color A (Linear Encoded)", 2D) = "grey" {}
        _MonolithTerrainPom("ssfx_terrain_pom", Vector) = (36,20,0.04,1)
        _MonolithTerrainOffset("ssfx_terrain_offset", Vector) = (-0.07,-0.05,-0.07,0)
        _MonolithTerrainColorFlipV("Terrain Color Orientation RGBA", Vector) = (0,0,0,0)
        _MonolithTerrainBumpFlipV("Terrain Bump Orientation RGBA", Vector) = (0,0,0,0)
        _MonolithTerrainHeightFlipV("Terrain Height Orientation RGBA", Vector) = (0,0,0,0)
        _MonolithTerrainColorSrgb("Terrain Color Hardware sRGB RGBA", Vector) = (1,1,1,1)
        _MonolithTerrainMaskFlipV("Terrain Mask Orientation", Float) = 0
        _MonolithTerrainBaseFlipV("Terrain Base Orientation", Float) = 0
        _MonolithTerrainHeightR("Terrain Height R (Linear R)", 2D) = "black" {}
        _MonolithTerrainHeightG("Terrain Height G (Linear R)", 2D) = "black" {}
        _MonolithTerrainHeightB("Terrain Height B (Linear R)", 2D) = "black" {}
        _MonolithTerrainHeightA("Terrain Height A (Linear R)", 2D) = "black" {}
        [MainColor] _BaseColor("BaseColor", Color) = (1,1,1,1)
        [MainTexture] _BaseColorMap("BaseColorMap", 2D) = "white" {}
        _Metallic("_Metallic", Range(0.0, 1.0)) = 0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _MaskMap("MaskMap", 2D) = "white" {}
        _MetallicRemapMin("MetallicRemapMin", Float) = 0.0
        _MetallicRemapMax("MetallicRemapMax", Float) = 1.0
        _SmoothnessRemapMin("SmoothnessRemapMin", Float) = 0.0
        _SmoothnessRemapMax("SmoothnessRemapMax", Float) = 1.0
        _AlphaRemapMin("AlphaRemapMin", Float) = 0.0
        _AlphaRemapMax("AlphaRemapMax", Float) = 1.0
        _AORemapMin("AORemapMin", Float) = 0.0
        _AORemapMax("AORemapMax", Float) = 1.0
        _NormalMap("NormalMap", 2D) = "bump" {}
        _NormalMapOS("NormalMapOS", 2D) = "white" {}
        _NormalScale("_NormalScale", Range(0.0, 8.0)) = 1
        _BentNormalMap("_BentNormalMap", 2D) = "bump" {}
        _BentNormalMapOS("_BentNormalMapOS", 2D) = "white" {}
        _HeightMap("HeightMap", 2D) = "black" {}
        [HideInInspector] _HeightAmplitude("Height Amplitude", Float) = 0.02
        [HideInInspector] _HeightCenter("Height Center", Range(0.0, 1.0)) = 0.5
        [Enum(MinMax, 0, Amplitude, 1)] _HeightMapParametrization("Heightmap Parametrization", Int) = 0
        _HeightOffset("Height Offset", Float) = 0
        _HeightMin("Heightmap Min", Float) = -1
        _HeightMax("Heightmap Max", Float) = 1
        _HeightTessAmplitude("Amplitude", Float) = 2.0
        _HeightTessCenter("Height Center", Range(0.0, 1.0)) = 0.5
        _HeightPoMAmplitude("Height Amplitude", Float) = 2.0
        _DetailMap("DetailMap", 2D) = "linearGrey" {}
        _DetailAlbedoScale("_DetailAlbedoScale", Range(0.0, 2.0)) = 1
        _DetailNormalScale("_DetailNormalScale", Range(0.0, 2.0)) = 1
        _DetailSmoothnessScale("_DetailSmoothnessScale", Range(0.0, 2.0)) = 1
        _TangentMap("TangentMap", 2D) = "bump" {}
        _TangentMapOS("TangentMapOS", 2D) = "white" {}
        _Anisotropy("Anisotropy", Range(-1.0, 1.0)) = 0
        _AnisotropyMap("AnisotropyMap", 2D) = "white" {}
        _SubsurfaceMask("Subsurface Radius", Range(0.0, 1.0)) = 1.0
        _SubsurfaceMaskMap("Subsurface Radius Map", 2D) = "white" {}
        _TransmissionMask("Transmission Mask", Range(0.0, 1.0)) = 1.0
        _TransmissionMaskMap("Transmission Mask Map", 2D) = "white" {}
        _Thickness("Thickness", Float) = 1.0
        _ThicknessMap("Thickness Map", 2D) = "white" {}
        _ThicknessRemap("Thickness Remap", Vector) = (0, 1, 0, 0)
        _IridescenceThickness("Iridescence Thickness", Range(0.0, 1.0)) = 1.0
        _IridescenceThicknessMap("Iridescence Thickness Map", 2D) = "white" {}
        _IridescenceThicknessRemap("Iridescence Thickness Remap", Vector) = (0, 1, 0, 0)
        _IridescenceMask("Iridescence Mask", Range(0.0, 1.0)) = 1.0
        _IridescenceMaskMap("Iridescence Mask Map", 2D) = "white" {}
        _CoatMask("Coat Mask", Range(0.0, 1.0)) = 0.0
        _CoatMaskMap("CoatMaskMap", 2D) = "white" {}
        [ToggleUI] _EnergyConservingSpecularColor("_EnergyConservingSpecularColor", Float) = 1.0
        _SpecularColor("SpecularColor", Color) = (1, 1, 1, 1)
        _SpecularColorMap("SpecularColorMap", 2D) = "white" {}
        [Enum(Off, 0, From Ambient Occlusion, 1, From AO and Bent Normals, 2)]  _SpecularOcclusionMode("Specular Occlusion Mode", Int) = 1
        [HDR] _EmissiveColor("EmissiveColor", Color) = (0, 0, 0)
        [HideInInspector] _EmissiveColorLDR("EmissiveColor LDR", Color) = (0, 0, 0)
        _EmissiveColorMap("EmissiveColorMap", 2D) = "white" {}
        [ToggleUI] _AlbedoAffectEmissive("Albedo Affect Emissive", Float) = 0.0
        _EmissiveIntensityUnit("Emissive Mode", Int) = 0
        [ToggleUI] _UseEmissiveIntensity("Use Emissive Intensity", Int) = 0
        _EmissiveIntensity("Emissive Intensity", Float) = 1
        _EmissiveExposureWeight("Emissive Pre Exposure", Range(0.0, 1.0)) = 1.0
        [ToggleUI]  _UseShadowThreshold("_UseShadowThreshold", Float) = 0.0
        [ToggleUI]  _AlphaCutoffEnable("Alpha Cutoff Enable", Float) = 0.0
        _AlphaCutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _AlphaCutoffShadow("_AlphaCutoffShadow", Range(0.0, 1.0)) = 0.5
        _AlphaCutoffPrepass("_AlphaCutoffPrepass", Range(0.0, 1.0)) = 0.5
        _AlphaCutoffPostpass("_AlphaCutoffPostpass", Range(0.0, 1.0)) = 0.5
        [ToggleUI] _TransparentDepthPrepassEnable("_TransparentDepthPrepassEnable", Float) = 0.0
        [ToggleUI] _TransparentBackfaceEnable("_TransparentBackfaceEnable", Float) = 0.0
        [ToggleUI] _TransparentDepthPostpassEnable("_TransparentDepthPostpassEnable", Float) = 0.0
        _TransparentSortPriority("_TransparentSortPriority", Float) = 0
        [Enum(None, 0, Planar, 1, Sphere, 2, Thin, 3)]_RefractionModel("Refraction Model", Int) = 0
        _Ior("Index Of Refraction", Range(1.0, 2.5)) = 1.5
        _TransmittanceColor("Transmittance Color", Color) = (1.0, 1.0, 1.0)
        _TransmittanceColorMap("TransmittanceColorMap", 2D) = "white" {}
        _ATDistance("Transmittance Absorption Distance", Float) = 1.0
        [ToggleUI] _TransparentWritingMotionVec("_TransparentWritingMotionVec", Float) = 0.0
        [ToggleUI] _PerPixelSorting("_PerPixelSorting", Float) = 0.0
        [HideInInspector] _StencilRef("_StencilRef", Int) = 0
        [HideInInspector] _StencilWriteMask("_StencilWriteMask", Int) = 3
        [HideInInspector] _StencilRefGBuffer("_StencilRefGBuffer", Int) = 2
        [HideInInspector] _StencilWriteMaskGBuffer("_StencilWriteMaskGBuffer", Int) = 3
        [HideInInspector] _StencilRefDepth("_StencilRefDepth", Int) = 0
        [HideInInspector] _StencilWriteMaskDepth("_StencilWriteMaskDepth", Int) = 8
        [HideInInspector] _StencilRefMV("_StencilRefMV", Int) = 32
        [HideInInspector] _StencilWriteMaskMV("_StencilWriteMaskMV", Int) = 32
        _SurfaceType("__surfacetype", Float) = 0.0
        _BlendMode("__blendmode", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _DstBlend2("__dst2", Float) = 0.0
        [HideInInspector] _AlphaSrcBlend("__alphaSrc", Float) = 1.0
        [HideInInspector] _AlphaDstBlend("__alphaDst", Float) = 0.0
        [HideInInspector][ToggleUI] _ZWrite("__zw", Float) = 1.0
        [HideInInspector][ToggleUI] _TransparentZWrite("_TransparentZWrite", Float) = 0.0
        [HideInInspector] _CullMode("__cullmode", Float) = 2.0
        [HideInInspector] _CullModeForward("__cullmodeForward", Float) = 2.0
        [Enum(UnityEngine.Rendering.HighDefinition.TransparentCullMode)] _TransparentCullMode("_TransparentCullMode", Int) = 2
        [Enum(UnityEngine.Rendering.HighDefinition.OpaqueCullMode)] _OpaqueCullMode("_OpaqueCullMode", Int) = 2
        [HideInInspector] _ZTestDepthEqualForOpaque("_ZTestDepthEqualForOpaque", Int) = 4
        [HideInInspector] _ZTestGBuffer("_ZTestGBuffer", Int) = 4
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestTransparent("Transparent ZTest", Int) = 4
        [ToggleUI] _EnableFogOnTransparent("Enable Fog", Float) = 1.0
        [ToggleUI] _EnableBlendModePreserveSpecularLighting("Enable Blend Mode Preserve Specular Lighting", Float) = 1.0
        [ToggleUI] _DoubleSidedEnable("Double sided enable", Float) = 0.0
        [Enum(Flip, 0, Mirror, 1, None, 2)] _DoubleSidedNormalMode("Double sided normal mode", Float) = 1
        [HideInInspector] _DoubleSidedConstants("_DoubleSidedConstants", Vector) = (1, 1, -1, 0)
        [Enum(Auto, 0, On, 1, Off, 2)] _DoubleSidedGIMode("Double sided GI mode", Float) = 0
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3, Planar, 4, Triplanar, 5)] _UVBase("UV Set for base", Float) = 0
        [Enum(WorldSpace, 0, ObjectSpace, 1)] _ObjectSpaceUVMapping("Mapping space", Float) = 0.0
        _TexWorldScale("Scale to apply on world coordinate", Float) = 1.0
        [HideInInspector] _InvTilingScale("Inverse tiling scale = 2 / (abs(_BaseColorMap_ST.x) + abs(_BaseColorMap_ST.y))", Float) = 1
        [HideInInspector] _UVMappingMask("_UVMappingMask", Color) = (1, 0, 0, 0)
        [Enum(TangentSpace, 0, ObjectSpace, 1)] _NormalMapSpace("NormalMap space", Float) = 0
        [Enum(Subsurface Scattering, 0, Standard, 1, Anisotropy, 2, Iridescence, 3, Specular Color, 4, Translucent, 5)] _MaterialID("MaterialId", Int) = 1
        [ToggleUI] _TransmissionEnable("_TransmissionEnable", Float) = 1.0
        _DisplacementMode("DisplacementMode", Int) = 0
        [ToggleUI] _DisplacementLockObjectScale("displacement lock object scale", Float) = 1.0
        [ToggleUI] _DisplacementLockTilingScale("displacement lock tiling scale", Float) = 1.0
        [ToggleUI] _DepthOffsetEnable("Depth Offset View space", Float) = 0.0
        [ToggleUI] _EnableGeometricSpecularAA("EnableGeometricSpecularAA", Float) = 0.0
        _SpecularAAScreenSpaceVariance("SpecularAAScreenSpaceVariance", Range(0.0, 1.0)) = 0.1
        _SpecularAAThreshold("SpecularAAThreshold", Range(0.0, 1.0)) = 0.2
        _PPDMinSamples("Min sample for POM", Range(1.0, 64.0)) = 5
        _PPDMaxSamples("Max sample for POM", Range(1.0, 64.0)) = 15
        _PPDLodThreshold("Start lod to fade out the POM effect", Range(0.0, 16.0)) = 5
        _PPDPrimitiveLength("Primitive length for POM", Float) = 1
        _PPDPrimitiveWidth("Primitive width for POM", Float) = 1
        [HideInInspector] _InvPrimScale("Inverse primitive scale for non-planar POM", Vector) = (1, 1, 0, 0)
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3)] _UVDetail("UV Set for detail", Float) = 0
        [HideInInspector] _UVDetailsMappingMask("_UVDetailsMappingMask", Color) = (1, 0, 0, 0)
        [ToggleUI] _LinkDetailsWithBase("LinkDetailsWithBase", Float) = 1.0
        [Enum(Use Emissive Color, 0, Use Emissive Mask, 1)] _EmissiveColorMode("Emissive color mode", Float) = 1
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3, Planar, 4, Triplanar, 5, Same as Base, 6)] _UVEmissive("UV Set for emissive", Float) = 0
        [Enum(WorldSpace, 0, ObjectSpace, 1)] _ObjectSpaceUVMappingEmissive("Mapping space", Float) = 0.0
        _TexWorldScaleEmissive("Scale to apply on world coordinate", Float) = 1.0
        [HideInInspector] _UVMappingMaskEmissive("_UVMappingMaskEmissive", Color) = (1, 0, 0, 0)
        _EmissionColor("Color", Color) = (1, 1, 1)
        [HideInInspector] _MainTex("Albedo", 2D) = "white" {}
        [HideInInspector] _Color("Color", Color) = (1,1,1,1)
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [ToggleUI] _SupportDecals("Support Decals", Float) = 1.0
        [ToggleUI] _ReceivesSSR("Receives SSR", Float) = 1.0
        [ToggleUI] _ReceivesSSRTransparent("Receives SSR Transparent", Float) = 0.0
        [ToggleUI] _AddPrecomputedVelocity("AddPrecomputedVelocity", Float) = 0.0
        [ToggleUI] _RayTracing("Ray Tracing (Preview)", Float) = 0
        [HideInInspector] _DiffusionProfile("Obsolete, kept for migration purpose", Int) = 0
        [HideInInspector] _DiffusionProfileAsset("Diffusion Profile Asset", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DiffusionProfileHash("Diffusion Profile Hash", Float) = 0
        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }
    HLSLINCLUDE
        #pragma shader_feature_local _MONOLITH_ORIGINAL_ENVIRONMENT
        #pragma shader_feature_local _MONOLITH_ORIGINAL_TBN
        #pragma shader_feature_local _MONOLITH_SKINNED_TBN
        #pragma shader_feature_local _MONOLITH_LEVEL
        #pragma shader_feature_local _MONOLITH_LEVEL_DETAIL
        #pragma shader_feature_local _MONOLITH_LEVEL_LOD
        #pragma shader_feature_local_fragment _MONOLITH_LEVEL_FLORA
        #pragma shader_feature_local_fragment _MONOLITH_LEVEL_TERRAIN
        #pragma multi_compile_local_fragment _ _MONOLITH_PRECIPITATION
        #pragma multi_compile_local_fragment _ _MONOLITH_WET_ANOMALY_TERRAIN
        #ifdef _MONOLITH_ORIGINAL_TBN
        #define _REQUIRE_UV3
        #endif
        #if (defined(_MONOLITH_LEVEL) || defined(_MONOLITH_LEVEL_DETAIL) || defined(_MONOLITH_LEVEL_LOD)) && !defined(_REQUIRE_UV3)
        #define _REQUIRE_UV3
        #endif
        #define MONOLITH_PREVIOUS_FRAME_TIME (-1048576.0)
        #if defined(_MONOLITH_LEVEL_DETAIL)
        #define ATTRIBUTES_NEED_TEXCOORD1
        #define VARYINGS_NEED_TEXCOORD1
        #endif
        #if defined(_MONOLITH_LEVEL) && !defined(_MONOLITH_SKINNED_TBN) && !defined(_MONOLITH_LEVEL_DETAIL)
        #define ATTRIBUTES_NEED_TEXCOORD0
        #define ATTRIBUTES_NEED_TEXCOORD3
        #endif
    #pragma target 4.5
    #pragma shader_feature_local _DEPTHOFFSET_ON
    #pragma shader_feature_local _DOUBLESIDED_ON
    #pragma shader_feature_local _ _VERTEX_DISPLACEMENT _PIXEL_DISPLACEMENT
    #pragma shader_feature_local_vertex _VERTEX_DISPLACEMENT_LOCK_OBJECT_SCALE
    #pragma shader_feature_local _DISPLACEMENT_LOCK_TILING_SCALE
    #pragma shader_feature_local_fragment _PIXEL_DISPLACEMENT_LOCK_OBJECT_SCALE
    #pragma shader_feature_local_raytracing _ _REFRACTION_PLANE _REFRACTION_SPHERE _REFRACTION_THIN
    #pragma shader_feature_local_fragment _ _EMISSIVE_MAPPING_PLANAR _EMISSIVE_MAPPING_TRIPLANAR _EMISSIVE_MAPPING_BASE
    #pragma shader_feature_local _ _MAPPING_PLANAR _MAPPING_TRIPLANAR
    #pragma shader_feature_local_raytracing _ _EMISSIVE_MAPPING_PLANAR _EMISSIVE_MAPPING_TRIPLANAR _EMISSIVE_MAPPING_BASE
    #pragma shader_feature_local_raytracing _NORMALMAP_TANGENT_SPACE
    #pragma shader_feature_local _ _REQUIRE_UV2 _REQUIRE_UV3
    #pragma shader_feature_local_raytracing _MASKMAP
    #pragma shader_feature_local_raytracing _BENTNORMALMAP
    #pragma shader_feature_local_raytracing _EMISSIVE_COLOR_MAP
    #pragma shader_feature_local_fragment _ENABLESPECULAROCCLUSION
    #pragma shader_feature_local_fragment _ _SPECULAR_OCCLUSION_NONE _SPECULAR_OCCLUSION_FROM_BENT_NORMAL_MAP
    #pragma shader_feature_local_raytracing _ENABLESPECULAROCCLUSION
    #pragma shader_feature_local_raytracing _ _SPECULAR_OCCLUSION_NONE _SPECULAR_OCCLUSION_FROM_BENT_NORMAL_MAP
    #ifdef _ENABLESPECULAROCCLUSION
    #define _SPECULAR_OCCLUSION_FROM_BENT_NORMAL_MAP
    #endif
    #pragma shader_feature_local _HEIGHTMAP
    #pragma shader_feature_local_raytracing _TANGENTMAP
    #pragma shader_feature_local_raytracing _ANISOTROPYMAP
    #pragma shader_feature_local_raytracing _DETAIL_MAP
    #pragma shader_feature_local_raytracing _SUBSURFACE_MASK_MAP
    #pragma shader_feature_local_raytracing _THICKNESSMAP
    #pragma shader_feature_local_raytracing _IRIDESCENCE_THICKNESSMAP
    #pragma shader_feature_local_raytracing _SPECULARCOLORMAP
    #pragma shader_feature_local_raytracing _TRANSMITTANCECOLORMAP
    #pragma shader_feature_local_raytracing _DISABLE_SSR
    #pragma shader_feature_local_raytracing _MATERIAL_FEATURE_SUBSURFACE_SCATTERING
    #pragma shader_feature_local_raytracing _MATERIAL_FEATURE_TRANSMISSION
    #pragma shader_feature_local_raytracing _MATERIAL_FEATURE_ANISOTROPY
    #pragma shader_feature_local_raytracing _MATERIAL_FEATURE_CLEAR_COAT
    #pragma shader_feature_local_raytracing _MATERIAL_FEATURE_IRIDESCENCE
    #pragma shader_feature_local_raytracing _MATERIAL_FEATURE_SPECULAR_COLOR
    #define SUPPORT_GLOBAL_MIP_BIAS
    #define HAVE_RECURSIVE_RENDERING
    #define HAVE_VERTEX_MODIFICATION
    #define SUPPORT_BLENDMODE_PRESERVE_SPECULAR_LIGHTING
    #ifndef _SURFACE_TYPE_TRANSPARENT
    #undef _ENABLE_FOG_ON_TRANSPARENT
    #endif
    #if defined(_MATERIAL_FEATURE_SUBSURFACE_SCATTERING) && !defined(_SURFACE_TYPE_TRANSPARENT)
    #define OUTPUT_SPLIT_LIGHTING
    #endif
    #if (defined(_TRANSPARENT_WRITES_MOTION_VEC) || defined(_TRANSPARENT_REFRACTIVE_SORT)) && defined(_SURFACE_TYPE_TRANSPARENT)
    #define _WRITE_TRANSPARENT_MOTION_VECTOR
    #endif
    #ifdef _HEIGHTMAP
    #define _CONSERVATIVE_DEPTH_OFFSET
    #endif
    #define PREFER_HALF 0
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
    #include "MonolithProperties.hlsl"
    #if defined(_MONOLITH_PRECIPITATION) && defined(_MONOLITH_WET_ANOMALY_TERRAIN) && defined(_MONOLITH_LEVEL_TERRAIN)
    #define _MonolithSourceProfile 1.0
    #define _MonolithSourceTerrain 1.0
    #define _MonolithTerrainSource 1.0
    #define _MonolithHasTerrainSourceColor 1.0
    #define _MonolithTrueStalkerSourceTerrain 0.0
    #define _MonolithTrueStalkerHasLookup 0.0
    #define MonolithTrueStalkerTerrainEnabled() false
    #endif
    #include "MonolithLightInputs.hlsl"
    #include "MonolithMaterial.hlsl"
    #ifdef _MONOLITH_PRECIPITATION
    #include "MonolithWetness.hlsl"
    #endif
    #include "MonolithFlora.hlsl"
    #include "MonolithTrueStalkerTerrain.hlsl"
    #include "MonolithSourceTexture.hlsl"
    #include "MonolithEnvironment.hlsl"
    ENDHLSL
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDLitShader" "Queue" = "Geometry" }
        Pass
        {
            Name "ScenePickingPass"
            Tags { "LightMode" = "Picking" }
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local _ALPHATEST_ON
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #define SCENEPICKINGPASS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/PickingSpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #include "MonolithData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma editor_sync_compilation
            ENDHLSL
        }
        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }
            Cull Off
            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local _ALPHATEST_ON
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #define SCENESELECTIONPASS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/PickingSpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #include "MonolithData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma editor_sync_compilation
            ENDHLSL
        }
        Pass
        {
            Name "META"
            Tags{ "LightMode" = "META" }
            Cull Off
            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma shader_feature EDITOR_VISUALIZATION
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_CLEAR_COAT
            #pragma shader_feature_local _DISABLE_DECALS
            #pragma shader_feature_local_fragment _DISABLE_SSR
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_SUBSURFACE_SCATTERING
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_TRANSMISSION
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_ANISOTROPY
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_IRIDESCENCE
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_SPECULAR_COLOR
            #pragma shader_feature_local_fragment _ENABLE_GEOMETRIC_SPECULAR_AA
            #pragma shader_feature_local_fragment _BENTNORMALMAP
            #pragma shader_feature_local_fragment _EMISSIVE_COLOR_MAP
            #pragma shader_feature_local_fragment _TANGENTMAP
            #pragma shader_feature_local_fragment _ANISOTROPYMAP
            #pragma shader_feature_local_fragment _DETAIL_MAP
            #pragma shader_feature_local_fragment _SUBSURFACE_MASK_MAP
            #pragma shader_feature_local_fragment _TRANSMISSION_MASK_MAP
            #pragma shader_feature_local_fragment _THICKNESSMAP
            #pragma shader_feature_local_fragment _IRIDESCENCE_THICKNESSMAP
            #pragma shader_feature_local_fragment _SPECULARCOLORMAP
            #pragma shader_feature_local_fragment _TRANSMITTANCECOLORMAP
            #pragma shader_feature_local_fragment _MASKMAP
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _NORMALMAP_TANGENT_SPACE
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ENABLE_FOG_ON_TRANSPARENT
            #define SHADERPASS SHADERPASS_LIGHT_TRANSPORT
            #define SCENEPICKINGPASS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/PickingSpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #include "MonolithData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassLightTransport.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags{ "LightMode" = "ShadowCaster" }
            Cull[_CullMode]
            ZClip [_ZClip]
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local _ALPHATEST_ON
            #define SHADERPASS SHADERPASS_SHADOWS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #include "MonolithData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "DepthForwardOnly"
            Tags{ "LightMode" = "DepthForwardOnly" }
            Cull[_CullMode]
            AlphaToMask [_AlphaCutoffEnable]
            Stencil
            {
                WriteMask [_StencilWriteMaskDepth]
                Ref [_StencilRefDepth]
                Comp Always
                Pass Replace
            }
            ZWrite On
            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #define WRITE_NORMAL_BUFFER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #pragma multi_compile _ WRITE_DECAL_BUFFER WRITE_RENDERING_LAYER
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_CLEAR_COAT
            #pragma shader_feature_local _DISABLE_DECALS
            #pragma shader_feature_local_fragment _ENABLE_GEOMETRIC_SPECULAR_AA
            #pragma shader_feature_local_fragment _MASKMAP
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _NORMALMAP_TANGENT_SPACE
            #pragma shader_feature_local _ALPHATEST_ON
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #ifdef WRITE_NORMAL_BUFFER
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #else
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitDepthPass.hlsl"
            #endif
            #include "MonolithData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags{ "LightMode" = "MotionVectors" }
            Stencil
            {
                WriteMask [_StencilWriteMaskMV]
                Ref [_StencilRefMV]
                Comp Always
                Pass Replace
            }
            Cull[_CullMode]
            AlphaToMask [_AlphaCutoffEnable]
            ZWrite On
            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #define WRITE_NORMAL_BUFFER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #pragma multi_compile _ WRITE_DECAL_BUFFER_AND_RENDERING_LAYER
            #pragma shader_feature_local _DISABLE_DECALS
            #pragma shader_feature_local_fragment _DISABLE_SSR
            #pragma shader_feature_local_fragment _BENTNORMALMAP
            #pragma shader_feature_local_fragment _TANGENTMAP
            #pragma shader_feature_local_fragment _DETAIL_MAP
            #pragma shader_feature_local_fragment _MASKMAP
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local _ _TRANSPARENT_WRITES_MOTION_VEC _TRANSPARENT_REFRACTIVE_SORT
            #pragma shader_feature_local _ADD_PRECOMPUTED_VELOCITY
            #ifdef WRITE_DECAL_BUFFER_AND_RENDERING_LAYER
            #define WRITE_DECAL_BUFFER
            #endif
            #define SHADERPASS SHADERPASS_MOTION_VECTORS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #ifdef WRITE_NORMAL_BUFFER
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #else
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitMotionVectorPass.hlsl"
            #endif
            #include "MonolithData.hlsl"
            #define _LastTimeParameters float4(MONOLITH_PREVIOUS_FRAME_TIME, 0.0, 0.0, 0.0)
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassMotionVectors.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Stencil
            {
                WriteMask [_StencilWriteMask]
                Ref [_StencilRef]
                Comp Always
                Pass Replace
            }
            Blend [_SrcBlend] [_DstBlend], [_AlphaSrcBlend] [_AlphaDstBlend]
            Blend 1 One OneMinusSrcAlpha
            Blend 2 One [_DstBlend2]
            Blend 3 One [_DstBlend2]
            Blend 4 One OneMinusSrcAlpha
            ZTest [_ZTestDepthEqualForOpaque]
            ZWrite [_ZWrite]
            Cull [_CullModeForward]
            ColorMask [_ColorMaskTransparentVelOne] 1
            ColorMask [_ColorMaskTransparentVelTwo] 2
            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment SCREEN_SPACE_SHADOWS_OFF SCREEN_SPACE_SHADOWS_ON
            #pragma multi_compile_fragment DECALS_OFF DECALS_3RT DECALS_4RT
            #pragma multi_compile_fragment _ DECAL_SURFACE_GRADIENT
	        #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
	        #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma multi_compile_fragment USE_FPTL_LIGHTLIST USE_CLUSTERED_LIGHTLIST
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_CLEAR_COAT
            #pragma shader_feature_local _DISABLE_DECALS
            #pragma shader_feature_local_fragment _DISABLE_SSR
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_SUBSURFACE_SCATTERING
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_TRANSMISSION
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_ANISOTROPY
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_IRIDESCENCE
            #pragma shader_feature_local_fragment _MATERIAL_FEATURE_SPECULAR_COLOR
            #pragma shader_feature_local_fragment _ENABLE_GEOMETRIC_SPECULAR_AA
            #pragma shader_feature_local_fragment _BENTNORMALMAP
            #pragma shader_feature_local_fragment _EMISSIVE_COLOR_MAP
            #pragma shader_feature_local_fragment _TANGENTMAP
            #pragma shader_feature_local_fragment _ANISOTROPYMAP
            #pragma shader_feature_local_fragment _DETAIL_MAP
            #pragma shader_feature_local_fragment _SUBSURFACE_MASK_MAP
            #pragma shader_feature_local_fragment _TRANSMISSION_MASK_MAP
            #pragma shader_feature_local_fragment _THICKNESSMAP
            #pragma shader_feature_local_fragment _IRIDESCENCE_THICKNESSMAP
            #pragma shader_feature_local_fragment _SPECULARCOLORMAP
            #pragma shader_feature_local_fragment _TRANSMITTANCECOLORMAP
            #pragma shader_feature_local_fragment _MASKMAP
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _NORMALMAP_TANGENT_SPACE
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local _ _TRANSPARENT_WRITES_MOTION_VEC _TRANSPARENT_REFRACTIVE_SORT
            #pragma shader_feature_local_fragment _ENABLE_FOG_ON_TRANSPARENT
            #pragma shader_feature_local _DISABLE_SSR_TRANSPARENT
            #pragma shader_feature_local_fragment _ _REFRACTION_PLANE _REFRACTION_SPHERE _REFRACTION_THIN
            #pragma shader_feature_local _ADD_PRECOMPUTED_VELOCITY
            #ifndef SHADER_STAGE_FRAGMENT
            #define SHADOW_LOW
            #ifndef USE_FPTL_LIGHTLIST
            #define USE_FPTL_LIGHTLIST
            #endif
            #endif
            #define SHADERPASS SHADERPASS_FORWARD
            #if !defined(_SURFACE_TYPE_TRANSPARENT) && !defined(DEBUG_DISPLAY) && !defined(_ALPHATEST_ON)
                #define SHADERPASS_FORWARD_BYPASS_ALPHA_TEST
            #endif
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
        #ifdef DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
        #endif
            #define HAS_LIGHTLOOP
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
            #include "MonolithLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #include "MonolithData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
    CustomEditor "XRay.Importer.Editor.MonolithMaterialInspector"
    Fallback Off
}
