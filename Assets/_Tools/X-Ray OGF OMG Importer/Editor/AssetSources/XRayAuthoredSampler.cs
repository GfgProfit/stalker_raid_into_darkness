using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor
{
    public sealed class XRayAuthoredSamplerPlan
    {
        public string AssetPath { get; set; }
        public bool Eligible { get; set; }
        public bool Recorded { get; set; }
        public int CurrentAnisotropy { get; set; }
        public float CurrentMipBias { get; set; }
        public int DesiredAnisotropy { get; set; }
        public float DesiredMipBias { get; set; }
        public int FinalAnisotropy { get; set; }
        public float FinalMipBias { get; set; }
        public string FinalRecord { get; set; }
        public List<string> Overrides { get; } = new List<string>();
        public bool ChangesTexture => Eligible && (FinalAnisotropy != CurrentAnisotropy || !Same(FinalMipBias, CurrentMipBias));
        public bool ChangesRecord { get; set; }
        public bool Applied { get; set; }
        public string Note { get; set; }

        private static bool Same(float a, float b)
        {
            return a.ToString("R", CultureInfo.InvariantCulture) == b.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    public static class XRayAuthoredSampler
    {
        public const string ANISOTROPY = "anisoLevel";
        public const string MIP_BIAS = "mipMapBias";

        public static XRayAuthoredSamplerPlan Plan(string assetPath, int anisotropy, float mipBias)
        {
            var plan = new XRayAuthoredSamplerPlan { AssetPath = assetPath, DesiredAnisotropy = anisotropy, DesiredMipBias = mipBias };
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            AuthoredMarker marker = ReadMarker(assetPath);
            if (texture == null || marker == null || marker.Key == null || marker.Key.IndexOf(XRayTextureImporter.AUTHORED_KEY_SUFFIX, StringComparison.Ordinal) < 0)
            {
                plan.Note = "Not an importer-owned authored flora texture; sampler settings are left unchanged.";
                return plan;
            }

            plan.Eligible = true;
            plan.CurrentAnisotropy = texture.anisoLevel;
            plan.CurrentMipBias = texture.mipMapBias;
            plan.Recorded = TryParse(marker.Sampler, out int recordedAnisotropy, out float recordedMipBias);
            bool ownsAnisotropy = !plan.Recorded || recordedAnisotropy == plan.CurrentAnisotropy;
            bool ownsMipBias = !plan.Recorded || Format(recordedMipBias) == Format(plan.CurrentMipBias);
            plan.FinalAnisotropy = ownsAnisotropy ? anisotropy : plan.CurrentAnisotropy;
            plan.FinalMipBias = ownsMipBias ? mipBias : plan.CurrentMipBias;
            if (!ownsAnisotropy) plan.Overrides.Add(ANISOTROPY);
            if (!ownsMipBias) plan.Overrides.Add(MIP_BIAS);
            plan.FinalRecord = Record(ownsAnisotropy ? anisotropy : recordedAnisotropy, ownsMipBias ? mipBias : recordedMipBias);
            plan.ChangesRecord = marker.Sampler != plan.FinalRecord;
            plan.Note = Describe(plan);
            return plan;
        }

        public static bool Apply(XRayAuthoredSamplerPlan plan)
        {
            if (plan == null || !plan.Eligible || !plan.ChangesTexture && !plan.ChangesRecord)
            {
                return false;
            }

            if (plan.ChangesTexture)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(plan.AssetPath);
                texture.anisoLevel = plan.FinalAnisotropy;
                texture.mipMapBias = plan.FinalMipBias;
                EditorUtility.SetDirty(texture);
                AssetDatabase.SaveAssetIfDirty(texture);
            }

            if (plan.ChangesRecord)
            {
                AuthoredMarker marker = ReadMarker(plan.AssetPath);
                Write(plan.AssetPath, marker.Key, marker.Role, marker.Source, marker.SourcePath, marker.Stamp, plan.FinalRecord);
            }

            plan.Applied = true;
            return true;
        }

        public static void WriteMarker(string assetPath, XRayAssetMarker marker, string record)
        {
            Write(assetPath, marker.Key, marker.Role, marker.Source, marker.SourcePath, marker.Stamp, record);
        }

        public static string Record(int anisotropy, float mipBias)
        {
            return "aniso=" + anisotropy.ToString(CultureInfo.InvariantCulture) + ";bias=" + Format(mipBias);
        }

        public static string ReadRecord(string assetPath)
        {
            return ReadMarker(assetPath)?.Sampler;
        }

        private static void Write(string assetPath, string key, string role, string source, string sourcePath, string stamp, string record)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                return;
            }

            string userData = XRayAssetMarker.PREFIX + JsonUtility.ToJson(new AuthoredMarker(key, role, source, sourcePath, stamp, record));
            if (importer.userData != userData)
            {
                importer.userData = userData;
                EditorUtility.SetDirty(importer);
                AssetDatabase.WriteImportSettingsIfDirty(assetPath);
            }
        }

        private static AuthoredMarker ReadMarker(string assetPath)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null || string.IsNullOrEmpty(importer.userData) || !importer.userData.StartsWith(XRayAssetMarker.PREFIX, StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<AuthoredMarker>(importer.userData.Substring(XRayAssetMarker.PREFIX.Length));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool TryParse(string record, out int anisotropy, out float mipBias)
        {
            anisotropy = 0;
            mipBias = 0.0f;
            if (string.IsNullOrEmpty(record))
            {
                return false;
            }

            string[] parts = record.Split(';');
            if (parts.Length != 2 || !parts[0].StartsWith("aniso=", StringComparison.Ordinal) || !parts[1].StartsWith("bias=", StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(parts[0].Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out anisotropy) && float.TryParse(parts[1].Substring(5), NumberStyles.Float, CultureInfo.InvariantCulture, out mipBias);
        }

        private static string Format(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Describe(XRayAuthoredSamplerPlan plan)
        {
            string state = plan.ChangesTexture ? $"sampler {plan.CurrentAnisotropy}/{Format(plan.CurrentMipBias)} -> {plan.FinalAnisotropy}/{Format(plan.FinalMipBias)}" : $"sampler {plan.CurrentAnisotropy}/{Format(plan.CurrentMipBias)} up to date";
            string origin = plan.Recorded ? string.Empty : "; no previous importer record, current values treated as importer-owned";
            string overrides = plan.Overrides.Count > 0 ? "; user overrides kept: " + string.Join(", ", plan.Overrides) : string.Empty;
            return "Authored flora " + state + origin + overrides + ".";
        }

        [Serializable]
        private sealed class AuthoredMarker
        {
            [SerializeField] private string _key;
            [SerializeField] private string _role;
            [SerializeField] private string _source;
            [SerializeField] private string _sourcePath;
            [SerializeField] private string _stamp;
            [SerializeField] private string _sampler;

            public AuthoredMarker(string key, string role, string source, string sourcePath, string stamp, string sampler)
            {
                _key = key;
                _role = role;
                _source = source;
                _sourcePath = sourcePath;
                _stamp = stamp;
                _sampler = sampler;
            }

            public string Key => _key;
            public string Role => _role;
            public string Source => _source;
            public string SourcePath => _sourcePath;
            public string Stamp => _stamp;
            public string Sampler => _sampler;
        }
    }
}
