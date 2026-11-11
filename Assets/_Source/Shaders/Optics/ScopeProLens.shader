Shader "RaidIntoDarkness/Optics/ScopeProLens"
{
    Properties
    {
        [Enum(LegacyPNG, 0, SDF, 1)] _ReticleMode("Reticle Mode", Float) = 0
        [NoScaleOffset] _Reticle("Reticle", 2D) = "white" {}
        [NoScaleOffset] _ReticleSdf("Reticle SDF (R Ink, G Illumination)", 2D) = "black" {}
        [HDR] _ReticleColor("Reticle Color", Color) = (1, 1, 1, 1)
        [HDR] _IlluminationColor("SDF Illumination Color", Color) = (1, 0, 0, 1)
        _IlluminationIntensity("SDF Illumination Intensity", Range(0, 10)) = 0
        _ReticleSize("Reticle Size", Range(0.1, 5)) = 0.8
        [ToggleUI] _ReticleExtendLeft("SDF Extend Left Line", Float) = 0
        [ToggleUI] _ReticleExtendRight("SDF Extend Right Line", Float) = 0
        [ToggleUI] _ReticleExtendUp("SDF Extend Upper Line", Float) = 0
        [ToggleUI] _ReticleExtendDown("SDF Extend Lower Line", Float) = 0
        _LensTint("Lens Reflection Tint", Color) = (0.65, 0.8, 1, 1)
        _LensReflectionStrength("Lens Reflection Amount", Range(0, 1)) = 1
        _LensEnvironmentIntensity("Lens Environment Intensity", Range(0, 20)) = 1
        _LensRoughness("Lens Reflection Roughness", Range(0, 1)) = 0.15
        [HideInInspector] _LensUseEnvironment("Use Baked Lens Environment", Float) = 0
        [HideInInspector] [NoScaleOffset] _LensEnvironment("Baked Lens Environment", Cube) = "black" {}
        [HideInInspector] _ScopeVisibility("Scope Visibility", Float) = 1
        _ScopeDepth("Scope Depth", Range(0, 5)) = 0.94
        _ScopeApperture("Scope Aperture", Range(0, 2)) = 1.01
        _ScopeFade("Scope Fade", Range(0, 2)) = 0.5
        _RenderSize("Render Size", Range(0.001, 2)) = 1
        [NoScaleOffset] _RenderTexture("Render Texture", 2D) = "black" {}
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode("Cull Mode", Float) = 2
        [HideInInspector] _ScopeProStyle("Scope Pro Style", Float) = 1
        [HideInInspector] _ScopeAvailable("Scope Available", Float) = 0
        [HideInInspector] _ScopeRightWS("Scope Right", Vector) = (1, 0, 0, 0)
        [HideInInspector] _ScopeUpWS("Scope Up", Vector) = (0, 1, 0, 0)
        [HideInInspector] _ReticleRuntimeScale("Reticle Runtime Scale", Float) = 1
        [HideInInspector] _ReticleSdfAvailable("Reticle SDF Available", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
        #include "ScopeProLens.hlsl"
        ENDHLSL

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Cull [_CullMode]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ScopeProVertex
            #pragma fragment ScopeProDepth
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            Cull [_CullMode]
            ZWrite On
            ZTest LEqual
            Stencil
            {
                WriteMask 32
                Ref 32
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ScopeProVertex
            #pragma fragment ScopeProMotionVectors
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            ENDHLSL
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull [_CullMode]
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ScopeProVertex
            #pragma fragment ScopeProFragment
            ENDHLSL
        }
    }
    Fallback Off
}
