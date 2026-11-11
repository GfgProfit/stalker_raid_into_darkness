Shader "RaidIntoDarkness/Optics/ScopeLens"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode("Cull Mode", Float) = 2
        [HideInInspector] [NoScaleOffset] _ScopeTexture("Scope Texture", 2D) = "black" {}
        [HideInInspector] [NoScaleOffset] _ReticleTexture("Reticle Texture", 2D) = "black" {}
        [HideInInspector] [NoScaleOffset] _ReticleAlphaMask("Reticle Alpha Mask", 2D) = "white" {}
        [HideInInspector] [NoScaleOffset] _IlluminationMask("Illumination Mask", 2D) = "black" {}
        [HideInInspector] _ScopeAvailable("Scope Available", Float) = 0
        [HideInInspector] _ReticleScale("Reticle Scale", Vector) = (1, 1, 0, 0)
        [HideInInspector] _ReticleCenter("Reticle Center", Vector) = (0.5, 0.5, 0, 0)
        [HideInInspector] _ReticleOffset("Reticle Offset", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ReticleRotation("Reticle Rotation", Float) = 0
        [HideInInspector] _ReticleKeyParams("Reticle Key Parameters", Vector) = (0, 0.002, 0.03, 1)
        [HideInInspector] _ReticleOptions("Reticle Options", Vector) = (0, 0, 0, 1)
        [HideInInspector] _ReticleMipBias("Reticle Mip Bias", Float) = 0
        [HideInInspector] _ColorKeyUnpremultiply("Color Key Unpremultiply", Float) = 1
        [HideInInspector] [HDR] _IlluminationColor("Illumination Color", Color) = (1, 0, 0, 1)
        [HideInInspector] _IlluminationIntensity("Illumination Intensity", Float) = 0
        [HideInInspector] _EyeAperture("Eye Aperture", Vector) = (0.5, 0.5, 0.5, 0.5)
        [HideInInspector] _EyeTransmission("Eye Transmission", Float) = 0
        [HideInInspector] _ApertureSoftness("Aperture Softness", Float) = 0.005
        [HideInInspector] _OpticalDistortion("Optical Distortion", Vector) = (0, 0, 0, 1)
        [HideInInspector] _OpticalFalloff("Optical Falloff", Vector) = (0, 0, 0, 0)
        [HideInInspector] _CoatingTint("Coating Tint", Color) = (0.1, 0.15, 0.2, 1)
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
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
        #include "ScopeOpticsCommon.hlsl"
        #include "ScopeReticle.hlsl"
        #include "ScopeGlass.hlsl"

        ScopeVaryings ScopeVertex(ScopeAttributes input)
        {
            ScopeVaryings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = input.uv;
            return output;
        }

        float4 ScopeDepth(ScopeVaryings input) : SV_Target
        {
            ScopeClipLens(input.uv);
            return 0.0;
        }

        float4 ScopeFragment(ScopeVaryings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            ScopeClipLens(input.uv);
            if (_ScopeAvailable < 0.5)
            {
                return float4(0.0, 0.0, 0.0, 1.0);
            }

            float aperture = ScopeEyeVisibility(input.uv);
            float3 scene = ScopeSampleGlass(input.uv, input.positionWS, input.normalWS);
            float4 reticle = ScopeSampleReticle(input.uv);
            float reticleVisibility = lerp(1.0, aperture, saturate(_ReticleOptions.w));
            float exposure = GetCurrentExposureMultiplier();
            float3 exposedColor = max(scene, 0.0) * aperture * exposure;
            exposedColor = exposedColor * (1.0 - reticle.a) + saturate(reticle.rgb) * reticle.a * reticleVisibility;
            exposedColor += ScopeSampleIllumination(input.uv) * reticleVisibility * exposure;
            return float4(max(exposedColor, 0.0), 1.0);
        }
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
            #pragma vertex ScopeVertex
            #pragma fragment ScopeDepth
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
            #pragma vertex ScopeVertex
            #pragma fragment ScopeFragment
            ENDHLSL
        }
    }
    Fallback Off
}
