Shader "Hidden/XRay/Precipitation Height"
{
    HLSLINCLUDE
    #pragma target 4.5
    #pragma editor_sync_compilation
    #include "UnityCG.cginc"
    sampler2D _XRayRainRoofTexture;
    float4 _XRayRainRoofTexture_ST;
    float4 _XRayRainWorldMapping;
    float _XRayRainRoofCutoff;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float height : TEXCOORD1;
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        float3 position = mul(unity_ObjectToWorld, input.positionOS).xyz;
        float2 uv = (position.xz - _XRayRainWorldMapping.xy) * _XRayRainWorldMapping.zw;
        output.positionCS = float4(uv.x * 2.0 - 1.0, (1.0 - uv.y) * 2.0 - 1.0, 0.5, 1.0);
        output.uv = input.uv * _XRayRainRoofTexture_ST.xy + _XRayRainRoofTexture_ST.zw;
        output.height = position.y;
        return output;
    }

    float4 Frag(Varyings input) : SV_Target
    {
        clip(tex2D(_XRayRainRoofTexture, input.uv).a - _XRayRainRoofCutoff);
        return input.height.xxxx;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Pass
        {
            ColorMask R
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            ColorMask G
            Blend One One
            BlendOp Min
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
