using System;
using System.Collections.Generic;
using System.IO;
using XRay.Importer.Core.IO;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;

namespace RaidIntoDarkness.FirstPerson.Editor
{
    public sealed class FirstPersonModelSource
    {
        public FirstPersonModelSource(string visual, string virtualPath, XRayIndexedFile file)
        {
            Visual = visual;
            VirtualPath = virtualPath;
            File = file;
        }

        public string Visual { get; }
        public string VirtualPath { get; }
        public XRayIndexedFile File { get; }
        public bool Exists => File != null;
    }

    public sealed class FirstPersonOutfitRecord
    {
        public string Section { get; set; }
        public string DisplayName { get; set; }
        public string InvName { get; set; }
        public string HudSection { get; set; }
        public FirstPersonModelSource Hands { get; set; }
        public string LegsSection { get; set; }
        public FirstPersonModelSource Body { get; set; }
        public bool IsExoskeleton { get; set; }
        public string RepairType { get; set; }
        public FirstPersonOutfitResolution Resolution { get; set; }
        public string Origin { get; set; }
    }

    public sealed class FirstPersonOutfitScan
    {
        public List<FirstPersonOutfitRecord> Outfits { get; } = new List<FirstPersonOutfitRecord>();
        public List<string> ExcludedMultiplayer { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public Dictionary<string, string> SwmOptions { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public FirstPersonSwmScript Script { get; set; }
        public string ScriptVirtualPath { get; set; }
        public string ScriptSource { get; set; }
        public bool UseHdModels { get; set; }
        public int SectionCount { get; set; }
        public int ModFileCount { get; set; }
        public int StringCount { get; set; }
        public float CameraHeight { get; set; }
        public string CameraHeightOrigin { get; set; }
    }

    public static class FirstPersonOutfitScanner
    {
        public const string ACTOR_SECTION = "actor";
        public const string ACTOR_LEGS_SECTION = "actor_legs";
        public const string LEGS_SUFFIX = "_legs";
        public const string EXO_REPAIR_TYPE = "outfit_exo";
        public const string SCRIPT_PATH = "scripts/swm_legs.script";
        public const string OPTIONS_PATH = "configs/axr_options.ltx";
        public const string SWM_OPTION_PREFIX = "swm_legs/";
        public const string MULTIPLAYER_CONFIGS = "configs/mp/";
        public const string NO_SECTION_BODY = "sm\\actor_legs\\no_outfit";

        public static readonly string[] INDEX_SCOPE = { "configs", "scripts", "meshes/anomaly_weapons/hands", "meshes/sm/actor_legs" };

        public static FirstPersonOutfitScan Scan(XRayAssetIndex index)
        {
            var scan = new FirstPersonOutfitScan();
            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(index);
            scan.Warnings.AddRange(system.Warnings);
            scan.SectionCount = system.Sections.Count;
            scan.ModFileCount = system.ModFileCount;
            XRayStringTable strings = XRayStringTable.Load(index, "rus");
            scan.StringCount = strings.Count;
            ReadOptions(index, scan);
            ReadScript(index, scan);
            ReadCameraHeight(system, scan);

            if (scan.UseHdModels)
            {
                scan.Warnings.Add("swm_legs/use_hd_models is true; this scanner resolves only non-HD bodies (<section>_legs) and the result does not match the game.");
            }

            scan.Outfits.Add(CreateRecord(index, system, strings, ACTOR_SECTION, true));
            var names = new List<string>(system.Sections.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
            {
                if (name == ACTOR_SECTION || string.IsNullOrEmpty(system.GetValue(name, "player_hud_section")))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(system.GetValue(name, "inv_name")) || system.GetValue(name, "cost") == null)
                {
                    continue;
                }

                string definedIn = system.Sections[name].DefinedIn ?? string.Empty;
                if (definedIn.StartsWith(MULTIPLAYER_CONFIGS, StringComparison.Ordinal))
                {
                    scan.ExcludedMultiplayer.Add(name);
                    continue;
                }

                scan.Outfits.Add(CreateRecord(index, system, strings, name, false));
            }

            return scan;
        }

        public static string ToMeshPath(string visual)
        {
            string path = XRayVirtualPath.Normalize("meshes/" + visual);
            return path.EndsWith(".ogf", StringComparison.Ordinal) ? path : path + ".ogf";
        }

        private static FirstPersonOutfitRecord CreateRecord(XRayAssetIndex index, XRayLtxDatabase system, XRayStringTable strings, string section, bool isActor)
        {
            var record = new FirstPersonOutfitRecord { Section = section, Origin = system.GetOrigin(section) };
            record.InvName = isActor ? null : XRayLtxDatabase.Unquote(system.GetValue(section, "inv_name"));
            record.DisplayName = isActor ? "Без костюма" : strings.Translate(record.InvName);
            record.HudSection = XRayLtxDatabase.Unquote(system.GetValue(section, "player_hud_section"));
            string handsVisual = string.IsNullOrEmpty(record.HudSection) ? null : XRayLtxDatabase.Unquote(system.GetValue(record.HudSection, "visual"));
            record.Hands = CreateSource(index, handsVisual);
            record.RepairType = XRayLtxDatabase.Unquote(system.GetValue(section, "repair_type"));
            record.IsExoskeleton = string.Equals(record.RepairType, EXO_REPAIR_TYPE, StringComparison.OrdinalIgnoreCase);
            record.Resolution = FirstPersonOutfitResolution.Resolved;

            string legs = section + LEGS_SUFFIX;
            string legsVisual = isActor ? null : XRayLtxDatabase.Unquote(system.GetValue(legs, "visual"));
            FirstPersonModelSource body = string.IsNullOrEmpty(legsVisual) ? null : CreateSource(index, legsVisual);
            if (body != null && body.Exists)
            {
                record.LegsSection = legs;
                record.Body = body;
            }
            else
            {
                if (!isActor)
                {
                    record.Resolution = body == null ? FirstPersonOutfitResolution.BodyFallbackNoLegsSection : FirstPersonOutfitResolution.BodyFallbackMissingModel;
                }

                record.LegsSection = ACTOR_LEGS_SECTION;
                record.Body = CreateSource(index, XRayLtxDatabase.Unquote(system.GetValue(ACTOR_LEGS_SECTION, "visual")) ?? NO_SECTION_BODY);
            }

            if (!record.Hands.Exists)
            {
                record.Resolution = FirstPersonOutfitResolution.HandsMissing;
            }
            else if (!record.Body.Exists)
            {
                record.Resolution = FirstPersonOutfitResolution.BodyMissing;
            }

            return record;
        }

        private static FirstPersonModelSource CreateSource(XRayAssetIndex index, string visual)
        {
            if (string.IsNullOrEmpty(visual))
            {
                return new FirstPersonModelSource(visual, null, null);
            }

            string path = ToMeshPath(visual);
            index.TryGet(path, out XRayIndexedFile file);
            return new FirstPersonModelSource(visual, path, file);
        }

        private static void ReadOptions(XRayAssetIndex index, FirstPersonOutfitScan scan)
        {
            if (!index.TryGet(OPTIONS_PATH, out XRayIndexedFile _))
            {
                scan.Warnings.Add($"'{OPTIONS_PATH}' not found; swm_legs MCM options use script defaults.");
                return;
            }

            XRayLtxDatabase options = XRayLtxDatabase.LoadFile(index, OPTIONS_PATH);
            foreach (XRayLtxSection section in options.Sections.Values)
            {
                foreach (KeyValuePair<string, string> pair in section.Values)
                {
                    if (pair.Key.StartsWith(SWM_OPTION_PREFIX, StringComparison.Ordinal))
                    {
                        scan.SwmOptions[pair.Key.Substring(SWM_OPTION_PREFIX.Length)] = pair.Value;
                    }
                }
            }

            scan.UseHdModels = GetBool(scan, "use_hd_models", false);
        }

        public static bool GetBool(FirstPersonOutfitScan scan, string key, bool fallback)
        {
            return scan.SwmOptions.TryGetValue(key, out string value) ? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) : fallback;
        }

        private static void ReadScript(XRayAssetIndex index, FirstPersonOutfitScan scan)
        {
            if (!index.TryGet(SCRIPT_PATH, out XRayIndexedFile file))
            {
                scan.Warnings.Add($"'{SCRIPT_PATH}' not found; the body animation tables are empty.");
                scan.Script = FirstPersonSwmScript.Parse(string.Empty);
                return;
            }

            byte[] data = File.ReadAllBytes(index.GetFullPath(file.Winner));
            scan.ScriptVirtualPath = SCRIPT_PATH;
            scan.ScriptSource = index.GetSource(file.Winner).Name;
            scan.Script = FirstPersonSwmScript.Parse(Cp1251.Decode(data, 0, data.Length));
            scan.Warnings.AddRange(scan.Script.Warnings);
        }

        private static void ReadCameraHeight(XRayLtxDatabase system, FirstPersonOutfitScan scan)
        {
            string factor = system.GetValue(ACTOR_SECTION, "camera_height_factor");
            string size = system.GetValue(ACTOR_SECTION, "ph_box0_size");
            string[] parts = size != null ? size.Split(',') : new string[0];
            if (factor != null && parts.Length == 3 && float.TryParse(factor, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float k) && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float halfHeight))
            {
                scan.CameraHeight = k * halfHeight * 2.0f;
                scan.CameraHeightOrigin = $"[actor] camera_height_factor = {factor}, ph_box0_size = {size}";
                return;
            }

            scan.Warnings.Add("Actor camera height could not be read from [actor]; CameraRoot stays at the rig origin.");
        }
    }
}
