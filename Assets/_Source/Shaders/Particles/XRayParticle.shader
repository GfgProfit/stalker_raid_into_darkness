Shader "RaidIntoDarkness/XRayParticle"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _FrameSize("Frame Size", Vector) = (1, 1, 0, 0)
        _FrameColumns("Frame Columns", Float) = 1
        [HideInInspector] _FrameLookup("Frame Lookup", 2D) = "black" {}
        [HideInInspector] _FrameLookupLength("Frame Lookup Length", Float) = 0
        [HideInInspector] _FrameLookupLifetime("Frame Lookup Lifetime", Float) = 1
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
            TEXTURE2D(_FrameLookup);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _FrameSize;
                float _FrameColumns;
                float _FrameLookupLength;
                float _FrameLookupLifetime;
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

            float ResolveFrame(float packedValue)
            {
                if (_FrameLookupLength == 0.0)
                {
                    return packedValue;
                }
                if (_FrameLookupLength < 0.0)
                {
                    return 0.0;
                }

                precise float remainingFraction = 1.0 - packedValue;
                precise float remaining = remainingFraction * _FrameLookupLifetime;
                precise float age = _FrameLookupLifetime - remaining;
                int index = 0;
                int limit = (int)_FrameLookupLength;
                [loop]
                for (int step = 0; step < 9 && index + 1 < limit; step++)
                {
                    int middle = (index + limit) >> 1;
                    float threshold = LOAD_TEXTURE2D(_FrameLookup, int2(middle, 0)).x;
                    if (age >= threshold)
                    {
                        index = middle;
                    }
                    else
                    {
                        limit = middle;
                    }
                }
                return LOAD_TEXTURE2D(_FrameLookup, int2(index, 0)).y;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                float frame = ResolveFrame(input.uv.z);
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
