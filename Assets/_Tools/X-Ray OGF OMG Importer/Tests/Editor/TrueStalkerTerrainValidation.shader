Shader "Hidden/XRay/TrueStalkerTerrainValidation"
{
    Properties
    {
        _Base("Base", 2D) = "white" {}
        _Mask("Mask", 2D) = "white" {}
        _ColorR("Color R", 2D) = "white" {}
        _ColorG("Color G", 2D) = "white" {}
        _ColorB("Color B", 2D) = "white" {}
        _ColorA("Color A", 2D) = "white" {}
        _BumpR("Bump R", 2D) = "white" {}
        _BumpG("Bump G", 2D) = "white" {}
        _BumpB("Bump B", 2D) = "white" {}
        _BumpA("Bump A", 2D) = "white" {}
        _MaterialLut("Material", 3D) = "" {}
        _Env0("Environment 0", Cube) = "" {}
        _Env1("Environment 1", Cube) = "" {}
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
            #include "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/MonolithTrueStalkerTerrain.hlsl"
            Texture2D _Base;
            Texture2D _Mask;
            Texture2D _ColorR;
            Texture2D _ColorG;
            Texture2D _ColorB;
            Texture2D _ColorA;
            Texture2D _BumpR;
            Texture2D _BumpG;
            Texture2D _BumpB;
            Texture2D _BumpA;
            Texture3D _MaterialLut;
            TextureCube _Env0;
            TextureCube _Env1;
            SamplerState sampler_Base;
            SamplerState sampler_LinearClamp;
            float4 _Parameters;
            float4 _Uv;
            float4 _Tangent;
            float4 _Bitangent;
            float4 _Geometric;
            float4 _Position;
            float4 _LightDirection;
            float4 _LightPosition;
            float4 _EnvironmentColor;
            float4 _Ambient;
            float4x4 _ViewToWorld;
            float4 _RawT;
            float4 _RawB;
            float4 _RawN;
            float _Stage;
            float _Gamma22;
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
                float4 baseColor = _Base.Sample(sampler_Base, tc);
                float4 weights = MonolithTrueStalkerWeights(_Mask.Sample(sampler_Base, tc));
                float2 detailTc = tc * _Parameters.x;
                float4 r = _BumpR.Sample(sampler_Base, detailTc);
                float4 g = _BumpG.Sample(sampler_Base, detailTc);
                float4 b = _BumpB.Sample(sampler_Base, detailTc);
                float4 a = _BumpA.Sample(sampler_Base, detailTc);
                float3 color;
                float3 normal;
                float gloss;
                MonolithTrueStalkerBlend(baseColor.rgb, weights, _ColorR.Sample(sampler_Base, detailTc).rgb, _ColorG.Sample(sampler_Base, detailTc).rgb, _ColorB.Sample(sampler_Base, detailTc).rgb, _ColorA.Sample(sampler_Base, detailTc).rgb, r, g, b, a, color, normal, gloss);
                float3 n = MonolithTrueStalkerNormal(_Tangent.xyz, _Bitangent.xyz, _Geometric.xyz, normal);
                if (_Stage < 0.5) return weights;
                if (_Stage < 1.5) return float4(color, gloss);
                if (_Stage < 2.5) return float4(n, gloss);
                if (_Stage < 3.5) return float4(MonolithTrueStalkerLayerNormal(r), r.r);
                if (_Stage < 4.5) return float4(MonolithTrueStalkerLayerNormal(g), g.r);
                if (_Stage < 5.5) return float4(MonolithTrueStalkerLayerNormal(b), b.r);
                if (_Stage < 6.5) return float4(MonolithTrueStalkerLayerNormal(a), a.r);
                if (_Stage < 7.5) return float4(MonolithTrueStalkerPosition(_Position.xyz, n), 1.0);
                if (_Stage < 8.5) return float4(0.95, MonolithTrueStalkerQuantizeMaterial(0.95), (uint)(saturate(baseColor.a) * 255.9) / 254.8, 1.0);
                if (_Stage < 9.5) return float4(MonolithTrueStalkerPackNormal(n), 0.0, 1.0);
                float3 view = -normalize(_Position.xyz);
                if (_Stage < 10.5) return MonolithTrueStalkerDirect(_MaterialLut, sampler_LinearClamp, _Parameters.z, n, view, -_LightDirection.xyz, false, 1.0);
                float3 delta = _Position.xyz - _LightPosition.xyz;
                if (_Stage < 11.5) return MonolithTrueStalkerDirect(_MaterialLut, sampler_LinearClamp, _Parameters.z, n, view, -normalize(delta), true, saturate(1.0 - dot(delta, delta) * _LightPosition.w));
                float3 worldN = mul((float3x3)_ViewToWorld, n);
                float3 worldV = mul((float3x3)_ViewToWorld, view);
                float3 reflection = MonolithTrueStalkerReflection(worldN, worldV);
                float3 diffuse;
                float3 specular;
                MonolithTrueStalkerEnvironment(_MaterialLut, sampler_LinearClamp, _Parameters.z, baseColor.a, gloss, worldN, worldV, _Gamma22 > 0.5, _Env0.SampleLevel(sampler_LinearClamp, worldN, 0).rgb, _Env1.SampleLevel(sampler_LinearClamp, worldN, 0).rgb, _Env0.SampleLevel(sampler_LinearClamp, reflection, 0).rgb, _Env1.SampleLevel(sampler_LinearClamp, reflection, 0).rgb, _EnvironmentColor, _Ambient.rgb, diffuse, specular);
                if (_Stage < 12.5) return float4(diffuse, 1.0);
                if (_Stage < 13.5) return float4(specular, 1.0);
                if (_Stage < 14.5) return float4(reflection, 1.0);
                float3 tangent = mul((float3x3)_ViewToWorld, _RawT.bgr * 4.0 - 2.0);
                float3 bitangent = mul((float3x3)_ViewToWorld, _RawB.bgr * 4.0 - 2.0);
                float3 geometric = mul((float3x3)_ViewToWorld, _RawN.bgr * 4.0 - 2.0);
                return float4(MonolithTrueStalkerNormal(tangent, bitangent, geometric, normal), 1.0);
            }
            float4 Frag(Varyings input) : SV_Target
            {
                return Evaluate(input.uv * _Uv.xy + _Uv.zw);
            }
            ENDHLSL
        }
    }
}
