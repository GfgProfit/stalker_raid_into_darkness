Shader "RaidIntoDarkness/Weapons/WorldCrosshair"
{
    Properties
    {
        _CrosshairColor("Color", Color) = (1, 1, 1, 1)
        _CrosshairDimensions("Dimensions", Vector) = (3, 6, 0.75, 24)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+100" }
        Pass
        {
            Name "WorldCrosshair"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CrosshairColor;
                float4 _CrosshairDimensions;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 p = abs((input.uv - 0.5) * _CrosshairDimensions.w);
                float gap = _CrosshairDimensions.x;
                float lineLength = _CrosshairDimensions.y;
                float width = _CrosshairDimensions.z;
                float horizontal = max(p.y - width, max(gap - p.x, p.x - gap - lineLength));
                float vertical = max(p.x - width, max(gap - p.y, p.y - gap - lineLength));
                float distance = min(horizontal, vertical);
                float smoothing = max(fwidth(distance) * 0.5, 0.0001);
                float coverage = 1.0 - smoothstep(-smoothing, smoothing, distance);
                return float4(_CrosshairColor.rgb, saturate(_CrosshairColor.a) * coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
