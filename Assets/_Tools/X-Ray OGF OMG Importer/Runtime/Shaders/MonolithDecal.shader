Shader "HDRP/MonolithDecal"
{
    Properties
    {
        [MainTexture] _BaseColorMap("Base (RGB) Blend (A)", 2D) = "white" {}
        _MonolithAlbedoSrgb("Albedo Texture Is sRGB", Float) = 1
        _MonolithDecalNormalShift("wmark NORMAL_SHIFT", Float) = 0.007
        _MonolithDecalMinShift("wmark MIN_SHIFT", Float) = 0.003
        _MonolithDecalMaxShift("wmark MAX_SHIFT", Float) = 0.011
        _MonolithDecalProjectionShift("r__wallmark_shift_pp", Float) = 0.0001
        _MonolithDecalViewShift("r__wallmark_shift_v", Float) = 0.0001
        _MonolithDecalXRayNear("X-Ray Viewport Near", Float) = 0.2
        _MonolithDecalAlphaRef("Alpha Ref", Range(0, 1)) = 0
        _DrawOrder("Draw Order", Int) = 0
        [HideInInspector] _DecalStencilRef("_DecalStencilRef", Int) = 16
        [HideInInspector] _DecalStencilWriteMask("_DecalStencilWriteMask", Int) = 16
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "DBufferMesh"
            Tags { "LightMode" = "DBufferMesh" }

            Stencil
            {
                WriteMask [_DecalStencilWriteMask]
                Ref [_DecalStencilRef]
                Comp Always
                Pass Replace
            }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend 0 SrcAlpha OneMinusSrcAlpha, Zero OneMinusSrcAlpha
            ColorMask RGBA 0
            ColorMask 0 1
            ColorMask 0 2
            ColorMask 0 3

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment DECALS_3RT DECALS_4RT
            #pragma multi_compile_instancing

            #define SHADERPASS SHADERPASS_DBUFFER_MESH
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Decal/Decal.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Decal/Decal.hlsl"
            #include "MonolithDecalPass.hlsl"
            ENDHLSL
        }
    }
}
