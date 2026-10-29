using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using System.IO;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public enum XRayTerrainSourceProfile
    {
        Unknown,
        AnomalyAtmospherics,
        TrueStalker
    }

    public sealed class XRayTerrainSourceBinding
    {
        public const string VERSION = "xray-terrain-binding-v1";
        public const string ANOMALY_SHADER_HASH = "a459da94365f59fe49c3d36bbd7dded1e80d2d0dac19187701bc82076c67b8d3";
        public const string ANOMALY_BRDF_HASH = "28f5979d56ed51d3e6260f461bcede914ebe1e01d99991b90a8747b5410c0b6a";
        public const string TRUE_STALKER_SHADER_HASH = "a7f772699db1146b67d48a178afc99dd42a200e34cc1d6246cddddabdfd9045a";
        public const string TRUE_STALKER_BRDF_HASH = "c1445208b47479905e319cc879c33f86bc9eb0ffb0a8f68a66720c9c6c646fa4";
        public const string ANOMALY_MID_HASH = "6004039018428d0f122f35050b0cd7c3375a215f2123c9fb828396eae78e6aed";
        public const string ANOMALY_LOW_HASH = "e1440dc306646c1c7fb66ab2a7a4399cb9a98eb33d1d520fe59b248bc19033e6";
        public const string TRUE_STALKER_CONTRACT_HASH = "7c080c55a46766d5e184db31906a5b8b8dee07a3b4cd042d0709d69ca8554a7a";

        private static readonly string[] TRUE_STALKER_FILES =
        {
            "shaders/r3/combine_1.ps",
            "shaders/r3/common.h",
            "shaders/r3/common_cbuffers.h",
            "shaders/r3/common_defines.h",
            "shaders/r3/common_functions.h",
            "shaders/r3/common_iostructs.h",
            "shaders/r3/common_policies.h",
            "shaders/r3/common_samplers.h",
            "shaders/r3/deffer_base_bump.vs",
            "shaders/r3/deffer_base_flat.ps",
            "shaders/r3/deffer_base_flat.vs",
            "shaders/r3/deffer_impl_flat.ps",
            "shaders/r3/deffer_impl_flat_d.ps",
            "shaders/r3/deffer_impl_flat_d.vs",
            "shaders/r3/hmodel.h",
            "shaders/r3/lmodel.h",
            "shaders/r3/screenspace_common.h",
            "shaders/r3/screenspace_common_noise.h",
            "shaders/r3/screenspace_common_ripples.h",
            "shaders/r3/screenspace_fog.h",
            "shaders/r3/screenspace_reflections.h",
            "shaders/r3/settings_screenspace_fog.h",
            "shaders/r3/settings_screenspace_puddles.h",
            "shaders/r3/settings_screenspace_ssr.h",
            "shaders/r3/shadow_direct_base.ps",
            "shaders/r3/shadow_direct_base.vs",
            "shaders/r3/shared/common.h",
            "shaders/r3/sload.h",
            "shaders/r3/ssao.ps",
            "shaders/r3/ssao_hbao.ps",
            "shaders/r3/ssao_hdao.ps",
            "shaders/r3/ssao_hdao_new.ps"
        };

        public static IReadOnlyList<string> TrueStalkerFiles => TRUE_STALKER_FILES;

        public XRayTerrainSourceProfile Profile { get; private set; }
        public XRayResolvedFile Shader { get; private set; }
        public XRayResolvedFile Brdf { get; private set; }
        public string ShaderHash { get; private set; }
        public string BrdfHash { get; private set; }
        public XRayResolvedFile MidShader { get; private set; }
        public XRayResolvedFile LowShader { get; private set; }
        public string MidHash { get; private set; }
        public string LowHash { get; private set; }
        public string TrueStalkerHash { get; private set; }
        public bool UsesHeight => Profile == XRayTerrainSourceProfile.AnomalyAtmospherics;
        public bool VerifiedMid => UsesHeight && MidHash == ANOMALY_MID_HASH;
        public bool VerifiedLow => UsesHeight && LowHash == ANOMALY_LOW_HASH;
        public bool VerifiedTrueStalker => Profile == XRayTerrainSourceProfile.TrueStalker && TrueStalkerHash == TRUE_STALKER_CONTRACT_HASH;

        public static bool IsTerrainBlender(XRayBlender blender)
        {
            return blender != null && (blender.ClassId == "BmmD" || blender.ClassId == "BmmDold");
        }

        public static XRayTerrainSourceProfile Identify(string shaderHash, string brdfHash)
        {
            if (shaderHash == ANOMALY_SHADER_HASH && brdfHash == ANOMALY_BRDF_HASH) return XRayTerrainSourceProfile.AnomalyAtmospherics;
            if (shaderHash == TRUE_STALKER_SHADER_HASH && brdfHash == TRUE_STALKER_BRDF_HASH) return XRayTerrainSourceProfile.TrueStalker;
            return XRayTerrainSourceProfile.Unknown;
        }

        public static XRayTerrainSourceBinding Resolve(XRayTextureResolver resolver)
        {
            var binding = new XRayTerrainSourceBinding { Brdf = resolver.ResolveFile("shaders/r3/lmodel.h") };
            binding.BrdfHash = Hash(binding.Brdf);
            XRayResolvedFile high = resolver.ResolveFile("shaders/r3/deffer_terrain_high_flat_d.ps");
            XRayResolvedFile legacy = resolver.ResolveFile("shaders/r3/deffer_impl_flat.ps");
            string highHash = Hash(high);
            string legacyHash = Hash(legacy);
            bool trueStalker = Identify(legacyHash, binding.BrdfHash) == XRayTerrainSourceProfile.TrueStalker;
            binding.Shader = trueStalker || !high.IsResolved ? legacy : high;
            binding.ShaderHash = trueStalker || !high.IsResolved ? legacyHash : highHash;
            binding.Profile = Identify(binding.ShaderHash, binding.BrdfHash);
            binding.MidShader = resolver.ResolveFile("shaders/r3/deffer_terrain_mid_flat.ps");
            binding.LowShader = resolver.ResolveFile("shaders/r3/deffer_terrain_low_flat.ps");
            binding.MidHash = Hash(binding.MidShader);
            binding.LowHash = Hash(binding.LowShader);
            if (binding.Profile == XRayTerrainSourceProfile.TrueStalker)
            {
                var contract = new StringBuilder();
                foreach (string path in TRUE_STALKER_FILES) contract.Append(path).Append(':').Append(Hash(resolver.ResolveFile(path))).Append('\n');
                using (SHA256 sha = SHA256.Create()) binding.TrueStalkerHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(contract.ToString().TrimEnd('\n')))).Replace("-", string.Empty).ToLowerInvariant();
            }
            return binding;
        }

        internal static string Hash(XRayResolvedFile file)
        {
            if (file == null || !file.IsResolved) return string.Empty;
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(file.Winner.ReadAllBytes())).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }

    public sealed class XRayTerrainSourceParameters
    {
        public Vector4? Pom { get; private set; }
        public Vector4? Offset { get; private set; }
        public float? Anisotropy { get; private set; }
        public float? MipBias { get; private set; }
        public string Path { get; private set; }
        public string Hash { get; private set; }

        public static XRayTerrainSourceParameters Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            XRayTerrainSourceParameters result = Parse(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
            result.Path = path;
            using (SHA256 sha = SHA256.Create()) result.Hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            return result;
        }

        public static XRayTerrainSourceParameters FromSource(XRayLevelSourceProvenance source)
        {
            if (string.IsNullOrEmpty(source?.GameFolder)) return null;
            XRayFsGame fs = File.Exists(source.FsGame) ? XRayFsGame.Parse(File.ReadAllBytes(source.FsGame), source.FsGame) : XRayFsGame.CreateDefault();
            XRayFsPath appData = fs.Paths.Find(path => path.Alias == XRayFsGame.APP_DATA_ROOT);
            if (appData == null || appData.IsExternal) return null;
            string root = System.IO.Path.GetFullPath(source.GameFolder).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
            string settings = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, appData.RelativePath, "user.ltx"));
            return settings.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(settings) ? Read(settings) : null;
        }

        public static XRayTerrainSourceParameters Parse(string text)
        {
            var result = new XRayTerrainSourceParameters();
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                int separator = trimmed.IndexOfAny(new[] { ' ', '\t' });
                if (separator < 0) continue;
                string key = trimmed.Substring(0, separator);
                string value = trimmed.Substring(separator).Trim();
                if (key == "ssfx_terrain_pom") result.Pom = ReadVector(value);
                else if (key == "ssfx_terrain_offset") result.Offset = ReadVector(value);
                else if (key == "r__tf_aniso") result.Anisotropy = ReadNumber(value);
                else if (key == "r__tf_mipbias") result.MipBias = ReadNumber(value);
            }
            if (result.Pom.HasValue && (result.Pom.Value.x < 1.0f || result.Pom.Value.y <= 0.0f || result.Pom.Value.z < 0.0f)) result.Pom = null;
            if (result.Anisotropy.HasValue && (result.Anisotropy.Value < 1.0f || result.Anisotropy.Value > 16.0f || result.Anisotropy.Value != Mathf.Floor(result.Anisotropy.Value))) result.Anisotropy = null;
            return result;
        }

        private static Vector4? ReadVector(string text)
        {
            string[] parts = text.Trim('(', ')', ' ').Split(',');
            if (parts.Length != 4) return null;
            var value = Vector4.zero;
            for (int i = 0; i < parts.Length; i++)
            {
                float? number = ReadNumber(parts[i]);
                if (!number.HasValue) return null;
                value[i] = number.Value;
            }
            return value;
        }

        private static float? ReadNumber(string text)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && !float.IsNaN(value) && !float.IsInfinity(value) ? value : (float?)null;
        }
    }
}
