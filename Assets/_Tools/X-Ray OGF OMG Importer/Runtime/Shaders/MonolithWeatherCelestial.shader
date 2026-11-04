Shader "XRay/Monolith Weather Celestial"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma editor_sync_compilation
    #pragma only_renderers d3d11 vulkan metal

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassSampling.hlsl"

    TEXTURE2D_X(_XRayCelestialInput);
    TEXTURE2D_X(_XRayCelestialSceneColor);
    TEXTURE2D(_XRayCelestialSprite);
    float4 _XRayCelestialSource;
    float4 _XRayCelestialOcclusion;
    float4 _XRayCelestialViewport;
    float4 _XRayCelestialColor;
    float4 _XRayCelestialInputScale;
    float4 _XRayCelestialSceneScale;
    float4 _XRayCelestialSpriteRect;
    float4 _XRayCelestialSpriteFade;
    float _XRayCelestialBlurSize;
    float _XRayCelestialGain;
    float _XRayCelestialSpriteContribution;
    int _XRayCelestialSpriteMode;

    static const int SHAFTS_SAMPLES = 9;
    static const uint QUAD_INDICES[6] = { 0, 1, 2, 2, 1, 3 };

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    struct SpriteVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 color : TEXCOORD1;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings VertScreen(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    float CameraDepth(float2 uv)
    {
        float2 pixel = clamp((uv + _XRayCelestialOcclusion.xy) * _ScreenSize.xy, 0.0, _ScreenSize.xy - 1.0);
        return CustomPassLoadCameraDepth(uint2(pixel));
    }

    float Sky(float2 uv)
    {
        return CameraDepth(uv) == UNITY_RAW_FAR_CLIP_VALUE ? 1.0 : 0.0;
    }

    float SourceDepth(float2 uv)
    {
        float depth = CameraDepth(uv);
        if (depth == UNITY_RAW_FAR_CLIP_VALUE) return 0.0;
        float eyeDepth = LinearEyeDepth(depth, _ZBufferParams);
        if (unity_OrthoParams.w > 0.5)
        {
            #if UNITY_REVERSED_Z
            depth = 1.0 - depth;
            #endif
            eyeDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, depth);
        }
        return saturate(eyeDepth / 100.0) * step(0.001, eyeDepth);
    }

    float SampleMask(float2 uv)
    {
        float2 sampleUv = clamp(uv * _XRayCelestialInputScale.xy, _XRayCelestialInputScale.zw, _XRayCelestialInputScale.xy - _XRayCelestialInputScale.zw);
        return SAMPLE_TEXTURE2D_X_LOD(_XRayCelestialInput, s_linear_clamp_sampler, sampleUv, 0.0).r;
    }

    float4 FragMask(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 pixel = _XRayCelestialViewport.zw;
        float depth = SourceDepth(input.uv + float2(0.0, 1.0) * pixel);
        depth += SourceDepth(input.uv + float2(1.0, 0.65) * pixel);
        depth += SourceDepth(input.uv + float2(-1.0, 0.65) * pixel);
        return saturate(1.0 - depth * 333.0);
    }

    float4 FragBlur(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 toSource = _XRayCelestialSource.xy - input.uv;
        float fade = saturate(_XRayCelestialSource.z) * saturate(1.0 - length(toSource * float2(1.0, _XRayCelestialSource.w)));
        float2 stepUv = toSource * (_XRayCelestialSource.z * _XRayCelestialBlurSize * 0.2);
        float accumulated = SampleMask(input.uv);
        [unroll]
        for (int index = 1; index < SHAFTS_SAMPLES; index++)
        {
            accumulated += SampleMask(input.uv + stepUv * index) * (1.0 - (float)index / SHAFTS_SAMPLES);
        }
        return accumulated * (2.0 / SHAFTS_SAMPLES) * fade;
    }

    float4 FragShafts(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 toSource = _XRayCelestialSource.xy - input.uv;
        float2 axis = toSource.yx * rsqrt(max(dot(toSource, toSource), 0.00000001)) * _XRayCelestialViewport.zw;
        float rays = 0.5 * SampleMask(input.uv) + 0.25 * (SampleMask(input.uv + axis) + SampleMask(input.uv - axis));
        if (rays <= 0.0) return 0.0;
        float2 sceneUv = clamp(input.uv * _XRayCelestialSceneScale.xy, _XRayCelestialSceneScale.zw, _XRayCelestialSceneScale.xy - _XRayCelestialSceneScale.zw);
        float3 scene = saturate(SAMPLE_TEXTURE2D_X_LOD(_XRayCelestialSceneColor, s_point_clamp_sampler, sceneUv, 0.0).rgb);
        float3 encoded = pow(scene, 1.0 / 2.2);
        float3 withShafts = saturate(encoded + rays * _XRayCelestialColor.rgb * (1.0 - encoded) * _XRayCelestialGain);
        return float4(max(0.0, pow(withShafts, 2.2) - scene), 0.0);
    }

    float FlareVisibility()
    {
        float2 uv = _XRayCelestialSource.xy;
        float2 spread = _XRayCelestialOcclusion.zw;
        float visibility = Sky(uv);
        visibility += Sky(uv + float2(spread.x, 0.0));
        visibility += Sky(uv - float2(spread.x, 0.0));
        visibility += Sky(uv + float2(0.0, spread.y));
        visibility += Sky(uv - float2(0.0, spread.y));
        return visibility * 0.2;
    }

    float FragGeometryVisibility(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return FlareVisibility();
    }

    SpriteVaryings VertSprite(Attributes input)
    {
        SpriteVaryings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        uint corner = QUAD_INDICES[input.vertexID];
        float2 coordinates = float2(corner & 1u, corner >> 1);
        float2 signedCorner = coordinates * 2.0 - 1.0;
        float2 halfSize = _XRayCelestialSpriteRect.zw;
        float colorFactor = 1.0;
        float alphaFactor = _XRayCelestialSpriteFade.x;
        if (_XRayCelestialSpriteMode != 0)
        {
            float visibility = FlareVisibility() * _XRayCelestialSpriteFade.x * _XRayCelestialSpriteFade.y;
            if (_XRayCelestialSpriteMode == 2)
            {
                float2 edge = saturate((2.5 - abs(_XRayCelestialSource.xy * 2.0 - 1.0)) * 0.5);
                visibility *= edge.x * edge.y;
                halfSize *= visibility;
                visibility *= _XRayCelestialSpriteFade.x;
            }
            else if (_XRayCelestialSpriteMode == 1)
            {
                float2 axis = (_XRayCelestialSource.xy - 0.5) / max(halfSize, 0.00000001);
                float lengthSquared = dot(axis, axis);
                axis = lengthSquared > 0.00000001 ? axis * rsqrt(lengthSquared) : float2(1.0, 0.0);
                signedCorner = axis * signedCorner.x + float2(-axis.y, axis.x) * signedCorner.y;
            }
            colorFactor = visibility;
            alphaFactor = visibility;
        }
        float2 clipPosition = (_XRayCelestialSpriteRect.xy + signedCorner * halfSize) * 2.0 - 1.0;
        #if UNITY_UV_STARTS_AT_TOP
        clipPosition.y = -clipPosition.y;
        #endif
        output.positionCS = float4(clipPosition, UNITY_RAW_FAR_CLIP_VALUE, 1.0);
        output.uv = float2(coordinates.x, 1.0 - coordinates.y);
        output.color = float4(_XRayCelestialColor.rgb * colorFactor, alphaFactor);
        return output;
    }

    float4 FragSprite(SpriteVaryings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        if (_XRayCelestialSpriteMode == 3)
        {
            float2 local = input.uv * 2.0 - 1.0;
            float2 offset = local * float2(_XRayCelestialViewport.x * _XRayCelestialViewport.w, 1.0);
            float radius = length(offset);
            float angle = radius > 0.00001 ? atan2(offset.y, offset.x) : 0.0;
            float angular = pow(abs(cos(angle * 7.0 + 0.28)), 24.0) * (0.72 + 0.28 * cos(angle * 5.0 + 0.25));
            float rays = angular * exp2(-radius * 3.1) * saturate(1.0 - radius) * smoothstep(0.0, 0.05, radius);
            float streak = exp2(-abs(offset.y) * 155.0 - abs(offset.x) * 2.4) * saturate(1.0 - abs(local.x));
            float halo = exp2(-radius * 20.0) * saturate(1.0 - radius);
            float3 color = saturate(input.color.rgb) * float3(1.0, 0.82, 0.58);
            return float4(color * (0.85 * rays + 0.36 * streak + 0.20 * halo) * _XRayCelestialSpriteContribution, 0.0);
        }
        if (_XRayCelestialSpriteMode == 0) clip(Sky(input.positionCS.xy * _XRayCelestialViewport.zw) - 0.5);
        float4 sprite = SAMPLE_TEXTURE2D(_XRayCelestialSprite, s_linear_clamp_sampler, input.uv) * input.color;
        if (_XRayCelestialSpriteMode == 1 && _XRayCelestialSpriteFade.z > 0.5) return float4(min(saturate(sprite.rgb * sprite.a) * 1.5, 0.28) * _XRayCelestialSpriteContribution, 0.0);
        return float4(pow(saturate(sprite.rgb * sprite.a), 2.2) * _XRayCelestialSpriteContribution, 0.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Shafts Sky Mask"

            HLSLPROGRAM
            #pragma vertex VertScreen
            #pragma fragment FragMask
            ENDHLSL
        }

        Pass
        {
            Name "Shafts Radial Blur"

            HLSLPROGRAM
            #pragma vertex VertScreen
            #pragma fragment FragBlur
            ENDHLSL
        }

        Pass
        {
            Name "Shafts Composite"
            Blend One One, Zero One

            HLSLPROGRAM
            #pragma vertex VertScreen
            #pragma fragment FragShafts
            ENDHLSL
        }

        Pass
        {
            Name "Flare Sprite"
            Blend One One, Zero One

            HLSLPROGRAM
            #pragma vertex VertSprite
            #pragma fragment FragSprite
            ENDHLSL
        }

        Pass
        {
            Name "Flare Geometry Visibility"

            HLSLPROGRAM
            #pragma vertex VertScreen
            #pragma fragment FragGeometryVisibility
            ENDHLSL
        }
    }

    Fallback Off
}
