using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public enum XRayFloraSourceProfile
    {
        Unknown,
        AnomalySss,
        TrueStalker
    }

    public sealed class XRayFloraSourceBinding
    {
        public const string VERSION = "xray-flora-binding-v1";
        public const string ANOMALY_CONTRACT_HASH = "facee55662ade6cf69973f32ef44e49e4cab67f9f33b7d1a5dac5074368aeda5";
        public const string TRUE_STALKER_CONTRACT_HASH = "13d1c2aae3714299c9d2d0c5429f7ffa26306ac12b62befae2b0245fb060843d";
        public const float AREF = 200.0f / 255.0f;
        public const float GRASS_AREF = 170.0f / 255.0f;

        private static readonly string[] ANOMALY_FILES =
        {
            "shaders/r3/deffer_tree_branch_aref_bump-hq.ps",
            "shaders/r3/deffer_tree_branch_bump-hq.vs",
            "shaders/r3/sload.h",
            "shaders/r3/common_functions.h",
            "shaders/r3/gbuffer_stage.h",
            "shaders/r3/common_defines.h",
            "shaders/r3/check_screenspace_flora.h",
            "shaders/r3/settings_screenspace_flora.h",
            "shaders/r3/lmodel.h",
            "shaders/r3/common_brdf.h",
            "shaders/r3/pbr_brdf.h",
            "shaders/r3/hmodel.h",
            "shaders/r3/accum_base.ps",
            "shaders/r3/accum_omni_unshadowed.ps",
            "shaders/r3/shadow_direct_base_aref.ps",
            "shaders/r3/deffer_grass.ps",
            "shaders/r3/details_blend.s"
        };

        private static readonly string[] TRUE_STALKER_FILES =
        {
            "shaders/r3/deffer_base_aref_bump-hq.ps",
            "shaders/r3/deffer_tree_branch_bump-hq.vs",
            "shaders/r3/sload.h",
            "shaders/r3/common_functions.h",
            "shaders/r3/common_defines.h",
            "shaders/r3/settings_screenspace_flora.h",
            "shaders/r3/lmodel.h",
            "shaders/r3/accum_omni_unshadowed.ps",
            "shaders/r3/shadow_direct_base_aref.ps",
            "shaders/r3/deffer_grass.ps",
            "shaders/r3/details_blend.s"
        };

        public static IReadOnlyList<string> AnomalyFiles => ANOMALY_FILES;
        public static IReadOnlyList<string> TrueStalkerFiles => TRUE_STALKER_FILES;

        public XRayFloraSourceProfile Profile { get; private set; }
        public string AnomalyHash { get; private set; }
        public string TrueStalkerHash { get; private set; }
        public List<XRayResolvedFile> Files { get; } = new List<XRayResolvedFile>();
        public string ContractHash => Profile == XRayFloraSourceProfile.AnomalySss ? AnomalyHash : Profile == XRayFloraSourceProfile.TrueStalker ? TrueStalkerHash : string.Empty;

        public static XRayFloraSourceBinding Resolve(XRayTextureResolver resolver)
        {
            var binding = new XRayFloraSourceBinding();
            binding.AnomalyHash = Contract(resolver, ANOMALY_FILES, null);
            binding.TrueStalkerHash = Contract(resolver, TRUE_STALKER_FILES, null);
            if (binding.AnomalyHash == ANOMALY_CONTRACT_HASH) binding.Profile = XRayFloraSourceProfile.AnomalySss;
            else if (binding.TrueStalkerHash == TRUE_STALKER_CONTRACT_HASH) binding.Profile = XRayFloraSourceProfile.TrueStalker;
            if (binding.Profile != XRayFloraSourceProfile.Unknown) Contract(resolver, binding.Profile == XRayFloraSourceProfile.AnomalySss ? ANOMALY_FILES : TRUE_STALKER_FILES, binding.Files);
            return binding;
        }

        public static bool Accepts(XRayFloraImportMode mode, XRayFloraSourceProfile profile)
        {
            return mode == XRayFloraImportMode.EnableVerifiedAnomaly && profile == XRayFloraSourceProfile.AnomalySss || mode == XRayFloraImportMode.EnableVerifiedTrueStalker && profile == XRayFloraSourceProfile.TrueStalker;
        }

        private static string Contract(XRayTextureResolver resolver, string[] files, List<XRayResolvedFile> resolved)
        {
            var contract = new StringBuilder();
            foreach (string path in files)
            {
                XRayResolvedFile file = resolver.ResolveFile(path);
                resolved?.Add(file);
                if (contract.Length > 0) contract.Append('\n');
                contract.Append(path).Append(':').Append(XRayTerrainSourceBinding.Hash(file));
            }
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(contract.ToString()))).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }

    public sealed class XRayLevelFloraTextures
    {
        public XRayFloraSourceProfile Profile { get; set; }
        public string AuthoredAlbedoPath { get; set; }
        public string AuthoredNote { get; set; }
        public string CorrectionPath { get; set; }
    }
}
