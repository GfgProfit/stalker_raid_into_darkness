using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace XRay.Importer.Tests
{
    public static class MonolithFloraReferenceSource
    {
        public const string TEMPLATE = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/MonolithFloraValidation.shader";
        public const int GROUP_COUNT = 11;
        private const string SSS = "D:/GRA2.5/MO2/mods/[Шейдеры] ScreenSpaceShaders 23.5/gamedata/shaders/r3/";
        private const string ES = "D:/GRA2.5/MO2/mods/[Частицы_в2 и 12] Enh_Shaders_Color_G 1.10/gamedata/shaders/r3/";
        private const string ATMOSPHERICS = "D:/GRA2.5/MO2/mods/[Погода] Atmospherics 2.68.93_h3/gamedata/shaders/r3/";
        private const string ANOMALY_BASE = "D:/GRA2.5/Anomaly 1.5.3 up/tools/_unpacked/shaders/r3/";
        private const string TS = "E:/True Stalker Unpacked Patch 1.5/shaders/r3/";
        private static readonly string[] MAPPED = { "rain_params", "ssfx_florafixes_1", "ssfx_florafixes_2", "ssfx_floravariation", "c_scale", "c_bias", "wind", "wave", "dir2D", "consts", "wind_params", "ssfx_wsetup_grass", "ssfx_wsetup_trees", "ssfx_wind_anim", "ssfx_wind_anim_prev", "L_sun_color", "Ldynamic_color", "ssfx_lightsetup_1", "s_waves", "smp_linear2", "m_xform", "m_xform_v", "screen_res", "grass_align" };
        private const string DEFINES = "#define rain_params _Rain\n#define ssfx_florafixes_1 _Fixes1\n#define ssfx_florafixes_2 _Fixes2\n#define ssfx_floravariation _Variation\n#define c_scale float4(0.0, 0.0, 0.0, _HemiScaleBias.x)\n#define c_bias float4(0.0, 0.0, 0.0, _HemiScaleBias.y)\n#define wind _Wind\n#define wave _Wave\n#define dir2D _Wind\n#define consts _Consts\n#define wind_params _WindParams\n#define ssfx_wsetup_grass _WindGrass\n#define ssfx_wsetup_trees _WindTrees\n#define ssfx_wind_anim _WindAnim\n#define ssfx_wind_anim_prev _WindAnimPrev\n#define L_sun_color _SunColor\n#define Ldynamic_color _LightColor\n#define ssfx_lightsetup_1 _LightSetup\n#define s_waves _Waves\n#define smp_linear2 sampler_Waves\n#define s_base _Base\n#define s_bump _Bump\n#define s_bumpX _BumpX\n#define smp_base sampler_Base\n#define m_xform ((float3x4)_Xform)\n#define m_xform_v ((float3x4)_Xform)\n#define screen_res _ScreenRes\n#define grass_align _GrassAlign\n#define m_WV float3x3(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0)\n#define m_V float4x4(1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0)\n#define m_VP m_V\n#define m_vp_prev m_V\n#define m_P m_V\n#define xmaterial 0.0\n#define FXVS\n";
        private const string STRUCTURES = "struct p_bumped { float2 tcdh; float4 position; float3 M1; float3 M2; float3 M3; float4 RDrops; float2 tcdbump; float2 lmh; float4 vel_curr; float4 vel_prev; float4 sss_extra; };\nstruct v2p_bumped { float4 tcdh; float4 position; float3 M1; float3 M2; float3 M3; float4 RDrops; float2 tcdbump; float2 lmh; float4 vel_curr; float4 vel_prev; float4 sss_extra; float4 hpos; };\nstruct v_tree { float4 P; float4 Nh; float3 T; float3 B; int4 tc; };\nstruct v_detail { float4 pos; int4 misc; };\nstruct f_deffer { float4 Ne; float4 P; float4 C; float4 velocity; };\nf_deffer pack_gbuffer(float4 n, float4 p, float4 c) { f_deffer o; o.Ne = n; o.P = p; o.C = c; o.velocity = 0.0; return o; }\nfloat4 ssfx_mv_calc(float4 current, float4 previous, float hud, float mask) { return 0.0; }\nfloat2 ssfx_taa_jitter(float4 hpos) { return hpos.xy; }\nstatic float _ReferenceClip = 1.0;\nvoid ReferenceClip(float value) { _ReferenceClip = min(_ReferenceClip, value); }\n#define clip(value) ReferenceClip(value)\n";
        private const string BITS = "#ifndef MUST_BE_SET\n#define MUST_BE_SET uint(0x40000000)\n#endif\n#ifndef USABLE_BIT_13\n#define USABLE_BIT_13 uint(0x02000000)\n#endif\n#ifndef USABLE_BIT_14\n#define USABLE_BIT_14 uint(0x04000000)\n#endif\n#ifndef USABLE_BIT_15\n#define USABLE_BIT_15 uint(0x80000000)\n#endif\n";

        public static readonly int[][] STAGES = { new[] { 0, 1, 2 }, new[] { 3, 4 }, new[] { 5, 6 }, new[] { 7, 8, 9 }, new[] { 10, 11 }, new[] { 12, 13, 14 }, new[] { 15 }, new[] { 16 }, new[] { 17, 18, 19 }, new[] { 20 }, new[] { 21 } };
        public static readonly string[] NAMES = { "Anomaly_TreeBranch_PS", "Anomaly_TreeBranch_VS", "Anomaly_TreeTrunk_VS", "Anomaly_Grass_PS", "Anomaly_Grass_VS", "TrueStalker_Leaf_PS", "TrueStalker_TreeBranch_VS", "TrueStalker_TreeTrunk_VS", "TrueStalker_Grass_PS", "TrueStalker_Grass_VS", "Anomaly_Flora_Lmodel" };

        public static List<string> Files(int group)
        {
            switch (group)
            {
                case 0: return new List<string> { ANOMALY_BASE + "common_defines.h", ES + "common_functions.h", SSS + "gbuffer_stage.h", SSS + "sload.h", SSS + "deffer_tree_branch_aref_bump-hq.ps" };
                case 1: return new List<string> { ANOMALY_BASE + "common_defines.h", ES + "common_functions.h", SSS + "screenspace_wind.h", SSS + "deffer_tree_branch_bump-hq.vs" };
                case 2: return new List<string> { ANOMALY_BASE + "common_defines.h", ES + "common_functions.h", SSS + "screenspace_wind.h", SSS + "deffer_tree_bump-hq.vs" };
                case 3: return new List<string> { ANOMALY_BASE + "common_defines.h", ES + "common_functions.h", SSS + "gbuffer_stage.h", SSS + "sload.h", SSS + "deffer_grass.ps" };
                case 4: return new List<string> { ANOMALY_BASE + "common_defines.h", ES + "common_functions.h", SSS + "screenspace_wind.h", SSS + "deffer_grass.vs" };
                case 5: return new List<string> { TS + "common_defines.h", TS + "common_functions.h", TS + "sload.h", TS + "deffer_base_aref_bump-hq.ps" };
                case 6: return new List<string> { TS + "common_defines.h", TS + "common_functions.h", TS + "shared/common.h", TS + "deffer_tree_branch_bump-hq.vs" };
                case 7: return new List<string> { TS + "common_defines.h", TS + "common_functions.h", TS + "shared/common.h", TS + "deffer_tree_bump-hq.vs" };
                case 8: return new List<string> { TS + "common_defines.h", TS + "common_functions.h", TS + "sload.h", TS + "deffer_grass.ps" };
                case 9: return new List<string> { TS + "common_defines.h", TS + "common_functions.h", TS + "shared/common.h", TS + "deffer_grass.vs" };
                default: return new List<string> { ANOMALY_BASE + "common_defines.h", ANOMALY_BASE + "anomaly_shaders.h", ES + "common_functions.h", SSS + "gbuffer_stage.h", SSS + "sload.h", SSS + "deffer_tree_branch_aref_bump-hq.ps", ES + "srgb.h", SSS + "lmodel.h" };
            }
        }

        private static Dictionary<string, string> Includes(int group)
        {
            var includes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (group == 7) includes["deffer_tree_bump.vs"] = TS + "deffer_tree_bump.vs";
            if (group == 10)
            {
                includes["common_brdf.h"] = SSS + "common_brdf.h";
                includes["pbr_brdf.h"] = SSS + "pbr_brdf.h";
                includes["pbr_settings.h"] = ATMOSPHERICS + "pbr_settings.h";
                includes["pbr_brdf_blinn.h"] = ES + "pbr_brdf_blinn.h";
                includes["pbr_brdf_ggx.h"] = SSS + "pbr_brdf_ggx.h";
            }
            return includes;
        }

        private static string Expand(string file, Dictionary<string, string> includes, HashSet<string> visited, StringBuilder hashes)
        {
            hashes.AppendLine(Sha256(file) + "  " + file);
            string text = WithoutDeclarations(Strip(File.ReadAllText(file)));
            return Regex.Replace(text, @"^[ \t]*#[ \t]*include[ \t]+""([^""]+)""[^\r\n]*", match =>
            {
                string name = Path.GetFileName(match.Groups[1].Value.Replace('\\', '/'));
                if (!includes.TryGetValue(name, out string path) || !visited.Add(name.ToLowerInvariant())) return string.Empty;
                return Expand(path, includes, visited, hashes);
            }, RegexOptions.Multiline);
        }

        public static string Build(int group, string template, out string manifest)
        {
            var hashes = new StringBuilder();
            var source = new StringBuilder();
            source.AppendLine(DEFINES);
            if (group == 0 || group == 1 || group == 2 || group == 3 || group == 4 || group == 10) source.AppendLine("#define SSFX_ENHANCED_SHADERS\n#define SSFX_FLORAFIX\n#define SSFX_WIND\n#define SSFX_NEWGLOSS");
            if (group == 4) source.AppendLine("#define SSFX_WIND_ISGRASS");
            if (group == 10) source.AppendLine("#undef PI");
            List<string> files = Files(group);
            Dictionary<string, string> includes = Includes(group);
            var visited = new HashSet<string>();
            bool structures = false;
            foreach (string file in files)
            {
                string text = Expand(file, includes, visited, hashes);
                string name = Path.GetFileName(file);
                if (name == "common_functions.h")
                {
                    var functions = new StringBuilder();
                    foreach (string function in new[] { "unpack_normal", "unpack_bx4", "unpack_D3DCOLOR", "hash12", "gbuf_pack_hemi_mtl", "gbuf_unpack_hemi", "gbuf_unpack_mtl" })
                    {
                        foreach (string body in Functions(text, function)) functions.AppendLine(body);
                    }
                    text = BITS + functions;
                }
                else if (name == "anomaly_shaders.h")
                {
                    text = string.Join("\n", Regex.Matches(text, @"^[ \t]*#[ \t]*define[ \t]+LUMINANCE_VECTOR[^\r\n]*", RegexOptions.Multiline).Cast<Match>().Select(match => match.Value.Trim()));
                }
                else if (name == "gbuffer_stage.h")
                {
                    text = string.Join("\n", Functions(text, "gbuf_pack_hemi_mtl").Concat(Functions(text, "gbuf_unpack_hemi")).Concat(Functions(text, "gbuf_unpack_mtl")));
                }
                else if (name == "common.h")
                {
                    text = string.Join("\n", Functions(text, "calc_cyclic").Concat(Functions(text, "calc_xz_wave")));
                }
                if (!structures && (name.EndsWith(".ps", StringComparison.Ordinal) || name.EndsWith(".vs", StringComparison.Ordinal) || name == "sload.h" || name == "screenspace_wind.h"))
                {
                    source.AppendLine(STRUCTURES);
                    structures = true;
                }
                if (name.EndsWith(".ps", StringComparison.Ordinal)) text = Regex.Replace(text, @"\bmain\s*\(", "SourceMain(");
                if (name.EndsWith(".vs", StringComparison.Ordinal)) text = Regex.Replace(text, @"\bmain\s*\(", "SourceVertex(");
                source.AppendLine("#line 1 \"" + name + "\"");
                source.AppendLine(text);
            }
            source.AppendLine(Evaluate(group));
            manifest = hashes.ToString();
            int start = template.IndexOf("            float3 TreeWorld()", StringComparison.Ordinal);
            int end = template.IndexOf("            float4 Frag(", start, StringComparison.Ordinal);
            if (start < 0 || end < 0) throw new InvalidOperationException("Validation template markers are missing.");
            string reference = template.Substring(0, start) + source + template.Substring(end);
            return reference.Replace("Hidden/XRay/MonolithFloraValidation", "Hidden/XRay/MonolithFloraReference_" + NAMES[group]);
        }

        private static string Evaluate(int group)
        {
            const string PIXEL = "    p_bumped I = (p_bumped)0;\n    I.tcdh = tc;\n    I.position = _Position;\n    I.M1 = float3(_Terrain.w, 0.0, _Terrain.x);\n    I.M2 = float3(0.0, 0.0, _Terrain.y);\n    I.M3 = float3(0.0, 0.0, _Terrain.z);\n    I.sss_extra = float4(0.0, 0.0, _Extra.x, _Extra.y);\n    f_deffer O = SourceMain(I);\n    float packedHemiMaterial = gbuf_pack_hemi_mtl(O.Ne.w, O.P.w);\n";
            const string TREE = "    v_tree I;\n    I.P = float4(_Vertex.xyz, 1.0);\n    I.Nh = float4(0.5, 0.5, 0.5, _VertexData.x);\n    I.T = float3(0.5, 0.5, 0.5);\n    I.B = float3(0.5, 0.5, 0.5);\n    I.tc = int4((int)round(_VertexData.y / _Consts.x), (int)round(_VertexData.z / _Consts.y), (int)_VertexData.w, 0);\n    v2p_bumped O = SourceVertex(I);\n";
            const string DETAIL = "    v_detail I;\n    I.pos = float4(_Vertex.xyz, 1.0);\n    I.misc = int4((int)round(_VertexData.y / _Consts.x), (int)round(_VertexData.z / _Consts.y), (int)_VertexData.w, 0);\n    v2p_bumped O = SourceVertex(I);\n";
            string surface = "    if (_Stage < {0}) return float4(O.Ne.xyz, O.C.w);\n    if (_Stage < {1}) return float4(O.C.rgb, _ReferenceClip >= 0.0 ? 1.0 : 0.0);\n    return float4(gbuf_unpack_mtl(packedHemiMaterial), gbuf_unpack_hemi(packedHemiMaterial), O.P.w, O.Ne.w);\n";
            var body = new StringBuilder("float4 Evaluate(float2 tc)\n{\n");
            switch (group)
            {
                case 0: body.Append(PIXEL).Append(string.Format(surface, "0.5", "1.5")); break;
                case 1: body.Append(TREE).Append("    if (_Stage < 3.5) return float4(O.hpos.xyz, O.position.w);\n    return float4(O.vel_prev.xyz, O.sss_extra.z);\n"); break;
                case 2: body.Append(TREE).Append("    if (_Stage < 5.5) return float4(O.hpos.xyz, O.position.w);\n    return float4(O.vel_prev.xyz, 1.0);\n"); break;
                case 3: body.Append(PIXEL).Append(string.Format(surface, "7.5", "8.5")); break;
                case 4: body.Append(DETAIL).Append("    if (_Stage < 10.5) return float4(O.hpos.xyz, O.position.w);\n    return float4(O.vel_prev.xyz, O.sss_extra.z);\n"); break;
                case 5: body.Append(PIXEL).Append(string.Format(surface, "12.5", "13.5")); break;
                case 6: body.Append(TREE).Append("    return float4(O.hpos.xyz, O.position.w);\n"); break;
                case 7: body.Append(TREE).Append("    return float4(O.hpos.xyz, O.position.w);\n"); break;
                case 8: body.Append(PIXEL).Append(string.Format(surface, "17.5", "18.5")); break;
                case 9: body.Append(DETAIL).Append("    return float4(O.hpos.xyz, O.position.w);\n"); break;
                default: body.Append(PIXEL).Append("    float material = gbuf_unpack_mtl(packedHemiMaterial);\n    return float4(plight_infinity(material, _Position.xyz, O.Ne.xyz, float4(O.C.rgb, O.C.w), _LightDirection.xyz).rgb, 1.0);\n"); break;
            }
            body.Append("}\n");
            return body.ToString();
        }

        public static string Sha256(string file)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static IEnumerable<string> Functions(string source, string name)
        {
            foreach (Match match in Regex.Matches(source, @"\b(?:float[234]?|void|uint)\s+" + Regex.Escape(name) + @"\s*\([^;{]*?\)\s*\{"))
            {
                int start = source.IndexOf('{', match.Index);
                int depth = 1;
                int end = start + 1;
                while (depth > 0 && end < source.Length)
                {
                    if (source[end] == '{') depth++;
                    else if (source[end] == '}') depth--;
                    end++;
                }
                if (depth != 0) throw new InvalidOperationException("Unbalanced function " + name);
                yield return source.Substring(match.Index, end - match.Index);
            }
        }

        private static string Strip(string source)
        {
            return Regex.Replace(source, @"/\*.*?\*/|//[^\r\n]*", string.Empty, RegexOptions.Singleline);
        }

        private static string WithoutDeclarations(string source)
        {
            var mapped = new HashSet<string>(MAPPED);
            var lines = source.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match match = Regex.Match(lines[i], @"^\s*(?:uniform\s+)?(?:float[234]?(?:x[234])?|int|Texture2D|sampler|SamplerState)\s+([A-Za-z_][\w\s,\[\]\*]*?)\s*;\s*$");
                if (!match.Success) continue;
                string[] names = match.Groups[1].Value.Split(',').Select(part => Regex.Replace(part, @"\[.*", string.Empty).Trim()).ToArray();
                if (names.All(mapped.Contains)) lines[i] = string.Empty;
            }
            return string.Join("\n", lines);
        }
    }
}
