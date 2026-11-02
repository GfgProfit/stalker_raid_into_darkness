using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Textures;

namespace XRay.Importer.Editor.Levels
{
    public enum XRayLevelBindingOwnership
    {
        Generated,
        Unowned,
        UserOverride
    }

    public sealed class XRayLevelBindingChange
    {
        public string Property { get; set; }
        public string Current { get; set; }
        public string Previous { get; set; }
        public string Desired { get; set; }
        public string Final { get; set; }
        public XRayLevelBindingOwnership Ownership { get; set; }
        public bool Modifies => Final != Current;
    }

    public sealed class XRayLevelSourceBinding
    {
        public bool Eligible { get; set; }
        public string BlenderClass { get; set; }
        public string CorrectionPath { get; set; }
        public string DetailPath { get; set; }
        public string DetailBumpPath { get; set; }
        public string DetailCorrectionPath { get; set; }
        public float DetailScale { get; set; } = 1.0f;
        public int RecommendedDetail { get; set; }
        public int RecommendedParallax { get; set; }
        public List<string> Reasons { get; } = new List<string>();
    }

    public sealed class XRayLevelBindingPlan
    {
        public List<XRayLevelBindingChange> Changes { get; } = new List<XRayLevelBindingChange>();
        public string Tag { get; set; }
        public string PreviousTag { get; set; }
        public bool Modifies => Tag != PreviousTag || Changes.Exists(change => change.Modifies);
        public bool HasUserOverrides => Changes.Exists(change => change.Ownership == XRayLevelBindingOwnership.UserOverride);
    }

    public static class XRayLevelSourceBindings
    {
        public const string TAG = "XRaySourceBinding";
        public const string VERSION = "xray-source-binding-v1";
        public const string CORRECTION = "_MonolithBumpCorrection";
        public const string DETAIL = "_MonolithDetailMap";
        public const string DETAIL_BUMP = "_MonolithDetailBump";
        public const string DETAIL_CORRECTION = "_MonolithDetailBumpCorrection";
        public const string HAS_CORRECTION = "_MonolithHasBumpCorrection";
        public const string HAS_DETAIL = "_MonolithHasDetail";
        public const string HAS_DETAIL_BUMP = "_MonolithHasDetailBump";
        public const string DETAIL_SCALE = "_MonolithDetailScale";
        public const string DETAIL_FLIP = "_MonolithDetailFlipV";
        public const string DETAIL_SRGB = "_MonolithDetailSrgb";
        public const string RECOMMENDED_DETAIL = "_MonolithSourceDetailRecommended";
        public const string RECOMMENDED_PARALLAX = "_MonolithSourceParallaxRecommended";

        public static readonly string[] TEXTURE_PROPERTIES = { CORRECTION, DETAIL, DETAIL_BUMP, DETAIL_CORRECTION };
        public static readonly string[] FLOAT_PROPERTIES = { HAS_CORRECTION, HAS_DETAIL, HAS_DETAIL_BUMP, DETAIL_SCALE, DETAIL_FLIP, DETAIL_SRGB, RECOMMENDED_DETAIL, RECOMMENDED_PARALLAX };

        public static bool DeclaresDetail(XRayTextureDescription description)
        {
            return description != null && !string.IsNullOrEmpty(description.DetailName) && (description.Flags & (XRayTextureDescription.FLAG_DIFFUSE_DETAIL | XRayTextureDescription.FLAG_BUMP_DETAIL)) != 0;
        }

        public static bool CanBeDetailed(string blenderClass)
        {
            return blenderClass == "LM" || blenderClass == "V" || blenderClass == "LM_AREF" || blenderClass == "V_AREF" || blenderClass == "MODEL" || blenderClass == "D_TREE";
        }

        public static bool CanUseSteepParallax(string blenderClass)
        {
            return blenderClass == "LM" || blenderClass == "V" || blenderClass == "LM_AREF" || blenderClass == "V_AREF";
        }

        public static XRayLevelSourceBinding Resolve(XRayTextureDescription description, string blenderClass, XRayLevelSurfaceKind kind, bool flora, bool detailObject, bool hasBump, float bumpFlipV, string correctionPath, string detailPath, string detailBumpPath, string detailCorrectionPath, Func<string, bool> orientation = null)
        {
            orientation = orientation ?? PackedFlipV;
            var binding = new XRayLevelSourceBinding { BlenderClass = blenderClass };
            if (kind != XRayLevelSurfaceKind.Opaque && kind != XRayLevelSurfaceKind.AlphaTest && kind != XRayLevelSurfaceKind.Emissive)
            {
                binding.Reasons.Add($"surface kind {kind} has no sload detail/parallax path");
                return binding;
            }

            if (detailObject)
            {
                binding.Reasons.Add("detail vegetation object (detail_still) has no sload detail/parallax path");
                return binding;
            }

            if (flora)
            {
                binding.Reasons.Add("flora normal path (_MONOLITH_LEVEL_FLORA) excludes source texture features");
                return binding;
            }

            if (!CanBeDetailed(blenderClass))
            {
                binding.Reasons.Add($"blender '{blenderClass ?? "unknown"}' does not use uber_deffer detail/parallax");
                return binding;
            }

            binding.Eligible = true;
            if (DeclaresDetail(description))
            {
                if (detailPath == null || double.IsNaN(description.DetailScale) || double.IsInfinity(description.DetailScale) || description.DetailScale <= 0.0 || description.DetailScale > float.MaxValue)
                {
                    binding.Reasons.Add($"detail '{description.DetailName}' is unavailable or detail_scale is invalid ({description.DetailScale}); detail disabled");
                }
                else
                {
                    binding.DetailPath = detailPath;
                    binding.DetailScale = (float)description.DetailScale;
                    binding.RecommendedDetail = 1;
                    bool bumpDetail = (description.Flags & XRayTextureDescription.FLAG_BUMP_DETAIL) != 0;
                    if (bumpDetail && hasBump && detailBumpPath != null && detailCorrectionPath != null && orientation(detailBumpPath) == orientation(detailPath) && orientation(detailCorrectionPath) == orientation(detailPath))
                    {
                        binding.DetailBumpPath = detailBumpPath;
                        binding.DetailCorrectionPath = detailCorrectionPath;
                        binding.RecommendedDetail = 2;
                    }
                    else if (bumpDetail)
                    {
                        binding.Reasons.Add($"flBumpDetail set but the detail bump chain is incomplete or has mixed orientation (bump {(detailBumpPath != null ? "found" : "missing")}, bump# {(detailCorrectionPath != null ? "found" : "missing")}); USE_TDETAIL only");
                    }
                }
            }

            if (!hasBump)
            {
                binding.Reasons.Add("no bump: engine selects *_flat shaders without parallax");
            }
            else if (correctionPath == null)
            {
                binding.Reasons.Add("bump# (height) is not available; parallax disabled");
            }
            else if ((orientation(correctionPath) ? 1.0f : 0.0f) != bumpFlipV)
            {
                binding.Reasons.Add("bump# orientation differs from bump (_MonolithBumpFlipV); parallax disabled");
            }
            else
            {
                binding.CorrectionPath = correctionPath;
                if (CanUseSteepParallax(blenderClass) && description != null && description.BumpMode == XRayBumpMode.UseParallax)
                {
                    binding.RecommendedParallax = 2;
                }
                else if ((kind == XRayLevelSurfaceKind.AlphaTest || blenderClass == "LM_AREF" || blenderClass == "V_AREF") && binding.RecommendedDetail == 0)
                {
                    binding.Reasons.Add("alpha-test HQ without detail uses SSS deffer_base_aref_bump-hq.ps, which has no USE_PARALLAX");
                }
                else
                {
                    binding.RecommendedParallax = 1;
                }
            }

            return binding;
        }

        public static XRayLevelBindingPlan Plan(Material material, XRayLevelSourceBinding binding)
        {
            var plan = new XRayLevelBindingPlan { PreviousTag = material.GetTag(TAG, false, string.Empty) };
            Dictionary<string, string> previous = ParseTag(plan.PreviousTag);
            var desired = new Dictionary<string, string>();
            bool eligible = binding != null && binding.Eligible;
            desired[CORRECTION] = eligible ? Guid(binding.CorrectionPath) : string.Empty;
            desired[DETAIL] = eligible ? Guid(binding.DetailPath) : string.Empty;
            desired[DETAIL_BUMP] = eligible ? Guid(binding.DetailBumpPath) : string.Empty;
            desired[DETAIL_CORRECTION] = eligible ? Guid(binding.DetailCorrectionPath) : string.Empty;
            var final = new Dictionary<string, string>();
            foreach (string property in TEXTURE_PROPERTIES)
            {
                XRayLevelBindingChange change = Decide(property, TextureValue(material, property), string.Empty, previous, desired[property]);
                plan.Changes.Add(change);
                final[property] = change.Final;
            }

            Texture2D bumpTexture = material.GetTexture("_MonolithBump") as Texture2D;
            bool hasBump = bumpTexture != null && !bumpTexture.isDataSRGB && material.GetFloat("_MonolithHasBump") > 0.5f;
            bool hasCorrection = Linear(final[CORRECTION]) && hasBump && (PackedFlipV(AssetDatabase.GUIDToAssetPath(final[CORRECTION])) ? 1.0f : 0.0f) == material.GetFloat("_MonolithBumpFlipV");
            bool hasDetail = Bound(final[DETAIL]) && !float.IsNaN(material.GetFloat(DETAIL_SCALE)) && !float.IsInfinity(material.GetFloat(DETAIL_SCALE));
            bool hasDetailBump = hasBump && hasDetail && Linear(final[DETAIL_BUMP]) && Linear(final[DETAIL_CORRECTION]) && PackedFlipV(AssetDatabase.GUIDToAssetPath(final[DETAIL_BUMP])) == PackedFlipV(AssetDatabase.GUIDToAssetPath(final[DETAIL])) && PackedFlipV(AssetDatabase.GUIDToAssetPath(final[DETAIL_CORRECTION])) == PackedFlipV(AssetDatabase.GUIDToAssetPath(final[DETAIL]));
            desired[HAS_CORRECTION] = Number(hasCorrection ? 1.0f : 0.0f);
            desired[HAS_DETAIL] = Number(hasDetail ? 1.0f : 0.0f);
            desired[HAS_DETAIL_BUMP] = Number(hasDetailBump ? 1.0f : 0.0f);
            desired[DETAIL_SCALE] = Number(eligible && hasDetail && binding.DetailPath != null ? binding.DetailScale : Default(material, DETAIL_SCALE));
            desired[DETAIL_FLIP] = Number(hasDetail && PackedFlipV(AssetDatabase.GUIDToAssetPath(final[DETAIL])) ? 1.0f : 0.0f);
            Texture2D detailTexture = hasDetail ? AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(final[DETAIL])) : null;
            desired[DETAIL_SRGB] = Number(detailTexture != null && detailTexture.isDataSRGB ? 1.0f : 0.0f);
            desired[RECOMMENDED_DETAIL] = Number(eligible && hasDetail ? Mathf.Min(binding.RecommendedDetail, hasDetailBump ? 2 : 1) : 0);
            desired[RECOMMENDED_PARALLAX] = Number(eligible && hasCorrection ? binding.RecommendedParallax : 0);
            foreach (string property in FLOAT_PROPERTIES)
            {
                XRayLevelBindingChange change = Decide(property, Number(material.GetFloat(property)), Number(Default(material, property)), previous, desired[property]);
                if (property == HAS_CORRECTION || property == HAS_DETAIL || property == HAS_DETAIL_BUMP)
                {
                    change.Ownership = XRayLevelBindingOwnership.Generated;
                    change.Final = desired[property];
                }

                plan.Changes.Add(change);
            }

            var tag = new StringBuilder(VERSION);
            foreach (string property in TEXTURE_PROPERTIES)
            {
                AppendOwnership(tag, plan.Changes.Find(change => change.Property == property));
            }

            foreach (string property in FLOAT_PROPERTIES)
            {
                AppendOwnership(tag, plan.Changes.Find(change => change.Property == property));
            }

            plan.Tag = !eligible && previous == null && !plan.Changes.Exists(change => change.Modifies) ? plan.PreviousTag : tag.ToString();
            return plan;
        }

        public static bool Apply(Material material, XRayLevelBindingPlan plan)
        {
            if (!plan.Modifies)
            {
                return false;
            }

            foreach (XRayLevelBindingChange change in plan.Changes)
            {
                if (!change.Modifies)
                {
                    continue;
                }

                if (Array.IndexOf(TEXTURE_PROPERTIES, change.Property) >= 0)
                {
                    material.SetTexture(change.Property, change.Final.Length > 0 ? AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(change.Final)) : null);
                }
                else
                {
                    material.SetFloat(change.Property, float.Parse(change.Final, CultureInfo.InvariantCulture));
                }
            }

            material.SetOverrideTag(TAG, plan.Tag);
            EditorUtility.SetDirty(material);
            return true;
        }

        public static bool PackedFlipV(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || !File.Exists(assetPath))
            {
                return false;
            }

            var header = new byte[148];
            using (FileStream stream = File.OpenRead(assetPath))
            {
                if (stream.Read(header, 0, header.Length) < 132 || header[84] != 'D' || header[85] != 'X' || header[86] != '1' || header[87] != '0')
                {
                    return false;
                }
            }

            uint dxgi = BitConverter.ToUInt32(header, 128);
            return dxgi == 95 || dxgi == 96 || dxgi == 98 || dxgi == 99;
        }

        public static Dictionary<string, string> ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith(VERSION + ";", StringComparison.Ordinal))
            {
                return null;
            }

            var values = new Dictionary<string, string>();
            foreach (string part in tag.Substring(VERSION.Length + 1).Split(';'))
            {
                int separator = part.IndexOf('=');
                if (separator > 0)
                {
                    values[part.Substring(0, separator)] = part.Substring(separator + 1);
                }
            }

            return values;
        }

        public static string TextureValue(Material material, string property)
        {
            Texture texture = material.GetTexture(property);
            if (texture == null)
            {
                return string.Empty;
            }

            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long _) ? guid : "instance:" + texture.GetEntityId();
        }

        internal static XRayLevelBindingChange Decide(string property, string current, string defaultValue, Dictionary<string, string> previous, string desired)
        {
            var change = new XRayLevelBindingChange { Property = property, Current = current, Desired = desired };
            if (previous != null && previous.TryGetValue(property, out string generated))
            {
                change.Previous = generated;
                change.Ownership = !generated.StartsWith("!", StringComparison.Ordinal) && current == generated ? XRayLevelBindingOwnership.Generated : XRayLevelBindingOwnership.UserOverride;
            }
            else if (current == defaultValue || current == desired)
            {
                change.Ownership = current == defaultValue ? XRayLevelBindingOwnership.Unowned : XRayLevelBindingOwnership.Generated;
            }
            else
            {
                change.Ownership = XRayLevelBindingOwnership.UserOverride;
            }

            change.Final = change.Ownership == XRayLevelBindingOwnership.UserOverride ? current : desired;
            return change;
        }

        internal static void AppendOwnership(StringBuilder tag, XRayLevelBindingChange change)
        {
            string generated = change.Ownership == XRayLevelBindingOwnership.UserOverride ? "!" + (change.Previous ?? change.Desired).TrimStart('!') : change.Desired;
            tag.Append(';').Append(change.Property).Append('=').Append(generated);
        }

        private static bool Bound(string guid)
        {
            return !string.IsNullOrEmpty(guid) && !guid.StartsWith("instance:", StringComparison.Ordinal) && AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid)) != null;
        }

        private static bool Linear(string guid)
        {
            return Bound(guid) && !AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid)).isDataSRGB;
        }

        private static float Default(Material material, string property)
        {
            int index = material.shader.FindPropertyIndex(property);
            return index >= 0 ? material.shader.GetPropertyDefaultFloatValue(index) : 0.0f;
        }

        private static string Guid(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
        }

        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
