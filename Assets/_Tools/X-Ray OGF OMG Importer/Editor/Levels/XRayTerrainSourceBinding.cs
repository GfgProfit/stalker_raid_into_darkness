using System;
using System.Security.Cryptography;
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

        public XRayTerrainSourceProfile Profile { get; private set; }
        public XRayResolvedFile Shader { get; private set; }
        public XRayResolvedFile Brdf { get; private set; }
        public string ShaderHash { get; private set; }
        public string BrdfHash { get; private set; }
        public bool UsesHeight => Profile == XRayTerrainSourceProfile.AnomalyAtmospherics;

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
            return binding;
        }

        private static string Hash(XRayResolvedFile file)
        {
            if (file == null || !file.IsResolved) return string.Empty;
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(file.Winner.ReadAllBytes())).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
