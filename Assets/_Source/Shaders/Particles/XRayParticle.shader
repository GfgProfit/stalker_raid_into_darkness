Shader "RaidIntoDarkness/XRayParticle"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _FrameSize("Frame Size", Vector) = (1, 1, 0, 0)
        _FrameColumns("Frame Columns", Float) = 1
        _Intensity("Intensity", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "XRayParticle"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _FrameSize;
                float _FrameColumns;
                float _Intensity;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                float frame = input.uv.z;
                float columns = max(1.0, _FrameColumns);
                float2 origin = float2(fmod(frame, columns), floor(frame / columns)) * _FrameSize.xy;
                float2 xray = origin + float2(input.uv.x, 1.0 - input.uv.y) * _FrameSize.xy;
                output.uv = float2(xray.x, 1.0 - xray.y);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float4 color = texel * input.color;
                color.rgb *= _Intensity;
                return color;
            }
            ENDHLSL
        }
    }
}
