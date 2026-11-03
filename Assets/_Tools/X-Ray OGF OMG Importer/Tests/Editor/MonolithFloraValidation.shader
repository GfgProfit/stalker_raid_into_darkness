Shader "Hidden/XRay/MonolithFloraValidation"
{
    Properties
    {
        _Base("Base", 2D) = "white" {}
        _Bump("Bump", 2D) = "white" {}
        _BumpX("Bump Correction", 2D) = "grey" {}
        _Waves("Waves", 2D) = "grey" {}
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
            Texture2D _Base;
            Texture2D _Bump;
            Texture2D _BumpX;
            Texture2D _Waves;
            SamplerState sampler_Base;
            SamplerState sampler_Waves;
            float4 _Uv;
            float _Stage;
            float4 _Rain;
            float4 _Fixes1;
            float4 _Fixes2;
            float4 _Variation;
            float _VariationEnabled;
            float4 _Extra;
            float4x4 _Xform;
            float4 _Vertex;
            float4 _VertexData;
            float4 _HemiScaleBias;
            float4 _WindParams;
            float4 _WindGrass;
            float4 _WindTrees;
            float4 _WindAnim;
            float4 _WindAnimPrev;
            float4 _Wave;
            float4 _Wind;
            float4 _Consts;
            float4 _Terrain;
            float4 _Position;
            float4 _ScreenRes;
            float4 _LightDirection;
            float4 _LightColor;
            float4 _SunColor;
            float4 _LightSetup;
            float _GrassAlign;
            #include "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/MonolithMaterial.hlsl"
            #include "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/MonolithFlora.hlsl"
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
            float3 TreeWorld()
            {
                return mul(_Xform, float4(_Vertex.xyz, 1.0)).xyz;
            }
            float4 Evaluate(float2 tc)
            {
                float4 base = _Base.Sample(sampler_Base, tc);
                float4 bump = _Bump.Sample(sampler_Base, tc);
                float4 correction = _BumpX.Sample(sampler_Base, tc);
                float wetness = _Rain.y;
                if (_Stage > 21.5) return float4(MonolithFloraOptionalColorVariation(base.rgb, _Extra.x, _Variation.z, _VariationEnabled), base.a);
                if (_Stage < 0.5) return float4(MonolithAnomalyFloraNormal(bump, wetness), MonolithAnomalyFloraGloss(_Fixes1, wetness, false));
                if (_Stage < 1.5) return float4(MonolithFloraColorVariation(base.rgb, _Extra.x, _Variation.z), base.a - MONOLITH_FLORA_AREF >= 0.0 ? 1.0 : 0.0);
                if (_Stage < 2.5) return float4(MonolithFloraMaterial(), MonolithFloraPackedHemisphere(_Position.w), 0.15, _Position.w);
                MonolithWindSetup setup = MonolithSsfxWindSetup(_WindParams, _WindGrass, _WindTrees);
                float3 world = TreeWorld();
                float baseHeight = _Xform._24;
                float treeHeight = world.y - baseHeight;
                float2 uv = float2(_VertexData.y, _VertexData.z);
                if (_Stage < 3.5) return float4(world + MonolithSsfxWindBranches(TEXTURE2D_ARGS(_Waves, sampler_Waves), world, baseHeight, treeHeight, uv.y, _WindAnim, setup), MonolithFloraTreeHemisphere(_VertexData.x, _HemiScaleBias.x, _HemiScaleBias.y));
                if (_Stage < 4.5) return float4(world + MonolithSsfxWindBranches(TEXTURE2D_ARGS(_Waves, sampler_Waves), world, baseHeight, treeHeight, uv.y, _WindAnimPrev, setup), MonolithAnomalyTreeVariation(_Xform._14_24_34, _Xform._11, _Variation.w));
                if (_Stage < 5.5)
                {
                    float2 trunk = MonolithSsfxWindTrunk(baseHeight, treeHeight, _WindAnim, setup).xy;
                    return float4(world.x + trunk.x, world.y, world.z + trunk.y, MonolithFloraTreeHemisphere(_VertexData.x, _HemiScaleBias.x, _HemiScaleBias.y));
                }
                if (_Stage < 6.5)
                {
                    float2 trunk = MonolithSsfxWindTrunk(baseHeight, treeHeight, _WindAnimPrev, setup).xy;
                    return float4(world.x + trunk.x, world.y, world.z + trunk.y, 1.0);
                }
                if (_Stage < 7.5) return float4(MonolithAnomalyGrassNormal(bump, _Terrain.xyz, wetness), MonolithAnomalyFloraGloss(_Fixes1, wetness, true));
                if (_Stage < 8.5)
                {
                    float2 pixel = (_Position.xy * float2(0.5, -0.5) + 0.5) * _ScreenRes.xy;
                    bool accepted = base.a - MONOLITH_FLORA_AREF >= 0.0 && _Terrain.w - MonolithAnomalyGrassDither(pixel) >= 0.0;
                    return float4(MonolithFloraColorVariation(base.rgb, _Extra.x, _Variation.x), accepted ? 1.0 : 0.0);
                }
                if (_Stage < 9.5) return float4(MonolithFloraMaterial(), MonolithFloraPackedHemisphere(_Position.w), 0.15, _Position.w);
                float3 grass = world;
                float grassHeight = grass.y - _Xform._24;
                grass.xz = grass.xz - 0.5 * _Terrain.xz * grassHeight * _GrassAlign;
                if (_Stage < 10.5) return float4(grass + MonolithSsfxWindGrass(TEXTURE2D_ARGS(_Waves, sampler_Waves), grass, grassHeight, _WindAnim, setup), MonolithFloraGrassHemisphere(_HemiScaleBias.w));
                if (_Stage < 11.5) return float4(grass + MonolithSsfxWindGrass(TEXTURE2D_ARGS(_Waves, sampler_Waves), grass, grassHeight, _WindAnimPrev, setup), MonolithAnomalyGrassVariation(grass, _Xform._11, _Variation.y));
                if (_Stage < 12.5) return float4(MonolithTrueStalkerFloraNormal(bump, correction, wetness, 0.02), MonolithTrueStalkerFloraGloss(bump));
                if (_Stage < 13.5) return float4(base.rgb, base.a - MONOLITH_FLORA_AREF >= 0.0 ? 1.0 : 0.0);
                if (_Stage < 14.5) return float4(MonolithFloraMaterial(), MonolithFloraPackedHemisphere(_Position.w), 0.15, _Position.w);
                float rigidity = _VertexData.w * _Consts.x;
                if (_Stage < 15.5) return float4(world + float3(MonolithXRayTreeWind(world, baseHeight, _Wave, _Wind, rigidity, 2.0), 0.0).xzy, MonolithFloraTreeHemisphere(_VertexData.x, _HemiScaleBias.x, _HemiScaleBias.y));
                if (_Stage < 16.5) return float4(world + float3(MonolithXRayTreeWind(world, baseHeight, _Wave, _Wind, rigidity, 1.0), 0.0).xzy, _VertexData.x * _HemiScaleBias.x + _HemiScaleBias.y);
                if (_Stage < 17.5) return float4(MonolithTrueStalkerFloraNormal(bump, correction, wetness, 0.1), 2.0 / 255.0);
                if (_Stage < 18.5) return float4(base.rgb, base.a - MONOLITH_FLORA_GRASS_AREF >= 0.0 ? 1.0 : 0.0);
                if (_Stage < 19.5) return float4(MonolithFloraMaterial(), MonolithFloraPackedHemisphere(_Position.w), 0.15, _Position.w);
                if (_Stage < 20.5) return float4(world + float3(MonolithXRayGrassWind(world, _Xform._24, _Wave, _Wind, rigidity), 0.0).xzy, MonolithFloraGrassHemisphere(_HemiScaleBias.w));
                float3 normal = MonolithAnomalyFloraNormal(bump, wetness);
                float3 view = -normalize(_Position.xyz);
                float3 light = -normalize(_LightDirection.xyz);
                float3 encoded = MonolithFloraColorVariation(base.rgb, _Extra.x, _Variation.z);
                float gloss = MonolithAnomalyFloraGloss(_Fixes1, wetness, false);
                float rawMaterial = MonolithFloraMaterial();
                float material = MonolithSourceMaterial(rawMaterial);
                float3 diffuse;
                float3 specular;
                MonolithSourceDirect(normal, view, light, encoded, gloss, rawMaterial, _LightColor.w, _LightColor.rgb, _LightSetup, _SunColor.rgb, _Fixes2, 0.0, diffuse, specular);
                return float4(diffuse * MonolithAlbedo(encoded, MonolithMetalness(gloss, material)) + specular, 1.0);
            }
            float4 Frag(Varyings input) : SV_Target
            {
                return Evaluate(input.uv * _Uv.xy + _Uv.zw);
            }
            ENDHLSL
        }
    }
}
