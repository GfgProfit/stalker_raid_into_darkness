using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.IO;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayWeatherImporter
    {
        private const string WEATHER_ROOT = "configs/environment/weathers/";
        private const string SUN_POSITIONS = "configs/environment/sun_positions.ltx";
        private const string WIND_WRITER_HASH = "256cb7279f2fb28e7999208ed7e687bd60c9f0b32363015982a7803d36360bad";
        private const string LIGHT_WRITER_HASH = "4ec6960c689500e350ae51c436cb575eb6283c4e69827c1cf4064d71cac300ab";
        private const string FLORA_WRITER_HASH = "08e723c06f23a6bbb95b39c793cc9cc19b6d818d83c30a5bfe91bcfd1c8013bd";
        private const string MCM_READER_HASH = "1cabb8dfcf6c3a0a3130fa672ff403c463df1d1f96e8a680ad55aa5ca7273836";
        private const string WEATHER_WRITER_HASH = "860fe1ca0ea658c88882befdba882092ebdb933ffe7fa8f7dddc60f7a771bec5";

        private static readonly Regex INCLUDE = new Regex("^#include\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);

        public static XRayWeatherProfile ImportCurrent(string cycleName, string assetPath = null)
        {
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "configs", "scripts", "textures/sky" });
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            if (!plan.IsValid || plan.MissingModFolders.Count != 0) throw new InvalidDataException("Cannot resolve current weather source: " + string.Join("; ", plan.Errors));
            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            index.ExtractionFolder = Path.Combine(XRayLevelPaths.Mo2Cache, "WeatherArchiveCache");
            var provenance = new StringBuilder();
            provenance.AppendLine("schema=1; target=Rvm; public-engine-model=xray-monolith@313a751d; executable equivalence unproven; mode=explicit-cycle");
            provenance.AppendLine("profile=" + plan.ProfileName + "; modOrder=" + plan.ModOrderFingerprint + "; game=" + plan.GameFolder);
            AddPhysical(provenance, plan.Installation.IniPath);
            AddPhysical(provenance, plan.FsGamePath);
            AddPhysical(provenance, Path.Combine(plan.Installation.ProfilesDirectory, plan.ProfileName, "modlist.txt"));
            var source = new XRayEffectiveGamedata(index);
            Vector4? wind = null;
            Vector4? light = null;
            Vector4? flora = null;
            Vector3? sunScale = null;
            float visibilityScale = 1.0f;
            bool savedSettingsConfirmed = false;
            if (plan.FsGame != null && plan.FsGame.TryGet(XRayFsGame.APP_DATA_ROOT, out XRayFsPath appdata) && !appdata.IsExternal)
            {
                string userPath = Path.GetFullPath(Path.Combine(plan.GameFolder, appdata.RelativePath, "user.ltx"));
                if (File.Exists(userPath) && source.Exists("configs/axr_options.ltx"))
                {
                    byte[] mcm = source.ReadAllBytes("configs/axr_options.ltx");
                    string user = File.ReadAllText(userPath);
                    AddPhysical(provenance, userPath);
                    AddEffective(provenance, source, "configs/axr_options.ltx");
                    if (VerifiedWriter(source, provenance, "scripts/ssfx_001_mcm.script", MCM_READER_HASH))
                    {
                        Dictionary<string, string> saved = ReadSettings(Cp1251.Decode(mcm, 0, mcm.Length));
                        Dictionary<string, string> console = ReadSettings(user);
                        ValidateBrightness(index, source, provenance, saved);
                        savedSettingsConfirmed = true;
                        if (!console.TryGetValue("rs_vis_distance", out string visibility)) throw new InvalidDataException("Missing saved source visibility scale.");
                        visibilityScale = Numbers(visibility, 1, 1)[0];
                        if (console.TryGetValue("r2_sun_lumscale", out string sunLumscale) && console.TryGetValue("r2_sun_lumscale_color", out string sunLumscaleColor))
                        {
                            float scale = Numbers(sunLumscale, 1, 1)[0];
                            float[] color = Numbers(sunLumscaleColor, 3, 3);
                            sunScale = new Vector3(color[0], color[1], color[2]) * scale;
                        }
                        if (VerifiedWriter(source, provenance, "scripts/ssfx_wind.script", WIND_WRITER_HASH)) wind = Reconcile(console, saved, "ssfx_wind_trees", new[] { "ssfx_module/wind/trees_speed_mcm", "ssfx_module/wind/trees_trunk_mcm", "ssfx_module/wind/trees_bend_mcm", "ssfx_module/wind/min_speed_mcm" });
                        if (VerifiedWriter(source, provenance, "scripts/ssfx_wetness.script", LIGHT_WRITER_HASH)) light = Reconcile(console, saved, "ssfx_lightsetup_1", new[] { "ssfx_module/ssfx_wetness/ssfx_gloss/specular_int_mcm", "ssfx_module/ssfx_wetness/ssfx_gloss/specular_color_mcm" });
                        if (VerifiedWriter(source, provenance, "scripts/ssfx_florafixes.script", FLORA_WRITER_HASH)) flora = Reconcile(console, saved, "ssfx_florafixes_2", new[] { "ssfx_module/florafixes/sss_int_mcm", "ssfx_module/florafixes/sss_color_mcm" });
                    }
                }
            }
            if (!savedSettingsConfirmed) throw new InvalidDataException("INPUT BLOCKED: current weather import requires resolved app_data_root, readable user.ltx and effective axr_options.ltx, and a hash-verified ssfx_001_mcm.script reader.");
            provenance.AppendLine("settings=saved user/MCM agreement with hash-verified writers; not a live-game sample; no Lua execution");
            XRayWeatherProfile profile = Import(index, cycleName, plan.ProfileName + "|" + plan.ModOrderFingerprint, provenance.ToString(), wind, light, flora, null, sunScale, visibilityScale);
            if (string.IsNullOrEmpty(assetPath)) return profile;
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Object.DestroyImmediate(profile);
                throw new ArgumentException("Weather assets must use an explicit Assets/.../*.asset destination.", nameof(assetPath));
            }
            if (File.Exists(assetPath) || AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                UnityEngine.Object.DestroyImmediate(profile);
                throw new IOException("Weather import preserves existing assets; select a new asset path: " + assetPath);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            AssetDatabase.Refresh();
            AssetDatabase.CreateAsset(profile, assetPath);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        public static XRayWeatherProfile Import(XRayAssetIndex index, string cycleName, string sourceIdentity, string sourceProvenance = "", Vector4? ssfxWindTrees = null, Vector4? lightSetup = null, Vector4? floraFixes = null, Func<string, Texture> resourceBinding = null, Vector3? sunLightScale = null, float visibilityScale = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(cycleName) || Path.GetFileName(cycleName) != cycleName || cycleName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) throw new ArgumentException("A weather cycle must be an unqualified source filename without extension.", nameof(cycleName));
            string weather = WEATHER_ROOT + cycleName + ".ltx";
            var source = new XRayEffectiveGamedata(index);
            var provenance = new StringBuilder(sourceProvenance);
            var loaded = new HashSet<string>(StringComparer.Ordinal);
            ValidateInput(source, weather, loaded, new HashSet<string>(StringComparer.Ordinal), provenance);
            ValidateInput(source, SUN_POSITIONS, loaded, new HashSet<string>(StringComparer.Ordinal), provenance);
            XRayLtxDatabase descriptors = XRayLtxDatabase.LoadFile(index, weather);
            XRayLtxDatabase positions = XRayLtxDatabase.LoadFile(index, SUN_POSITIONS);
            ValidateDatabase(descriptors);
            ValidateDatabase(positions);
            var frames = new List<XRayWeatherSnapshot>();
            foreach (string key in descriptors.Sections.Keys)
            {
                if (!TryTime(key, out double seconds)) continue;
                string sky = Required(descriptors, key, "sky_texture");
                string clouds = Required(descriptors, key, "clouds_texture");
                TrackResource(source, provenance, sky);
                TrackResource(source, provenance, clouds);
                float[] cloudValues = Numbers(Required(descriptors, key, "clouds_color"), 4, 5);
                float multiplier = cloudValues.Length == 5 ? cloudValues[4] * 0.5f : 0.0f;
                var cloudColor = new Color(cloudValues[0] * multiplier, cloudValues[1] * multiplier, cloudValues[2] * multiplier, cloudValues[3]);
                frames.Add(new XRayWeatherSnapshot(key, seconds, ColorValue(descriptors, key, "sun_color", 3), ColorValue(descriptors, key, "ambient_color", 3), ColorValue(descriptors, key, "hemisphere_color", 4), Number(descriptors, key, "wind_direction") * Mathf.Deg2Rad, Number(descriptors, key, "wind_velocity"), Number(descriptors, key, "tree_amplitude_intensity"), Number(descriptors, key, "rain_density"), ColorValue(descriptors, key, "rain_color", 3), ColorValue(descriptors, key, "fog_color", 3), Number(descriptors, key, "fog_density"), Number(descriptors, key, "fog_distance"), Number(descriptors, key, "far_plane"), ColorValue(descriptors, key, "sky_color", 3), cloudColor, OptionalNumber(descriptors, key, "sky_rotation", 0.0f) * Mathf.Deg2Rad, new XRayWeatherResources(sky, resourceBinding?.Invoke(sky)), new XRayWeatherResources(clouds, resourceBinding?.Invoke(clouds)), Required(descriptors, key, "sun"), Required(descriptors, key, "ambient"), Required(descriptors, key, "thunderbolt_collection"), Number(descriptors, key, "thunderbolt_period"), Number(descriptors, key, "thunderbolt_duration"), Number(descriptors, key, "sun_shafts_intensity"), Number(descriptors, key, "water_intensity")));
            }
            frames.Sort((a, b) => a.GameSeconds.CompareTo(b.GameSeconds));
            var sun = new Vector2[24];
            for (int hour = 0; hour < sun.Length; hour++)
            {
                string key = hour.ToString("00", CultureInfo.InvariantCulture) + ":00:00";
                sun[hour] = new Vector2(Number(positions, key, "sun_altitude"), Number(positions, key, "sun_longitude"));
            }
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            try
            {
                profile.name = cycleName;
                profile.Initialize(cycleName, sourceIdentity, provenance.ToString(), frames.ToArray(), sun, ssfxWindTrees, lightSetup, floraFixes, sunLightScale, visibilityScale);
                return profile;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(profile);
                throw;
            }
        }

        private static void ValidateInput(XRayEffectiveGamedata source, string path, HashSet<string> loaded, HashSet<string> stack, StringBuilder provenance)
        {
            if (!stack.Add(path)) throw new InvalidDataException("Cyclic weather include: " + path);
            if (!loaded.Add(path))
            {
                stack.Remove(path);
                return;
            }
            if (!source.Exists(path)) throw new InvalidDataException("Missing weather include: " + path);
            string directory = path.Substring(0, path.LastIndexOf('/'));
            string overridePrefix = "mod_" + Path.GetFileNameWithoutExtension(path) + "_";
            foreach (XRayEffectiveFile file in source.Enumerate(directory, false))
            {
                string name = Path.GetFileName(file.VirtualPath);
                if (name.StartsWith(overridePrefix, StringComparison.Ordinal) && name.EndsWith(".ltx", StringComparison.Ordinal)) throw new InvalidDataException("INPUT BLOCKED: standalone weather DLTX override requires a verified merge path: " + file.VirtualPath);
            }
            AddEffective(provenance, source, path);
            byte[] data = source.ReadAllBytes(path);
            string text = Cp1251.Decode(data, 0, data.Length);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("![", StringComparison.Ordinal) || line.StartsWith("!![", StringComparison.Ordinal) || line.StartsWith("@[", StringComparison.Ordinal)) throw new InvalidDataException("Weather DLTX headers require a separately verified engine merge path: " + path);
                if (line.StartsWith("#include", StringComparison.OrdinalIgnoreCase))
                {
                    Match include = INCLUDE.Match(line);
                    if (!include.Success) throw new InvalidDataException("Invalid weather include: " + path);
                    string combined = NormalizeInclude(path, include.Groups[1].Value);
                    if (combined.IndexOfAny(new[] { '*', '?' }) >= 0)
                    {
                        var pattern = new Regex("^" + Regex.Escape(combined).Replace("\\*", "[^/]*").Replace("\\?", "[^/]") + "$");
                        var matches = new List<string>();
                        foreach (XRayIndexedFile file in source.Index.Files)
                        {
                            if (pattern.IsMatch(file.VirtualPath)) matches.Add(file.VirtualPath);
                        }
                        matches.Sort(StringComparer.Ordinal);
                        if (matches.Count == 0) throw new InvalidDataException("Missing weather wildcard include: " + combined);
                        foreach (string match in matches) ValidateInput(source, match, loaded, stack, provenance);
                    }
                    else ValidateInput(source, combined, loaded, stack, provenance);
                }
            }
            stack.Remove(path);
        }

        private static string NormalizeInclude(string path, string include)
        {
            string directory = path.Substring(0, path.LastIndexOf('/'));
            var parts = new List<string>();
            foreach (string part in (directory + "/" + include.Replace('\\', '/')).ToLowerInvariant().Split('/'))
            {
                if (part == "." || part.Length == 0) continue;
                if (part == "..")
                {
                    if (parts.Count <= 1) throw new InvalidDataException("Weather include leaves configs.");
                    parts.RemoveAt(parts.Count - 1);
                }
                else parts.Add(part);
            }
            return string.Join("/", parts);
        }

        private static void ValidateDatabase(XRayLtxDatabase database)
        {
            if (database.Warnings.Count > 0) throw new InvalidDataException(string.Join("; ", database.Warnings));
            foreach (XRayLtxSection section in database.Sections.Values) ValidateParents(database, section, new HashSet<string>(StringComparer.Ordinal));
        }

        private static void ValidateParents(XRayLtxDatabase database, XRayLtxSection section, HashSet<string> stack)
        {
            if (!stack.Add(section.Name)) throw new InvalidDataException("Cyclic weather inheritance: " + section.Name);
            foreach (string parent in section.Parents)
            {
                if (!database.Sections.TryGetValue(parent, out XRayLtxSection inherited)) throw new InvalidDataException("Missing weather parent: " + parent);
                ValidateParents(database, inherited, stack);
            }
            stack.Remove(section.Name);
        }

        private static bool TryTime(string key, out double seconds)
        {
            seconds = 0.0;
            if (!TimeSpan.TryParseExact(key, "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out TimeSpan time)) return false;
            seconds = time.TotalSeconds;
            return seconds < XRayWeatherProfile.DAY_SECONDS;
        }

        private static void ValidateBrightness(XRayAssetIndex index, XRayEffectiveGamedata source, StringBuilder provenance, Dictionary<string, string> saved)
        {
            if (!saved.TryGetValue("video/night/brightness", out string brightness) || !VerifiedWriter(source, provenance, "scripts/level_weathers.script", WEATHER_WRITER_HASH)) throw new InvalidDataException("INPUT BLOCKED: unverified weather brightness selection or writer.");
            const string GRAPHS = "configs/environment/dynamic_weather_graphs.ltx";
            ValidateInput(source, GRAPHS, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), provenance);
            XRayLtxDatabase database = XRayLtxDatabase.LoadFile(index, GRAPHS);
            ValidateDatabase(database);
            string selected = "brightness_" + brightness;
            if (!database.Sections.TryGetValue(selected, out XRayLtxSection section)) throw new InvalidDataException("INPUT BLOCKED: missing weather brightness profile " + selected);
            ValidateNeutralBrightness(database, section);
            provenance.AppendLine("brightness=" + brightness + "; verified selected and inherited modifier sections are empty; no color boost");
        }

        private static void ValidateNeutralBrightness(XRayLtxDatabase database, XRayLtxSection section)
        {
            if (section.Values.Count != 0 || section.Appended.Count != 0 || section.Removed.Count != 0 || section.Deleted.Count != 0) throw new InvalidDataException("INPUT BLOCKED: non-neutral weather brightness modifiers need an explicit imported boost model.");
            foreach (string parent in section.Parents) ValidateNeutralBrightness(database, database.Sections[parent]);
        }

        private static string Required(XRayLtxDatabase database, string key, string field)
        {
            return XRayLtxDatabase.Unquote(database.GetValue(key, field)) ?? throw new InvalidDataException("Missing weather input: " + key + "/" + field);
        }

        private static float Number(XRayLtxDatabase database, string key, string field)
        {
            return Numbers(Required(database, key, field), 1, 1)[0];
        }

        private static float OptionalNumber(XRayLtxDatabase database, string key, string field, float fallback)
        {
            string value = database.GetValue(key, field);
            return value == null ? fallback : Numbers(value, 1, 1)[0];
        }

        private static Color ColorValue(XRayLtxDatabase database, string key, string field, int count)
        {
            float[] values = Numbers(Required(database, key, field), count, count);
            return new Color(values[0], values[1], values[2], count == 4 ? values[3] : 1.0f);
        }

        private static float[] Numbers(string value, int minimum, int maximum)
        {
            string[] parts = value.Trim('(', ')', ' ').Split(',');
            if (parts.Length < minimum || parts.Length > maximum) throw new InvalidDataException("Invalid weather vector: " + value);
            var result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]) || float.IsNaN(result[i]) || float.IsInfinity(result[i])) throw new InvalidDataException("Invalid weather number: " + parts[i]);
            }
            return result;
        }

        private static void TrackResource(XRayEffectiveGamedata source, StringBuilder provenance, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            string path = XRayVirtualPath.TextureToVirtualPath(name, ".dds");
            if (source.Exists(path)) AddEffective(provenance, source, path);
            else provenance.AppendLine("RESOURCE INPUT MISSING: " + path);
        }

        private static bool VerifiedWriter(XRayEffectiveGamedata source, StringBuilder provenance, string path, string hash)
        {
            if (!source.Exists(path)) return false;
            AddEffective(provenance, source, path);
            return Hash(source.ReadAllBytes(path)) == hash;
        }

        private static Dictionary<string, string> ReadSettings(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Split(';')[0].Trim();
                int split = line.IndexOf('=');
                if (split < 0) split = line.IndexOfAny(new[] { ' ', '\t' });
                if (split <= 0 || line[0] == '[') continue;
                result[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }
            return result;
        }

        private static Vector4? Reconcile(Dictionary<string, string> console, Dictionary<string, string> saved, string command, string[] keys)
        {
            if (!console.TryGetValue(command, out string text)) return null;
            float[] values = Numbers(text, 4, 4);
            var expected = new Vector4();
            for (int i = 0; i < keys.Length; i++)
            {
                if (!saved.TryGetValue(keys[i], out string value)) return null;
                expected[i] = Numbers(value, 1, 1)[0];
            }
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(expected[i] - values[i]) > 0.000001f) return null;
            }
            return expected;
        }

        private static void AddEffective(StringBuilder provenance, XRayEffectiveGamedata source, string path)
        {
            XRayEffectiveFile file = source.Resolve(path);
            if (file == null) throw new InvalidDataException("Missing weather source: " + path);
            provenance.AppendLine(path + "|" + file.SourceName + "|" + file.Location + "|sha256=" + Hash(source.ReadAllBytes(path)));
        }

        private static void AddPhysical(StringBuilder provenance, string path)
        {
            provenance.AppendLine(path + "|sha256=" + Hash(File.ReadAllBytes(path)));
        }

        private static string Hash(byte[] bytes)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
