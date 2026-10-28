Shader "Hidden/XRay/TerrainQualityValidation"
{
    Properties
    {
        _Base("Base", 2D) = "white" {}
        _Mask("Mask", 2D) = "white" {}
        _Atlas("Atlas", 2D) = "white" {}
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/MonolithSourceTerrain.hlsl"
            Texture2D _Base;
            Texture2D _Mask;
            Texture2D _Atlas;
            SamplerState sampler_Base;
            SamplerState sampler_LinearRepeat;
            float4 _Options;
            float4 _UvScale;
            float4 _Normal;
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
            float4 Evaluate(float2 tc)
            {
                float3 color = _Base.Sample(sampler_Base, tc).rgb;
                float quality = MonolithTerrainQualityMode(_Options.x, _Options.y, _Options.z);
                if (quality < 0.5) return float4(quality, quality, quality, 1.0);
                if (quality < 1.5) color = MonolithTerrainMid(TEXTURE2D_ARGS(_Mask, sampler_Base), TEXTURE2D_ARGS(_Atlas, sampler_LinearRepeat), tc, color, _UvScale.w, 1.0, 1.0);
                return _Options.w > 0.5 ? float4(normalize(_Normal.xyz), _Base.Sample(sampler_Base, tc).a) : float4(color, 0.001);
            }
            float4 Frag(Varyings input) : SV_Target
            {
                return Evaluate(input.uv * _UvScale.xy + _UvScale.zz);
            }
            ENDHLSL
        }
    }
}
