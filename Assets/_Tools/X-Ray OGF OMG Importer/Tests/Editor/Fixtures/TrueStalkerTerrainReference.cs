using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Tests
{
    public static class TrueStalkerTerrainReference
    {
        private const string ROOT = "E:/True Stalker Unpacked Patch 1.5/shaders/r3/";
        private const string TEMPLATE = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/TrueStalkerTerrainValidation.shader";

        public static Shader Create(bool gamma22)
        {
            string template = File.ReadAllText(TEMPLATE);
            string common = Strip(File.ReadAllText(ROOT + "common_functions.h"));
            string ps = WithoutIncludes(Strip(File.ReadAllText(ROOT + "deffer_impl_flat.ps")));
            string vs = WithoutIncludes(Strip(File.ReadAllText(ROOT + "deffer_base_bump.vs")));
            string lighting = WithoutIncludes(Strip(File.ReadAllText(ROOT + "lmodel.h")));
            string environment = WithoutIncludes(Strip(File.ReadAllText(ROOT + "hmodel.h")));
            environment = Regex.Replace(environment, @"TextureCube\s+\w+\s*;|uniform\s+float4\s+env_color\s*;|uniform\s+float3x4\s+m_v2w\s*;", string.Empty);
            var source = new StringBuilder();
            source.AppendLine("#define USE_TDETAIL");
            if (gamma22) source.AppendLine("#define USE_GAMMA_22");
            foreach (string name in new[] { "common_defines.h", "settings_screenspace_PUDDLES.h", "settings_screenspace_SSR.h" }) source.AppendLine(WithoutIncludes(Strip(File.ReadAllText(ROOT + name))));
            source.AppendLine("#define s_base _Base\n#define s_mask _Mask\n#define s_dt_r _ColorR\n#define s_dt_g _ColorG\n#define s_dt_b _ColorB\n#define s_dt_a _ColorA\n#define s_dn_r _BumpR\n#define s_dn_g _BumpG\n#define s_dn_b _BumpB\n#define s_dn_a _BumpA\n#define s_material _MaterialLut\n#define env_s0 _Env0\n#define env_s1 _Env1\n#define smp_base sampler_Base\n#define smp_material sampler_LinearClamp\n#define smp_rtlinear sampler_LinearClamp\n#define env_color _EnvironmentColor\n#define L_ambient _Ambient\n#define m_v2w ((float3x3)_ViewToWorld)\n#define m_WV _ViewToWorld\n#define m_WVP _ViewToWorld\n#define m_inv_V ((float3x3)_ViewToWorld)\n#define m_V ((float3x3)_ViewToWorld)\n#define dt_params (_Parameters.xx)\n#define FXVS\n#define xmaterial 0.0\n#define L_sun_color float4(1,1,1,1)\n#define ssfx_florafixes_2 float4(0,0,0,0)\nfloat4 rain_params = 0.0;\nfloat4 timers = 0.0;\nTexture2D s_lmap;");
            source.AppendLine("struct p_bumped { float2 tcdh; float2 tcdbump; float4 position; float3 M1; float3 M2; float3 M3; };\nstruct p_flat { float2 tcdh; float2 tcdbump; float4 position; float3 N; };\nstruct f_deffer { float4 position; float4 Ne; float4 C; };\nstruct v_static { float4 P; float4 Nh; float4 T; float4 B; float2 tc; float2 lmh; float4 color; };\nstruct v2p_bumped { float4 hpos; float4 tcdh; float4 position; float2 tcdbump; float2 lmh; float3 M1; float3 M2; float3 M3; };");
            source.AppendLine(Function(Strip(File.ReadAllText(ROOT + "sload.h")), "tbase"));
            foreach (string function in new[] { "unpack_tc_base", "unpack_bx4", "unpack_D3DCOLOR", "gbuf_pack_normal", "gbuf_pack_hemi_mtl", "gbuf_unpack_hemi", "gbuf_unpack_mtl" }) source.AppendLine(Function(common, function));
            source.AppendLine("#define MUST_BE_SET uint(0x40000000)\n#define USABLE_BIT_13 uint(0x02000000)\n#define USABLE_BIT_14 uint(0x04000000)\n#define USABLE_BIT_15 uint(0x80000000)");
            string bits = source.ToString();
            int functionStart = bits.IndexOf("float gbuf_pack_hemi_mtl", StringComparison.Ordinal);
            string definitions = "#define MUST_BE_SET uint(0x40000000)\n#define USABLE_BIT_13 uint(0x02000000)\n#define USABLE_BIT_14 uint(0x04000000)\n#define USABLE_BIT_15 uint(0x80000000)\n";
            bits = bits.Insert(functionStart, definitions);
            source.Clear();
            source.AppendLine(bits);
            source.AppendLine("f_deffer pack_gbuffer(float4 n, float4 p, float4 c) { f_deffer o; o.position=p; o.Ne=n; o.C=c; return o; }");
            string projections = @"
if (_Stage < 0.5) O.C = mask;
else if (_Stage < 1.5) O.C = float4(D.rgb, G);
else if (_Stage < 2.5) O.C = float4(Ne.xyz, G);
else if (_Stage < 3.5) O.C = float4(n_Rt.xyz - 0.5, n_Rt.w);
else if (_Stage < 4.5) O.C = float4(n_Gt.xyz - 0.5, n_Gt.w);
else if (_Stage < 5.5) O.C = float4(n_Bt.xyz - 0.5, n_Bt.w);
else if (_Stage < 6.5) O.C = float4(n_At.xyz - 0.5, n_At.w);
else if (_Stage < 7.5) O.C = float4(O.position.xyz, 1.0);
else if (_Stage < 8.5) O.C = float4(MAT_TERRAIN, gbuf_unpack_mtl(gbuf_pack_hemi_mtl(D.w, MAT_TERRAIN)), gbuf_unpack_hemi(gbuf_pack_hemi_mtl(D.w, MAT_TERRAIN)), 1.0);
else if (_Stage < 9.5) O.C = float4(gbuf_pack_normal(Ne.xyz), 0.0, 1.0);
return O;";
            Assert.AreEqual(1, Regex.Matches(ps, @"return\s+O\s*;").Count);
            ps = Regex.Replace(ps, @"return\s+O\s*;", projections);
            ps = Regex.Replace(ps, @"\bmain\s*\(", "SourceTerrainMain(");
            vs = Regex.Replace(vs, @"\bmain\s*\(", "SourceTerrainVertex(");
            source.AppendLine(vs);
            source.AppendLine(ps);
            source.AppendLine(lighting);
            source.AppendLine(environment);
            source.AppendLine(@"
float4 Evaluate(float2 tc)
{
    p_bumped input;
    input.tcdh = tc;
    input.tcdbump = tc * _Parameters.x;
    input.position = _Position;
    input.M1 = float3(_Tangent.x, _Bitangent.x, _Geometric.x);
    input.M2 = float3(_Tangent.y, _Bitangent.y, _Geometric.y);
    input.M3 = float3(_Tangent.z, _Bitangent.z, _Geometric.z);
    f_deffer output = SourceTerrainMain(input);
    if (_Stage < 9.5) return output.C;
    float3 normal = output.Ne.xyz;
    if (_Stage < 10.5) return plight_infinity(_Parameters.z, _Position.xyz, normal, output.C, _LightDirection.xyz);
    if (_Stage < 11.5)
    {
        float rsqr;
        return plight_local(_Parameters.z, _Position.xyz, normal, _LightPosition.xyz, _LightPosition.w, rsqr);
    }
    float3 diffuse;
    float3 specular;
    hmodel(diffuse, specular, _Parameters.z, _Base.Sample(sampler_Base, tc).a, output.C.a, _Position.xyz, normal);
    if (_Stage < 12.5) return float4(diffuse, 1.0);
    if (_Stage < 13.5) return float4(specular, 1.0);
    if (_Stage < 14.5)
    {
        float3 nw = mul(m_v2w, normal);
        float3 v2Pnt = mul(m_v2w, normalize(_Position.xyz));
        float3 reflected = reflect(v2Pnt, nw);
        reflected.y = reflected.y * 2.0 - 1.0;
        return float4(reflected, 1.0);
    }
    v_static vertex;
    vertex.P = float4(0.0, 0.0, 0.0, 1.0);
    vertex.Nh = _RawN;
    vertex.T = _RawT;
    vertex.B = _RawB;
    vertex.tc = tc;
    vertex.lmh = 0.0;
    vertex.color = 0.0;
    v2p_bumped transformed = SourceTerrainVertex(vertex);
    input.M1 = transformed.M1;
    input.M2 = transformed.M2;
    input.M3 = transformed.M3;
    f_deffer vertexOutput = SourceTerrainMain(input);
    return float4(vertexOutput.Ne.xyz, 1.0);
}");
            string body = source.ToString();
            int start = template.IndexOf("            float4 Evaluate(", StringComparison.Ordinal);
            int end = template.IndexOf("            float4 Frag(", start, StringComparison.Ordinal);
            string reference = template.Substring(0, start) + body + template.Substring(end);
            reference = reference.Replace("#include \"Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/MonolithTrueStalkerTerrain.hlsl\"", string.Empty);
            reference = reference.Replace("Hidden/XRay/TrueStalkerTerrainValidation", "Hidden/XRay/TrueStalkerTerrainReference" + (gamma22 ? "Gamma" : "Linear"));
            string outputPath = Output();
            File.WriteAllText(Path.Combine(outputPath, "Original_TS_Reference_" + (gamma22 ? "Gamma" : "Linear") + ".shader"), reference);
            Shader shader = ShaderUtil.CreateShaderAsset(reference);
            Assert.IsNotNull(shader);
            return shader;
        }

        public static string Function(string source, string name)
        {
            Match match = Regex.Match(source, @"\b(?:float[234]?|void)\s+" + Regex.Escape(name) + @"\s*\([^;]*?\)\s*\{");
            Assert.IsTrue(match.Success, name);
            int start = source.IndexOf('{', match.Index);
            int depth = 1;
            int end = start + 1;
            while (depth > 0 && end < source.Length)
            {
                if (source[end] == '{') depth++;
                else if (source[end] == '}') depth--;
                end++;
            }
            Assert.AreEqual(0, depth);
            return source.Substring(match.Index, end - match.Index);
        }

        public static string Output()
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE05D_OUTPUT");
            Assert.IsNotEmpty(output);
            Assert.IsTrue(Path.GetFullPath(output).StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(output);
            return output;
        }

        private static string Strip(string source)
        {
            return Regex.Replace(source, @"/\*.*?\*/|//[^\r\n]*", string.Empty, RegexOptions.Singleline);
        }

        private static string WithoutIncludes(string source)
        {
            return Regex.Replace(source, @"^\s*#\s*include[^\r\n]*", string.Empty, RegexOptions.Multiline);
        }
    }
}
