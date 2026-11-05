using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Mo2;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayPrecipitationAssetSetup
    {
        public const string CONFIG_ASSET = "Assets/_Source/Settings/Resources/PrecipitationConfig.asset";
        public const string TEXTURE_FOLDER = "Assets/_Source/Settings/Resources/XRayPrecipitation";

        private const string PRODUCTION = "E:\\Projects\\stalker_raid_into_darkness";
        private const string ARCHIVE = "E:\\Projects\\stalker_raid_into_darkness_Archive";
        private const string ROOT_ASSET = "Assets/_Source/Settings/Resources/GameConfig.asset";
        private const string SHADER_FOLDER = "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/";
        private const string MILITARY_TERRAIN = "terrain_military_1";

        public static string PrepareRain(string archiveFolder)
        {
            return Prepare(archiveFolder, false);
        }

        public static string PrepareSurfaces(string archiveFolder)
        {
            return Prepare(archiveFolder, true);
        }

        private static string Prepare(string archiveFolder, bool includeSurfaces)
        {
            if (!string.Equals(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), PRODUCTION, StringComparison.OrdinalIgnoreCase) || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Prepare precipitation resources in the Production Editor in Edit Mode.");
            string archivePath = Path.GetFullPath(archiveFolder ?? string.Empty);
            if (!archivePath.StartsWith(ARCHIVE + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Precipitation resource evidence must be stored under the project Archive.", nameof(archiveFolder));
            GameConfig root = RequireClean<GameConfig>(ROOT_ASSET, false);
            PrecipitationConfig settings = RequireClean<PrecipitationConfig>(CONFIG_ASSET, true);
            Shader rainShader = RequireAsset<Shader>(SHADER_FOLDER + "MonolithPrecipitation.shader");
            Shader heightShader = RequireAsset<Shader>(SHADER_FOLDER + "MonolithPrecipitationHeight.shader");
            ComputeShader rainCompute = RequireAsset<ComputeShader>(SHADER_FOLDER + "XRayPrecipitation.compute");
            ComputeShader wetnessCompute = includeSurfaces ? RequireAsset<ComputeShader>(SHADER_FOLDER + "MonolithWetness.compute") : null;
            if (ShaderUtil.ShaderHasError(rainShader) || ShaderUtil.ShaderHasError(heightShader)) throw new InvalidOperationException("Precipitation shaders must compile before resource preparation.");
            var rootData = new SerializedObject(root);
            SerializedProperty rootReference = RequireProperty(rootData, "_precipitation");
            if (rootReference.objectReferenceValue != null && rootReference.objectReferenceValue != settings) throw new InvalidOperationException("Preserve the existing GameConfig precipitation reference.");
            string output = Path.Combine(archivePath, "ResourcePreparation_" + (includeSurfaces ? "Surfaces_" : "Rain_") + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.Mo2Profile = string.Empty;
            configuration.IndexScope.Clear();
            configuration.IndexScope.Add("textures/fx");
            if (includeSurfaces) configuration.IndexScope.Add("textures/terrain");
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            if (!plan.IsValid || plan.MissingModFolders.Count != 0) throw new InvalidDataException("Cannot resolve effective precipitation sources: " + string.Join("; ", plan.Errors) + " " + string.Join("; ", plan.MissingModFolders));
            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            index.ExtractionFolder = Path.Combine(output, "ResolvedArchiveFiles");
            var source = new XRayEffectiveGamedata(index);
            var importer = new XRayWeatherResourceImporter(index, TEXTURE_FOLDER, TEXTURE_FOLDER);
            var records = new List<ResourceRecord>();
            var data = new SerializedObject(settings);
            SetReference(data, "_rainShader", rainShader);
            SetReference(data, "_heightShader", heightShader);
            SetReference(data, "_rainCompute", rainCompute);
            SetReference(data, "_rainTexture", ImportTexture(source, importer, "fx/fx_rain", records));
            SetReference(data, "_splashTexture", ImportTexture(source, importer, "fx/fx_rainsplash1", records));
            if (includeSurfaces)
            {
                SetReference(data, "_wetnessCompute", wetnessCompute);
                SetReference(data, "_puddleNoise", ImportTexture(source, importer, "fx/puddles_perlin", records));
                SetReference(data, "_rippleNormal", ImportTexture(source, importer, "fx/water_normal", records));
                BindMask(data, MILITARY_TERRAIN, ImportTexture(source, importer, "terrain/terrain_military_1_puddles_mask", records));
                RequireProperty(data, "_wetnessEnabled").boolValue = true;
                RequireProperty(data, "_puddlesEnabled").boolValue = true;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            if (!settings.TryValidate(out string diagnostic)) throw new InvalidDataException(diagnostic);
            if (!AssetDatabase.Contains(settings))
            {
                PrepareFolder(Path.GetDirectoryName(CONFIG_ASSET).Replace('\\', '/'));
                AssetDatabase.CreateAsset(settings, CONFIG_ASSET);
            }
            AssetDatabase.SaveAssetIfDirty(settings);
            rootReference.objectReferenceValue = settings;
            rootData.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(root);
            string manifestPath = Path.Combine(output, "SourceResourceManifest.json");
            var manifest = new ResourceManifest { profile = plan.ProfileName, enabledModCount = plan.ActiveModCount, modOrderFingerprint = plan.ModOrderFingerprint, includeSurfaces = includeSurfaces, rootAsset = ROOT_ASSET, rootGuid = AssetDatabase.AssetPathToGUID(ROOT_ASSET), configAsset = CONFIG_ASSET, configGuid = AssetDatabase.AssetPathToGUID(CONFIG_ASSET), configSha256 = Hash(File.ReadAllBytes(CONFIG_ASSET)), resources = records.ToArray(), escapePuddleMask = "No effective terrain_escape_puddles_mask.dds exists; procedural slope/perlin fallback is an HDRP adaptation. The terrain detail mask is not a puddle mask." };
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            return manifestPath;
        }

        private static T RequireClean<T>(string path, bool create) where T : ScriptableObject
        {
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing == null && create) return ScriptableObject.CreateInstance<T>();
            if (!(existing is T typed)) throw new InvalidDataException("Expected " + typeof(T).Name + " at " + path);
            if (EditorUtility.IsDirty(typed)) throw new InvalidOperationException("Preserve unsaved authored configuration at " + path);
            return typed;
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidDataException("Required precipitation resource is missing: " + path);
            return asset;
        }

        private static Texture2D ImportTexture(XRayEffectiveGamedata source, XRayWeatherResourceImporter importer, string textureName, List<ResourceRecord> records)
        {
            string virtualPath = "textures/" + textureName + ".dds";
            XRayEffectiveFile winner = source.Resolve(virtualPath);
            if (winner == null) throw new InvalidDataException("Missing effective precipitation texture: " + virtualPath);
            byte[] bytes = source.ReadAllBytes(virtualPath);
            Texture2D texture = importer.Bind(textureName) as Texture2D;
            if (texture == null || GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat)) throw new InvalidDataException("Expected an authored non-sRGB DDS texture: " + virtualPath);
            string path = AssetDatabase.GetAssetPath(texture);
            records.Add(new ResourceRecord { virtualPath = virtualPath, winner = winner.SourceName, location = winner.Location, sourceSha256 = Hash(bytes), sourceBytes = bytes.LongLength, assetPath = path, assetGuid = AssetDatabase.AssetPathToGUID(path), assetSha256 = Hash(File.ReadAllBytes(path)), width = texture.width, height = texture.height, mipCount = texture.mipmapCount, graphicsFormat = texture.graphicsFormat.ToString(), conversion = "Existing XRayWeatherResourceImporter and XRayTextureImporter.ImportWeather; UNorm authored DDS mips with a vertical flip." });
            return texture;
        }

        private static void SetReference(SerializedObject data, string name, UnityEngine.Object value)
        {
            SerializedProperty property = RequireProperty(data, name);
            if (property.objectReferenceValue == null) property.objectReferenceValue = value;
        }

        private static SerializedProperty RequireProperty(SerializedObject data, string name)
        {
            SerializedProperty property = data.FindProperty(name);
            if (property == null) throw new MissingFieldException(data.targetObject.GetType().FullName, name);
            return property;
        }

        private static void BindMask(SerializedObject data, string name, Texture2D texture)
        {
            SerializedProperty masks = RequireProperty(data, "_puddleMasks");
            for (int i = 0; i < masks.arraySize; i++)
            {
                SerializedProperty binding = masks.GetArrayElementAtIndex(i);
                if (!string.Equals(binding.FindPropertyRelative("_baseTextureName").stringValue, name, StringComparison.OrdinalIgnoreCase)) continue;
                SerializedProperty mask = binding.FindPropertyRelative("_mask");
                if (mask.objectReferenceValue == null) mask.objectReferenceValue = texture;
                return;
            }
            int index = masks.arraySize;
            masks.arraySize++;
            SerializedProperty added = masks.GetArrayElementAtIndex(index);
            added.FindPropertyRelative("_baseTextureName").stringValue = name;
            added.FindPropertyRelative("_mask").objectReferenceValue = texture;
        }

        private static void PrepareFolder(string folder)
        {
            string parent = "Assets";
            foreach (string part in folder.Substring(7).Split('/'))
            {
                string path = parent + "/" + part;
                if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, part);
                parent = path;
            }
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        [Serializable]
        private sealed class ResourceManifest
        {
            public string profile;
            public int enabledModCount;
            public string modOrderFingerprint;
            public bool includeSurfaces;
            public string rootAsset;
            public string rootGuid;
            public string configAsset;
            public string configGuid;
            public string configSha256;
            public ResourceRecord[] resources;
            public string escapePuddleMask;
        }

        [Serializable]
        private sealed class ResourceRecord
        {
            public string virtualPath;
            public string winner;
            public string location;
            public string sourceSha256;
            public long sourceBytes;
            public string assetPath;
            public string assetGuid;
            public string assetSha256;
            public int width;
            public int height;
            public int mipCount;
            public string graphicsFormat;
            public string conversion;
        }
    }
}
