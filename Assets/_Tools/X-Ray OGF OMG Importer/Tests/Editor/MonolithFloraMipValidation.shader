Shader "Hidden/XRay/MonolithFloraMipValidation"
{
    Properties
    {
        _Texture("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            Texture2D _Texture;
            SamplerState sampler_Texture;
            float4 _Uv;
            float _Mode;
            float _Mip;
            float _Cutoff;
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(id);
                output.uv = GetFullScreenTriangleTexCoord(id);
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv * _Uv.xy + _Uv.zw;
                float4 value = _Mode < 0.5 ? _Texture.SampleLevel(sampler_Texture, uv, _Mip) : _Texture.Sample(sampler_Texture, uv);
                float lod = _Texture.CalculateLevelOfDetail(sampler_Texture, uv);
                return _Mode < 1.5 ? value : float4(value.a - _Cutoff >= 0.0 ? 1.0 : 0.0, lod, value.a, 1.0);
            }
            ENDHLSL
        }
    }
}
