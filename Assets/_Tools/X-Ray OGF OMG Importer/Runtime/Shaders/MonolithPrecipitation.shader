Shader "XRay/Monolith Precipitation"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma editor_sync_compilation
    #pragma only_renderers d3d11 vulkan metal

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassSampling.hlsl"

    struct RainDrop
    {
        float3 position;
        float age;
        float3 velocity;
        uint seed;
        float3 splashPosition;
        float splashAge;
        float lifetime;
        float size;
        float2 reserved;
    };

    StructuredBuffer<RainDrop> _XRayRainDrops;
    TEXTURE2D(_XRayRainTexture);
    TEXTURE2D(_XRaySplashTexture);
    float4 _XRayRainShape;
    float4 _XRayRainAppearance;
    float4 _XRayRainColor;
    float4 _XRayRainCamera;

    static const uint QUAD_INDICES[6] = { 0, 1, 2, 2, 1, 3 };
    static const float SPLASH_LIFETIME = 0.3;

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        uint instanceID : SV_InstanceID;
    };

    struct RainVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float visibility : TEXCOORD1;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    RainVaryings VertRain(Attributes input)
    {
        RainVaryings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        RainDrop drop = _XRayRainDrops[input.instanceID];
        uint corner = QUAD_INDICES[input.vertexID];
        float head = corner >= 2u ? 1.0 : 0.0;
        float right = (corner & 1u) != 0u ? 1.0 : 0.0;
        float3 direction = SafeNormalize(drop.velocity);
        float3 trail = drop.position - direction * _XRayRainShape.x * (0.5 + 0.5 * saturate(_XRayRainAppearance.x));
        float3 center = (trail + drop.position) * 0.5;
        float3 cameraDirection = SafeNormalize(center - _XRayRainCamera.xyz);
        float3 lineTop = cross(cameraDirection, direction);
        float3 position = lerp(trail, drop.position, head) + lineTop * _XRayRainShape.y * (right * 2.0 - 1.0);
        output.positionCS = TransformWorldToHClip(GetCameraRelativePositionWS(position));
        output.uv = (drop.seed & 1u) == 0u ? float2(head, 1.0 - right) : float2(1.0 - head, right);
        float radius = max(_XRayRainAppearance.z, 0.001);
        float distanceToCamera = length(center.xz - _XRayRainCamera.xz);
        output.visibility = drop.size * (1.0 - smoothstep(radius * 0.8, radius, distanceToCamera));
        return output;
    }

    RainVaryings VertSplash(Attributes input)
    {
        RainVaryings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        RainDrop drop = _XRayRainDrops[input.instanceID];
        uint corner = QUAD_INDICES[input.vertexID];
        float2 uv = float2((corner & 1u) != 0u ? 1.0 : 0.0, corner >= 2u ? 1.0 : 0.0);
        float life = saturate(drop.splashAge / SPLASH_LIFETIME);
        float3 towardCamera = SafeNormalize(float3(_XRayRainCamera.x - drop.splashPosition.x, 0.0, _XRayRainCamera.z - drop.splashPosition.z));
        float3 right = float3(towardCamera.z, 0.0, -towardCamera.x);
        float size = _XRayRainShape.z * (0.5 + life * 0.5);
        float3 position = drop.splashPosition + right * (uv.x - 0.5) * size + float3(0.0, uv.y * size, 0.0);
        output.positionCS = TransformWorldToHClip(GetCameraRelativePositionWS(position));
        output.uv = uv;
        output.visibility = (1.0 - life) * step(0.5, _XRayRainAppearance.w);
        return output;
    }

    void ClipSceneDepth(RainVaryings input)
    {
        float cameraDepth = CustomPassLoadCameraDepth(uint2(input.positionCS.xy));
        #if UNITY_REVERSED_Z
        clip(input.positionCS.z - cameraDepth);
        #else
        clip(cameraDepth - input.positionCS.z);
        #endif
    }

    float4 FragRain(RainVaryings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        clip(input.visibility - 0.001);
        ClipSceneDepth(input);
        float alpha = SAMPLE_TEXTURE2D(_XRayRainTexture, s_linear_repeat_sampler, input.uv).a * _XRayRainShape.w * saturate(input.visibility);
        clip(alpha - 0.001);
        return float4(max(_XRayRainColor.rgb, 0.0) * GetCurrentExposureMultiplier(), saturate(alpha));
    }

    float4 FragSplash(RainVaryings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        clip(input.visibility - 0.001);
        ClipSceneDepth(input);
        float alpha = SAMPLE_TEXTURE2D(_XRaySplashTexture, s_linear_clamp_sampler, input.uv).a * input.visibility * 0.25;
        clip(alpha - 0.001);
        return float4(max(_XRayRainColor.rgb, 0.0) * GetCurrentExposureMultiplier(), saturate(alpha));
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Rain"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex VertRain
            #pragma fragment FragRain
            ENDHLSL
        }

        Pass
        {
            Name "Splash"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex VertSplash
            #pragma fragment FragSplash
            ENDHLSL
        }
    }
}
