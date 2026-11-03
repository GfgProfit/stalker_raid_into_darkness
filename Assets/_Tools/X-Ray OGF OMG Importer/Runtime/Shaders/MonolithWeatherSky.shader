Shader "XRay/Monolith Weather Sky"
{
    HLSLINCLUDE

    #pragma vertex Vert
    #pragma target 4.5
    #pragma editor_sync_compilation
    #pragma only_renderers d3d11 vulkan metal

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/SkyUtils.hlsl"

    TEXTURECUBE(_XRaySky0);
    TEXTURECUBE(_XRaySky1);
    TEXTURECUBE(_XRaySky2);
    TEXTURECUBE(_XRaySky3);
    SAMPLER(sampler_XRaySky0);
    float4 _XRaySkyWeights;
    float4 _XRaySkyTint;
    float4 _XRaySkyRotation;

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
        return output;
    }

    float4 RenderSourceSky(Varyings input)
    {
        float3 ray = -GetSkyViewDirWS(input.positionCS.xy);
        float3 localRay = float3(_XRaySkyRotation.x * ray.x - _XRaySkyRotation.y * ray.z, ray.y, _XRaySkyRotation.y * ray.x + _XRaySkyRotation.x * ray.z);
        float3 cubePosition = localRay / max(abs(localRay.x), max(abs(localRay.y), abs(localRay.z)));
        cubePosition.y = cubePosition.y >= 0.0 ? 2.0 * cubePosition.y - 1.0 : 0.01 * cubePosition.y - 1.0;
        float3 encoded = SAMPLE_TEXTURECUBE(_XRaySky0, sampler_XRaySky0, cubePosition).rgb * _XRaySkyWeights.x;
        encoded += SAMPLE_TEXTURECUBE(_XRaySky1, sampler_XRaySky0, cubePosition).rgb * _XRaySkyWeights.y;
        encoded += SAMPLE_TEXTURECUBE(_XRaySky2, sampler_XRaySky0, cubePosition).rgb * _XRaySkyWeights.z;
        encoded += SAMPLE_TEXTURECUBE(_XRaySky3, sampler_XRaySky0, cubePosition).rgb * _XRaySkyWeights.w;
        encoded *= _XRaySkyTint.rgb * 1.70;
        return float4(pow(max(encoded, 0.0), 2.2), 1.0);
    }

    float4 FragBaking(Varyings input) : SV_Target
    {
        return RenderSourceSky(input);
    }

    float4 FragRender(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float4 color = RenderSourceSky(input);
        color.rgb *= GetCurrentExposureMultiplier();
        return color;
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma fragment FragBaking
            ENDHLSL
        }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma fragment FragRender
            ENDHLSL
        }
    }

    Fallback Off
}
