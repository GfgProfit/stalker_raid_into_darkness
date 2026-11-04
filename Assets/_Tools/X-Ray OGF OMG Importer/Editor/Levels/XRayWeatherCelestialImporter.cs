using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayWeatherCelestialImporter
    {
        public const string SETTINGS_ASSET = "Assets/_Tools/X-Ray OGF OMG Importer/Resources/XRayWeatherCelestial.asset";

        private const string PRODUCTION = @"E:\Projects\stalker_raid_into_darkness";
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";
        private const string TEXTURE_FOLDER = "Assets/_Tools/X-Ray OGF OMG Importer/CelestialTextures";
        private const string SUNS = "configs/environment/suns.ltx";
        private const string SHADER = "XRay/Monolith Weather Celestial";

        [MenuItem("Tools/X-Ray/Levels/Import Celestial Weather Resources")]
        private static void ImportMenu()
        {
            Debug.Log("Imported celestial weather resources. Evidence: " + ImportCurrent(Path.Combine(ARCHIVE, "SunMoonShaftsFlare_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture))));
        }

        public static string ImportCurrent(string evidenceFolder)
        {
            if (!string.Equals(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), PRODUCTION, StringComparison.OrdinalIgnoreCase) || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Import celestial resources in the Production Editor in Edit Mode.");
            if (string.IsNullOrWhiteSpace(evidenceFolder) || !Path.GetFullPath(evidenceFolder).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use a task directory under the Archive for import evidence.", nameof(evidenceFolder));
            string output = Path.Combine(evidenceFolder, "CelestialImport_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            try
            {
                return Import(output);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(output, "Failure.txt"), exception.ToString());
                throw;
            }
        }

        private static string Import(string output)
        {
            Shader shader = Shader.Find(SHADER);
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("The celestial HDRP shader must be imported and supported: " + SHADER);
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(SETTINGS_ASSET);
            if (existing != null && (!(existing is XRayWeatherCelestialSettings) || EditorUtility.IsDirty(existing))) throw new InvalidOperationException("Preserve existing authored or unsaved data at " + SETTINGS_ASSET);
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.Mo2Profile = string.Empty;
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "configs", "textures/anamflares", "textures/fx" });
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            if (!plan.IsValid || plan.MissingModFolders.Count != 0) throw new InvalidDataException("Cannot resolve effective celestial sources: " + string.Join("; ", plan.Errors) + " " + string.Join("; ", plan.MissingModFolders));
            if (plan.FsGame == null || !plan.FsGame.TryGet(XRayFsGame.APP_DATA_ROOT, out XRayFsPath appdata) || appdata.IsExternal) throw new InvalidDataException("Cannot resolve the saved source user.ltx under app_data_root.");
            string userPath = Path.GetFullPath(Path.Combine(plan.GameFolder, appdata.RelativePath, "user.ltx"));
            byte[] user = File.ReadAllBytes(userPath);
            var console = ReadConsoleSettings(File.ReadAllLines(userPath));
            string mode = RequireSetting(console, "r2_sunshafts_mode");
            string quality = RequireSetting(console, "r2_sunshafts_quality");
            string volumetric = RequireSetting(console, "r2_volumetric_lights");
            float minimum = Number(RequireSetting(console, "r2_sunshafts_min"), "r2_sunshafts_min");
            float multiplier = Number(RequireSetting(console, "r2_sunshafts_value"), "r2_sunshafts_value");
            bool screenSpace = mode == "screen_space";
            if (screenSpace && quality != "st_opt_high") throw new InvalidDataException("The current celestial shader implements screen-space shafts at st_opt_high quality.");
            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            index.ExtractionFolder = Path.Combine(output, "ResolvedArchiveFiles");
            var source = new XRayEffectiveGamedata(index);
            XRayLtxDatabase database = XRayLtxDatabase.LoadFile(index, SUNS);
            if (database.Sections.Count == 0 || database.Warnings.Count != 0) throw new InvalidDataException("Cannot load effective suns.ltx: " + string.Join("; ", database.Warnings));
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            ReadUsedDefinitions(source, "configs/environment/weathers", usedNames);
            ReadUsedDefinitions(source, "configs/environment/weather_effects", usedNames);
            foreach (string name in usedNames)
            {
                if (!database.HasSection(name)) throw new InvalidDataException("Weather refers to a missing celestial definition: " + name);
            }

            var catalog = new List<XRayWeatherFlareDefinition>();
            var resources = new XRayWeatherResourceImporter(index, TEXTURE_FOLDER);
            var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            var records = new List<TextureRecord>();
            var warnings = new List<string>();
            var definitions = new List<string>(database.Sections.Keys);
            var definitionSources = new SortedSet<string>(StringComparer.Ordinal) { SUNS };
            definitions.Sort(StringComparer.Ordinal);
            foreach (string name in definitions)
            {
                definitionSources.Add(database.Sections[name].DefinedIn);
                bool required = usedNames.Contains(name);
                Texture2D Resolve(string textureName)
                {
                    string path = XRayVirtualPath.TextureToVirtualPath(Path.ChangeExtension(textureName, null), ".dds");
                    if (!textures.TryGetValue(path, out Texture2D texture))
                    {
                        XRayEffectiveFile winner = source.Resolve(path);
                        if (winner != null)
                        {
                            texture = resources.Bind(Path.ChangeExtension(textureName, null)) as Texture2D;
                            if (texture == null || GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat)) throw new InvalidDataException("Expected a non-sRGB 2D flare texture: " + path);
                            string asset = AssetDatabase.GetAssetPath(texture);
                            records.Add(new TextureRecord { source = RecordSource(source, path), asset = asset, guid = AssetDatabase.AssetPathToGUID(asset), assetSha256 = Hash(File.ReadAllBytes(asset)), graphicsFormat = texture.graphicsFormat.ToString(), width = texture.width, height = texture.height, mipCount = texture.mipmapCount });
                        }
                        textures.Add(path, texture);
                    }
                    if (texture == null)
                    {
                        string error = name + " refers to a missing effective texture: " + path;
                        if (required) throw new InvalidDataException(error);
                        if (!warnings.Contains(error)) warnings.Add(error);
                    }
                    return texture;
                }
                catalog.Add(ReadDefinition(database, name, Resolve));
            }

            string folder = Path.GetDirectoryName(SETTINGS_ASSET).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            XRayWeatherCelestialSettings settings = existing as XRayWeatherCelestialSettings;
            if (settings == null) settings = ScriptableObject.CreateInstance<XRayWeatherCelestialSettings>();
            settings.Initialize(shader, minimum, multiplier, screenSpace, catalog.ToArray());
            if (existing == null) AssetDatabase.CreateAsset(settings, SETTINGS_ASSET);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            var sources = new List<SourceRecord>();
            foreach (string path in definitionSources) sources.Add(RecordSource(source, path));
            var referenced = new List<string>(usedNames);
            referenced.Sort(StringComparer.Ordinal);
            string manifestPath = Path.Combine(output, "ImportedResources.json");
            var manifest = new ImportRecord { profile = plan.ProfileName, enabledModCount = plan.ActiveModCount, modOrderFingerprint = plan.ModOrderFingerprint, userLtx = userPath, userLtxSha256 = Hash(user), shaftsMode = mode, shaftsQuality = quality, shaftsMin = minimum, shaftsValue = multiplier, volumetricLights = volumetric, settingsAsset = SETTINGS_ASSET, settingsGuid = AssetDatabase.AssetPathToGUID(SETTINGS_ASSET), settingsSha256 = Hash(File.ReadAllBytes(SETTINGS_ASSET)), shaderAsset = AssetDatabase.GetAssetPath(shader), definitions = definitions.ToArray(), referencedDefinitions = referenced.ToArray(), sources = sources.ToArray(), textures = records.ToArray(), warnings = warnings.ToArray() };
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            return manifestPath;
        }

        private static void ReadUsedDefinitions(XRayEffectiveGamedata source, string directory, HashSet<string> names)
        {
            foreach (XRayEffectiveFile file in source.Enumerate(directory, true))
            {
                if (!file.VirtualPath.EndsWith(".ltx", StringComparison.Ordinal)) continue;
                XRayLtxDatabase database = XRayLtxDatabase.LoadFile(source.Index, file.VirtualPath);
                foreach (string section in database.Sections.Keys)
                {
                    string name = XRayLtxDatabase.Unquote(database.GetValue(section, "sun"));
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim().ToLowerInvariant());
                }
            }
        }

        internal static XRayWeatherFlareDefinition ReadDefinition(XRayLtxDatabase database, string name, Func<string, Texture2D> resolve)
        {
            string Value(string key)
            {
                return XRayLtxDatabase.Unquote(database.GetValue(name, key));
            }

            float Parameter(string key)
            {
                return Number(Value(key), name + "." + key);
            }

            Texture2D Texture(string texture)
            {
                return string.IsNullOrWhiteSpace(texture) ? null : resolve(texture.Trim());
            }

            XRayWeatherFlareElement source = default;
            XRayWeatherFlareElement gradient = default;
            if (Enabled(Value("sun"))) source = new XRayWeatherFlareElement(Texture(Value("sun_texture")), Parameter("sun_radius"), 1.0f, 1.0f, Enabled(Value("sun_ignore_color")));
            if (Enabled(Value("gradient"))) gradient = new XRayWeatherFlareElement(Texture(Value("gradient_texture")), Parameter("gradient_radius"), 1.0f, Parameter("gradient_opacity"), false);
            var flares = new List<XRayWeatherFlareElement>();
            if (Enabled(Value("flares")))
            {
                string[] textures = (Value("flare_textures") ?? string.Empty).TrimEnd(',').Split(',');
                string[] radii = (Value("flare_radius") ?? string.Empty).Split(',');
                string[] positions = (Value("flare_position") ?? string.Empty).Split(',');
                string[] opacities = (Value("flare_opacity") ?? string.Empty).Split(',');
                for (int i = 0; i < textures.Length; i++)
                {
                    if (textures[i].Trim().Length == 0) break;
                    flares.Add(new XRayWeatherFlareElement(Texture(textures[i]), Item(radii, i), Item(positions, i), Item(opacities, i), false));
                }
            }
            return new XRayWeatherFlareDefinition(name, source, gradient, flares.ToArray(), Mathf.Max(0.0f, Parameter("blend_rise_time")), Mathf.Max(0.0f, Parameter("blend_down_time")));
        }

        private static Dictionary<string, string> ReadConsoleSettings(string[] lines)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in lines)
            {
                string line = raw.Split(';')[0].Trim();
                int split = line.IndexOfAny(new[] { ' ', '\t', '=' });
                if (split > 0) values[line.Substring(0, split)] = line.Substring(split + 1).TrimStart(' ', '\t', '=');
            }
            return values;
        }

        private static string RequireSetting(Dictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out string value) || string.IsNullOrEmpty(value)) throw new InvalidDataException("Missing source console setting " + key);
            return value;
        }

        private static float Item(string[] values, int index)
        {
            if (index >= values.Length || !float.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float result) || !float.IsFinite(result)) return 0.0f;
            return result;
        }

        private static float Number(string value, string label)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) || !float.IsFinite(result)) throw new InvalidDataException("Invalid source celestial number " + label + ": " + value);
            return result;
        }

        private static bool Enabled(string value)
        {
            return value != null && (value.Equals("on", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "1");
        }

        private static SourceRecord RecordSource(XRayEffectiveGamedata source, string virtualPath)
        {
            XRayEffectiveFile winner = source.Resolve(virtualPath);
            return new SourceRecord { virtualPath = virtualPath, winner = winner.SourceName, location = winner.Location, sha256 = Hash(source.ReadAllBytes(virtualPath)) };
        }

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        [Serializable]
        private sealed class ImportRecord
        {
            public string profile;
            public int enabledModCount;
            public string modOrderFingerprint;
            public string userLtx;
            public string userLtxSha256;
            public string shaftsMode;
            public string shaftsQuality;
            public float shaftsMin;
            public float shaftsValue;
            public string volumetricLights;
            public string settingsAsset;
            public string settingsGuid;
            public string settingsSha256;
            public string shaderAsset;
            public string[] definitions;
            public string[] referencedDefinitions;
            public SourceRecord[] sources;
            public TextureRecord[] textures;
            public string[] warnings;
        }

        [Serializable]
        private sealed class SourceRecord
        {
            public string virtualPath;
            public string winner;
            public string location;
            public string sha256;
        }

        [Serializable]
        private sealed class TextureRecord
        {
            public SourceRecord source;
            public string asset;
            public string guid;
            public string assetSha256;
            public string graphicsFormat;
            public int width;
            public int height;
            public int mipCount;
        }
    }
}
