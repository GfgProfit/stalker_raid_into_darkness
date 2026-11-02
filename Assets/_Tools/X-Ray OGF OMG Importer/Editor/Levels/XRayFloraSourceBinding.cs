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
        public const string ANOMALY_WIND_CONTRACT_HASH = "bf458cc9ae3f6f5a29fff39c857386c0018886cbd9954eab361087ec302fcf13";
        public const string TRUE_STALKER_WIND_CONTRACT_HASH = "5797ad01668d5c28013707909190194c8f2f2e141b2931b3470ab2e7e1b463d9";
        public const string ANOMALY_WIND_WAVES = "textures/fx/wind_wave.dds";
        public const string ANOMALY_DETAIL_CONTRACT_HASH = "7e6799f789c9c508c1fecbcd7b9d455ee4c72ab91f801bdfab7cfb186b4f6614";
        public const string TRUE_STALKER_DETAIL_CONTRACT_HASH = "ad8af0d5d42b5fa05e72a2fd2a8850ecef8a7c50c8b118cda2681b1c1ddd6fbf";

        private static readonly string[] ANOMALY_DETAIL_FILES = { "shaders/r3/deffer_grass.vs", "shaders/r3/deffer_grass.ps", "shaders/r3/details_blend.s", "shaders/r3/screenspace_wind.h", "shaders/r3/common_defines.h" };
        private static readonly string[] TRUE_STALKER_DETAIL_FILES = { "shaders/r3/deffer_grass.vs", "shaders/r3/deffer_grass.ps", "shaders/r3/details_blend.s", "shaders/r3/common_defines.h", "shaders/r3/sload.h" };

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

        private static readonly string[] ANOMALY_WIND_FILES =
        {
            "shaders/r3/screenspace_wind.h",
            "shaders/r3/check_screenspace_wind.h",
            "shaders/r3/deffer_tree_branch_bump-hq.vs",
            "shaders/r3/deffer_tree_bump-hq.vs",
            "shaders/r3/shadow_direct_tree.vs",
            "shaders/r3/shadow_direct_tree_aref.vs",
            ANOMALY_WIND_WAVES
        };

        private static readonly string[] TRUE_STALKER_WIND_FILES =
        {
            "shaders/r3/shared/common.h",
            "shaders/r3/deffer_tree_branch_bump-hq.vs",
            "shaders/r3/deffer_tree_bump-hq.vs",
            "shaders/r3/deffer_tree_bump.vs",
            "shaders/r3/shadow_direct_tree.vs",
            "shaders/r3/shadow_direct_tree_aref.vs"
        };

        public static IReadOnlyList<string> AnomalyFiles => ANOMALY_FILES;
        public static IReadOnlyList<string> AnomalyDetailFiles => ANOMALY_DETAIL_FILES;
        public static IReadOnlyList<string> TrueStalkerDetailFiles => TRUE_STALKER_DETAIL_FILES;
        public static IReadOnlyList<string> AnomalyWindFiles => ANOMALY_WIND_FILES;
        public static IReadOnlyList<string> TrueStalkerWindFiles => TRUE_STALKER_WIND_FILES;
        public static IReadOnlyList<string> TrueStalkerFiles => TRUE_STALKER_FILES;

        public XRayFloraSourceProfile Profile { get; private set; }
        public string AnomalyHash { get; private set; }
        public string TrueStalkerHash { get; private set; }
        public XRayFloraSourceProfile WindProfile { get; private set; }
        public string AnomalyWindHash { get; private set; }
        public string TrueStalkerWindHash { get; private set; }
        public List<XRayResolvedFile> WindFiles { get; } = new List<XRayResolvedFile>();
        public string WindContractHash => WindProfile == XRayFloraSourceProfile.AnomalySss ? AnomalyWindHash : WindProfile == XRayFloraSourceProfile.TrueStalker ? TrueStalkerWindHash : string.Empty;
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
            binding.AnomalyWindHash = Contract(resolver, ANOMALY_WIND_FILES, null);
            binding.TrueStalkerWindHash = Contract(resolver, TRUE_STALKER_WIND_FILES, null);
            if (binding.Profile == XRayFloraSourceProfile.AnomalySss && binding.AnomalyWindHash == ANOMALY_WIND_CONTRACT_HASH) binding.WindProfile = XRayFloraSourceProfile.AnomalySss;
            else if (binding.Profile == XRayFloraSourceProfile.TrueStalker && binding.TrueStalkerWindHash == TRUE_STALKER_WIND_CONTRACT_HASH) binding.WindProfile = XRayFloraSourceProfile.TrueStalker;
            if (binding.WindProfile != XRayFloraSourceProfile.Unknown) Contract(resolver, binding.WindProfile == XRayFloraSourceProfile.AnomalySss ? ANOMALY_WIND_FILES : TRUE_STALKER_WIND_FILES, binding.WindFiles);
            return binding;
        }

        public static bool Accepts(XRayFloraImportMode mode, XRayFloraSourceProfile profile)
        {
            return mode == XRayFloraImportMode.EnableVerifiedAnomaly && profile == XRayFloraSourceProfile.AnomalySss || mode == XRayFloraImportMode.EnableVerifiedTrueStalker && profile == XRayFloraSourceProfile.TrueStalker;
        }

        public static bool AcceptsWind(XRayFloraWindImportMode mode, XRayFloraSourceProfile profile)
        {
            return mode == XRayFloraWindImportMode.EnableVerifiedAnomaly && profile == XRayFloraSourceProfile.AnomalySss || mode == XRayFloraWindImportMode.EnableVerifiedTrueStalker && profile == XRayFloraSourceProfile.TrueStalker;
        }

        public static bool AcceptsDetails(XRayTextureResolver resolver, XRayFloraSourceProfile profile)
        {
            return profile == XRayFloraSourceProfile.AnomalySss && Contract(resolver, ANOMALY_DETAIL_FILES, null) == ANOMALY_DETAIL_CONTRACT_HASH || profile == XRayFloraSourceProfile.TrueStalker && Contract(resolver, TRUE_STALKER_DETAIL_FILES, null) == TRUE_STALKER_DETAIL_CONTRACT_HASH;
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
        public string GrassWavesPath { get; set; }
    }
}
