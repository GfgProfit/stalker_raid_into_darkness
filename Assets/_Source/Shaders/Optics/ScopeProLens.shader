Shader "RaidIntoDarkness/Optics/ScopeProLens"
{
    Properties
    {
        [NoScaleOffset] _Reticle("Reticle", 2D) = "white" {}
        [HDR] _ReticleColor("Reticle Color", Color) = (1, 1, 1, 1)
        _ReticleSize("Reticle Size", Range(0.1, 2)) = 0.8
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
