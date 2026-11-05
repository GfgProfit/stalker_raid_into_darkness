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
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public static partial class XRayWeatherImporter
    {
        public const string AUTOMATIC_ELIGIBILITY_METRIC = "AutomaticWeatherEligible";

        private const string WEATHER_ROOT = "configs/environment/weathers/";
        private const string SUN_POSITIONS = "configs/environment/sun_positions.ltx";
        private const string WIND_WRITER_HASH = "256cb7279f2fb28e7999208ed7e687bd60c9f0b32363015982a7803d36360bad";
        private const string LIGHT_WRITER_HASH = "4ec6960c689500e350ae51c436cb575eb6283c4e69827c1cf4064d71cac300ab";
        private const string FLORA_WRITER_HASH = "08e723c06f23a6bbb95b39c793cc9cc19b6d818d83c30a5bfe91bcfd1c8013bd";
        private const string MCM_READER_HASH = "1cabb8dfcf6c3a0a3130fa672ff403c463df1d1f96e8a680ad55aa5ca7273836";
        private const string WEATHER_WRITER_HASH = "860fe1ca0ea658c88882befdba882092ebdb933ffe7fa8f7dddc60f7a771bec5";

        private static readonly string[] ORDINARY_CYCLES =
        {
            "w_clear1", "w_clear2", "w_clear3",
            "w_partly1", "w_partly2", "w_partly3",
            "w_cloudy1", "w_cloudy2", "w_cloudy2_dark", "w_cloudy3_dark",
            "w_foggy1", "w_foggy2", "w_foggy3",
            "w_cloudy3", "w_rain1", "w_rain2", "w_rain3", "w_rain4",
            "w_storm1", "w_storm2", "w_storm3"
        };
        private static readonly Regex INCLUDE = new Regex("^#include\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);

        private static IEnumerator<UnityEngine.Object> _preparation;

        public static XRayWeatherProfile ImportCurrent(string cycleName, string assetPath = null, string resourceFolder = null)
        {
            if (!string.IsNullOrEmpty(assetPath) && (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Weather assets must use an explicit Assets/.../*.asset destination.", nameof(assetPath));
            if (!string.IsNullOrEmpty(assetPath) && (File.Exists(assetPath) || AssetDatabase.LoadMainAssetAtPath(assetPath) != null)) throw new IOException("Weather import preserves existing assets; select a new asset path: " + assetPath);
            return ImportSource(CreateCurrentPlan(), cycleName, assetPath, resourceFolder);
        }

        private static XRaySourcePlan CreateCurrentPlan()
        {
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "configs", "scripts", "textures/sky" });
            return Mo2VirtualFileSystem.Plan(configuration);
        }

        public static XRayWeatherProfile ImportSource(XRaySourcePlan plan, string cycleName, string assetPath = null, string resourceFolder = null)
        {
            if (!string.IsNullOrEmpty(assetPath) && (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Weather assets must use an explicit Assets/.../*.asset destination.", nameof(assetPath));
            if (!string.IsNullOrEmpty(assetPath) && (File.Exists(assetPath) || AssetDatabase.LoadMainAssetAtPath(assetPath) != null)) throw new IOException("Weather import preserves existing assets; select a new asset path: " + assetPath);
            Func<string, XRayWeatherProfile> import = OpenSource(plan, resourceFolder, out _);
            XRayWeatherProfile profile = import(cycleName);
            if (!string.IsNullOrEmpty(assetPath)) SaveProfile(profile, assetPath);
            return profile;
        }

        private static Func<string, XRayWeatherProfile> OpenSource(XRaySourcePlan plan, string resourceFolder, out string commonProvenance, string reuseRoot = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
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
            Vector2? environmentScale = null;
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
                        if (console.TryGetValue("r2_sun_lumscale_amb", out string ambientScale) && console.TryGetValue("r2_sun_lumscale_hemi", out string hemisphereScale)) environmentScale = new Vector2(Numbers(ambientScale, 1, 1)[0], Numbers(hemisphereScale, 1, 1)[0]);
                        if (VerifiedWriter(source, provenance, "scripts/ssfx_wind.script", WIND_WRITER_HASH)) wind = Reconcile(console, saved, "ssfx_wind_trees", new[] { "ssfx_module/wind/trees_speed_mcm", "ssfx_module/wind/trees_trunk_mcm", "ssfx_module/wind/trees_bend_mcm", "ssfx_module/wind/min_speed_mcm" });
                        if (VerifiedWriter(source, provenance, "scripts/ssfx_wetness.script", LIGHT_WRITER_HASH)) light = Reconcile(console, saved, "ssfx_lightsetup_1", new[] { "ssfx_module/ssfx_wetness/ssfx_gloss/specular_int_mcm", "ssfx_module/ssfx_wetness/ssfx_gloss/specular_color_mcm" });
                        if (VerifiedWriter(source, provenance, "scripts/ssfx_florafixes.script", FLORA_WRITER_HASH)) flora = Reconcile(console, saved, "ssfx_florafixes_2", new[] { "ssfx_module/florafixes/sss_int_mcm", "ssfx_module/florafixes/sss_color_mcm" });
                    }
                }
            }
            if (!savedSettingsConfirmed) throw new InvalidDataException("INPUT BLOCKED: current weather import requires resolved app_data_root, readable user.ltx and effective axr_options.ltx, and a hash-verified ssfx_001_mcm.script reader.");
            provenance.AppendLine("settings=saved user/MCM agreement with hash-verified writers; not a live-game sample; no Lua execution");
            commonProvenance = provenance.ToString();
            string importProvenance = commonProvenance;
            XRayWeatherResourceImporter resources = string.IsNullOrEmpty(resourceFolder) ? null : new XRayWeatherResourceImporter(index, resourceFolder, reuseRoot);
            return cycle => Import(index, cycle, plan.ProfileName + "|" + plan.ModOrderFingerprint, importProvenance, wind, light, flora, resources == null ? null : resources.Bind, sunScale, visibilityScale, environmentScale, resources == null ? null : resources.Bind);
        }

        private static void SaveProfile(ScriptableObject profile, string assetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            if (!AssetDatabase.IsValidFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'))) AssetDatabase.Refresh();
            AssetDatabase.CreateAsset(profile, assetPath);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        [MenuItem("Tools/X-Ray/Weather/Prepare Ordinary Weather Catalog")]
        public static void PrepareOrdinaryCatalog()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || _preparation != null) throw new InvalidOperationException("Prepare the weather catalog once, outside Play Mode.");
            XRaySourcePlan plan = CreateCurrentPlan();
            _preparation = ResolveCatalog(plan, CurrentAssetRoot(plan), true).GetEnumerator();
            EditorApplication.update += AdvancePreparation;
        }

        private static void AdvancePreparation()
        {
            try
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && _preparation.MoveNext())
                {
                    UnityEngine.Object catalog = _preparation.Current;
                    if (catalog == null) return;
                    string count = catalog is XRayWeatherCatalog ordinary ? ordinary.Profiles.Count + " cycles" : ((XRayWeatherFxCatalog)catalog).Effects.Count + " effects";
                    Debug.Log("[X-Ray Weather] Prepared " + count + " at " + AssetDatabase.GetAssetPath(catalog));
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            _preparation.Dispose();
            _preparation = null;
            EditorApplication.update -= AdvancePreparation;
        }

        public static XRayWeatherCatalog LoadPreparedCatalog()
        {
            XRaySourcePlan plan = CreateCurrentPlan();
            return LoadCatalog(plan, CurrentAssetRoot(plan));
        }

        private static XRayWeatherCatalog LoadCatalog(XRaySourcePlan plan, string assetRoot)
        {
            foreach (XRayWeatherCatalog catalog in ResolveCatalog(plan, assetRoot, false)) return catalog;
            return null;
        }

        private static string CurrentAssetRoot(XRaySourcePlan plan)
        {
            if (!plan.IsValid || plan.Installation == null) throw new InvalidDataException("Cannot resolve current weather source: " + string.Join("; ", plan.Errors));
            return XRayLevelPaths.SpaceRoot(XRayLevelSourceCache.CreateProfileId(plan.Installation.InstanceFolder, plan.ProfileName));
        }

        private static string WeatherFolder(XRaySourcePlan plan, string assetRoot)
        {
            string settingsHash = "missing";
            if (plan.FsGame != null && plan.FsGame.TryGet(XRayFsGame.APP_DATA_ROOT, out XRayFsPath appdata) && !appdata.IsExternal)
            {
                string user = Path.GetFullPath(Path.Combine(plan.GameFolder, appdata.RelativePath, "user.ltx"));
                if (File.Exists(user)) settingsHash = XRayLevelGamedata.ComputeHash(user).Substring(0, 8);
            }
            return assetRoot + "/WeatherProfiles/" + plan.Fingerprint.Substring(0, 12) + "_" + settingsHash;
        }

        private static IEnumerable<XRayWeatherCatalog> ResolveCatalog(XRaySourcePlan plan, string assetRoot, bool prepare)
        {
            string folder = WeatherFolder(plan, assetRoot);
            string catalogPath = folder + "/OrdinaryWeatherCatalog.asset";
            XRayWeatherCatalog catalog = AssetDatabase.LoadAssetAtPath<XRayWeatherCatalog>(catalogPath);
            if (!prepare && catalog == null && !File.Exists(catalogPath)) yield break;
            if (catalog == null && File.Exists(catalogPath)) throw new InvalidDataException("Weather catalog destination is occupied: " + catalogPath);
            Func<string, XRayWeatherProfile> import = OpenSource(plan, prepare ? folder + "/Textures" : null, out string provenance, assetRoot + "/WeatherProfiles");
            var profiles = new XRayWeatherProfile[ORDINARY_CYCLES.Length];
            for (int i = 0; i < profiles.Length; i++)
            {
                string cycle = ORDINARY_CYCLES[i];
                string path = folder + "/" + cycle + ".asset";
                XRayWeatherProfile profile = AssetDatabase.LoadAssetAtPath<XRayWeatherProfile>(path);
                if (profile == null && (!prepare || File.Exists(path))) throw new InvalidDataException("Missing or incompatible cached weather profile: " + path);
                if (profile != null && (profile.CycleName != cycle || profile.SourceIdentity != plan.ProfileName + "|" + plan.ModOrderFingerprint || !profile.Provenance.StartsWith(provenance, StringComparison.Ordinal) || !HasSerializedResources(profile))) throw new InvalidDataException("Preserve or replace the incompatible cached weather profile explicitly: " + path);
                if (prepare)
                {
                    XRayWeatherProfile imported = import(cycle);
                    if (!HasSerializedResources(imported))
                    {
                        UnityEngine.Object.DestroyImmediate(imported);
                        throw new InvalidDataException("The cycle has incomplete sky or environment resources: " + cycle);
                    }
                    if (profile == null)
                    {
                        SaveProfile(imported, path);
                        profile = imported;
                    }
                    else
                    {
                        bool compatible = EditorJsonUtility.ToJson(profile) == EditorJsonUtility.ToJson(imported);
                        UnityEngine.Object.DestroyImmediate(imported);
                        if (!compatible) throw new InvalidDataException("Preserve or replace the incompatible cached weather profile explicitly: " + path);
                    }
                }
                profiles[i] = profile;
                if (prepare) yield return null;
            }
            if (catalog != null)
            {
                if (catalog.Profiles.Count != profiles.Length) throw new InvalidDataException("Preserve or replace the incompatible cached weather catalog explicitly: " + catalogPath);
                for (int i = 0; i < profiles.Length; i++)
                {
                    if (catalog.Profiles[i] != profiles[i] || !catalog.IsCompatible(profiles[i])) throw new InvalidDataException("Preserve or replace the incompatible cached weather catalog explicitly: " + catalogPath);
                }
                yield return catalog;
                yield break;
            }
            catalog = ScriptableObject.CreateInstance<XRayWeatherCatalog>();
            catalog.Initialize(profiles);
            AssetDatabase.CreateAsset(catalog, catalogPath);
            AssetDatabase.SaveAssetIfDirty(catalog);
            yield return catalog;
        }

        public static XRayWeatherProfile ResolveLevelProfile(XRayLevelImportSettings settings, XRaySourcePlan sourcePlan, string assetRoot, XRayLevelImportRecord record, bool allowAutomatic = true)
        {
            return ResolveLevelProfile(settings, sourcePlan, assetRoot, record, out _, allowAutomatic);
        }

        public static XRayWeatherProfile ResolveLevelProfile(XRayLevelImportSettings settings, XRaySourcePlan sourcePlan, string assetRoot, XRayLevelImportRecord record, out XRayWeatherCatalog catalog, bool allowAutomatic = true)
        {
            XRayWeatherProfile profile = ResolveLevelProfile(settings, record, out bool inheritWeather, allowAutomatic);
            catalog = inheritWeather ? GameConfig.Load().OrdinaryWeatherCatalog : null;
            return profile;
        }

        public static XRayWeatherProfile ResolveLevelProfile(XRayLevelImportSettings settings, XRayLevelImportRecord record, out bool inheritWeather, bool allowAutomatic = true)
        {
            inheritWeather = false;
            if (settings.WeatherImportMode == XRayWeatherImportMode.Disabled) return null;
            if (settings.WeatherProfile != null) return settings.WeatherProfile;
            if (settings.WeatherImportMode == XRayWeatherImportMode.CustomProfile)
            {
                record?.AddLimitation("Select an explicit profile for Custom Profile weather import.");
                return null;
            }
            if (!allowAutomatic)
            {
                record?.AddLimitation("Automatic weather preserves saved scenes imported before weather automation. Use Connect Active Scene Game Weather to add missing links.");
                return null;
            }
            GameConfig config = RequirePreparedGameConfig();
            inheritWeather = true;
            record?.AddDependency(XRayLevelDependencyCategory.Lighting, "GameConfig", config.name, null, null, XRayLevelDependencyStatus.Reused, "Shared runtime weather configuration", AssetDatabase.GetAssetPath(config));
            record?.AddDependency(XRayLevelDependencyCategory.Lighting, "Ordinary Weather Catalog", config.OrdinaryWeatherCatalog.name, null, null, XRayLevelDependencyStatus.Reused, "Global ordinary weather cycles", AssetDatabase.GetAssetPath(config.OrdinaryWeatherCatalog));
            record?.AddDependency(XRayLevelDependencyCategory.Lighting, "Weather FX Catalog", config.WeatherFxCatalog.name, null, null, XRayLevelDependencyStatus.Reused, "Global authored weather effects", AssetDatabase.GetAssetPath(config.WeatherFxCatalog));
            return config.DefaultWeather;
        }

        internal static GameConfig RequirePreparedGameConfig()
        {
            GameConfig config = GameConfig.Load();
            if (config == null) throw new InvalidDataException("The canonical Resources GameConfig.asset is required. Restore the Production configuration asset before importing a level.");
            if (!config.TryValidate(out string diagnostic)) throw new InvalidDataException("GameConfig is invalid: " + diagnostic + " Use the existing Tools/X-Ray/Weather catalog preparation commands to restore missing assets, then assign them in GameConfig.");
            if (config.CelestialSettings.Shader == null) throw new InvalidDataException("GameConfig celestial settings require the imported celestial shader. Use Tools/X-Ray/Levels/Import Celestial Weather Resources.");
            foreach (XRayWeatherProfile profile in config.OrdinaryWeatherCatalog.Profiles)
            {
                if (!HasSerializedResources(profile)) throw new InvalidDataException("GameConfig ordinary weather requires complete serialized sky and environment resources. Restore the prepared Ordinary Weather Catalog.");
            }
            foreach (XRayWeatherFxProfile effect in config.WeatherFxCatalog.Effects)
            {
                if (effect == null || !HasSerializedFxResources(effect, config.CelestialSettings)) throw new InvalidDataException("GameConfig WeatherFX requires complete serialized sky, environment and celestial resources. Restore the prepared Weather FX Catalog.");
            }
            return config;
        }

        public static bool IsAutomaticEligible(bool sceneExists, XRayLevelImportRecord previous)
        {
            return !sceneExists || previous != null && previous.GetMetric(AUTOMATIC_ELIGIBILITY_METRIC) > 0.5;
        }

        public static bool HasSerializedResources(XRayWeatherProfile profile)
        {
            if (profile == null || !profile.Validate(out string diagnostic)) return false;
            for (int frame = 0; frame < profile.KeyframeCount; frame++)
            {
                if (!HasSerializedSkyResources(profile.GetKeyframe(frame))) return false;
            }
            return true;
        }

        private static bool HasSerializedSkyResources(XRayWeatherSnapshot snapshot)
        {
            if (Mathf.Abs(Vector4.Dot(snapshot.SkyResources.Weights, Vector4.one) - 1.0f) > 0.0001f || Mathf.Abs(Vector4.Dot(snapshot.EnvironmentResources.Weights, Vector4.one) - 1.0f) > 0.0001f) return false;
            for (int resource = 0; resource < 4; resource++)
            {
                if (snapshot.SkyResources.Weights[resource] > 0.0f && !(snapshot.SkyResources.Texture(resource) is Cubemap)) return false;
                if (snapshot.EnvironmentResources.Weights[resource] > 0.0f && !(snapshot.EnvironmentResources.Texture(resource) is Cubemap)) return false;
            }
            return true;
        }

        public static XRayWeatherProfile Import(XRayAssetIndex index, string cycleName, string sourceIdentity, string sourceProvenance = "", Vector4? ssfxWindTrees = null, Vector4? lightSetup = null, Vector4? floraFixes = null, Func<string, Texture> resourceBinding = null, Vector3? sunLightScale = null, float visibilityScale = 1.0f, Vector2? environmentScale = null, Func<string, Texture> environmentBinding = null)
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
            XRayWeatherSnapshot[] frames = ReadFrames(source, descriptors, weather, provenance, resourceBinding, environmentBinding);
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
                profile.Initialize(cycleName, sourceIdentity, provenance.ToString(), frames, sun, ssfxWindTrees, lightSetup, floraFixes, sunLightScale, visibilityScale, environmentScale);
                return profile;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(profile);
                throw;
            }
        }

        private static XRayWeatherSnapshot[] ReadFrames(XRayEffectiveGamedata source, XRayLtxDatabase descriptors, string weather, StringBuilder provenance, Func<string, Texture> resourceBinding, Func<string, Texture> environmentBinding)
        {
            bool isFx = weather.StartsWith(WEATHER_FX_ROOT, StringComparison.Ordinal);
            var frames = new List<XRayWeatherSnapshot>();
            foreach (string key in descriptors.Sections.Keys)
            {
                if (!TryTime(key, out double seconds)) continue;
                string sky = Required(descriptors, key, "sky_texture");
                string clouds = Required(descriptors, key, "clouds_texture");
                TrackResource(source, provenance, sky);
                TrackResource(source, provenance, clouds);
                string environment = string.IsNullOrEmpty(sky) ? string.Empty : sky + "#small";
                if (environmentBinding != null) TrackResource(source, provenance, environment);
                float[] cloudValues = Numbers(Required(descriptors, key, "clouds_color"), 4, 5);
                float multiplier = cloudValues.Length == 5 ? cloudValues[4] * 0.5f : 0.0f;
                var cloudColor = new Color(cloudValues[0] * multiplier, cloudValues[1] * multiplier, cloudValues[2] * multiplier, cloudValues[3]);
                Color sunColor = ReadSunColor(descriptors, key, weather, provenance);
                float rainDensity = Number(descriptors, key, "rain_density");
                float effectiveRainDensity = isFx ? Mathf.Clamp01(rainDensity) : rainDensity;
                if (rainDensity != effectiveRainDensity) provenance.AppendLine("monolith-rain-density-clamp=" + weather + "/" + key + "; source=" + rainDensity.ToString(CultureInfo.InvariantCulture) + "; effective=" + effectiveRainDensity.ToString(CultureInfo.InvariantCulture));
                frames.Add(new XRayWeatherSnapshot(key, seconds, sunColor, ColorValue(descriptors, key, "ambient_color", 3), ColorValue(descriptors, key, "hemisphere_color", 4), Number(descriptors, key, "wind_direction") * Mathf.Deg2Rad, Number(descriptors, key, "wind_velocity"), Number(descriptors, key, "tree_amplitude_intensity"), effectiveRainDensity, ColorValue(descriptors, key, "rain_color", 3), ColorValue(descriptors, key, "fog_color", 3), Number(descriptors, key, "fog_density"), Number(descriptors, key, "fog_distance"), Number(descriptors, key, "far_plane"), ColorValue(descriptors, key, "sky_color", 3), cloudColor, OptionalNumber(descriptors, key, "sky_rotation", 0.0f) * Mathf.Deg2Rad, new XRayWeatherResources(sky, resourceBinding?.Invoke(sky)), new XRayWeatherResources(clouds, resourceBinding?.Invoke(clouds)), Required(descriptors, key, "sun"), Required(descriptors, key, "ambient"), Required(descriptors, key, "thunderbolt_collection"), Number(descriptors, key, "thunderbolt_period"), Number(descriptors, key, "thunderbolt_duration"), Number(descriptors, key, "sun_shafts_intensity"), Number(descriptors, key, "water_intensity"), new XRayWeatherResources(environment, environmentBinding?.Invoke(environment))));
            }
            frames.Sort((a, b) => a.GameSeconds.CompareTo(b.GameSeconds));
            return frames.ToArray();
        }

        private static Color ReadSunColor(XRayLtxDatabase descriptors, string key, string weather, StringBuilder provenance)
        {
            if (weather == WEATHER_FX_ROOT + "fx_super_blowout_night.ltx" && key == "00:07:12" && Required(descriptors, key, "sun_color") == "w, 0, 0")
            {
                provenance.AppendLine("monolith-zero-vector-scanf=" + weather + "/" + key + "; source=w, 0, 0; effective=0, 0, 0");
                return Color.black;
            }
            return ColorValue(descriptors, key, "sun_color", 3);
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
