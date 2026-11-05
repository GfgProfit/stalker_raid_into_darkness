using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Mo2;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayPrecipitationAssetSetup
    {
        public const string CONFIG_ASSET = "Assets/_Source/Settings/Resources/PrecipitationConfig.asset";
        public const string TEXTURE_FOLDER = "Assets/_Source/Settings/Resources/XRayPrecipitation";
        public const string WET_SURFACE_ARRAY = TEXTURE_FOLDER + "/WetSurfaceTextures.asset";
        public const string HEIGHT_INSTANCING_MATERIAL = TEXTURE_FOLDER + "/PrecipitationHeightInstancing.mat";

        private const string PRODUCTION = "E:\\Projects\\stalker_raid_into_darkness";
        private const string ARCHIVE = "E:\\Projects\\stalker_raid_into_darkness_Archive";
        private const string ROOT_ASSET = "Assets/_Source/Settings/Resources/GameConfig.asset";
        private const string SHADER_FOLDER = "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/";
        private const string ARRAY_KEY = "precipitation/wet-surface-textures-v1";
        private const string ARRAY_ROLE = "PrecipitationSourceTextureArray";
        private const string HEIGHT_MATERIAL_KEY = "precipitation/height-instancing-material-v1";
        private const string HEIGHT_MATERIAL_ROLE = "PrecipitationHeightInstancingMaterial";

        private static readonly string[] WET_SURFACE_SOURCES = { "fx/puddles_perlin", "fx/water_normal", "terrain/terrain_agroprom_puddles_mask", "terrain/terrain_darkcape_puddles_mask", "terrain/terrain_darkvalley_puddles_mask", "terrain/terrain_frodo_gen_puddles_mask", "terrain/terrain_garbage_new_puddles_mask", "terrain/terrain_jupiter_puddles_mask", "terrain/terrain_limansk_puddles_mask", "terrain/terrain_marsh_puddles_mask", "terrain/terrain_military_1_puddles_mask", "terrain/terrain_pripyat_puddles_mask", "terrain/terrain_red_forest_puddles_mask", "terrain/terrain_yantar_puddles_mask", "terrain/terrain_zaton_puddles_mask" };

        public static string PrepareRain(string archiveFolder)
        {
            return Prepare(archiveFolder, false);
        }

        public static string PrepareSurfaces(string archiveFolder)
        {
            return Prepare(archiveFolder, true);
        }

        public static string PrepareHeightInstancingMaterial(string archiveFolder)
        {
            if (!string.Equals(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), PRODUCTION, StringComparison.OrdinalIgnoreCase) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Prepare the height shader retention material in the idle Production Editor in Edit Mode.");
            string archivePath = Path.GetFullPath(archiveFolder ?? string.Empty);
            if (!archivePath.StartsWith(ARCHIVE + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Height shader retention evidence must stay under the project Archive.", nameof(archiveFolder));
            GameConfig root = RequireClean<GameConfig>(ROOT_ASSET, false);
            PrecipitationConfig settings = RequireClean<PrecipitationConfig>(CONFIG_ASSET, false);
            if (root.Precipitation != settings || settings.HeightShader == null) throw new InvalidOperationException("The canonical GameConfig precipitation domain must provide the authoritative height Shader reference.");
            string rootBefore = Hash(File.ReadAllBytes(ROOT_ASSET));
            string configBefore = Hash(File.ReadAllBytes(CONFIG_ASSET));
            string shaderPath = AssetDatabase.GetAssetPath(settings.HeightShader);
            if (string.IsNullOrEmpty(shaderPath) || ShaderUtil.ShaderHasError(settings.HeightShader)) throw new InvalidOperationException("The authoritative height Shader must be a valid saved production asset.");
            string output = Path.Combine(archivePath, "HeightInstancingPreparation_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            if (Directory.Exists(output)) throw new IOException("Shader retention evidence must use a new Archive directory.");
            Directory.CreateDirectory(output);
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(HEIGHT_INSTANCING_MATERIAL);
            if (existing == null && (File.Exists(HEIGHT_INSTANCING_MATERIAL) || File.Exists(HEIGHT_INSTANCING_MATERIAL + ".meta"))) throw new InvalidOperationException("Preserve an unreadable or orphaned existing height retention asset at " + HEIGHT_INSTANCING_MATERIAL);
            if (existing != null && (!(existing is Material) || EditorUtility.IsDirty(existing))) throw new InvalidOperationException("Preserve authored or unsaved data at " + HEIGHT_INSTANCING_MATERIAL);
            var material = existing as Material;
            bool created = material == null;
            bool updated = false;
            string previousSha256 = File.Exists(HEIGHT_INSTANCING_MATERIAL) ? Hash(File.ReadAllBytes(HEIGHT_INSTANCING_MATERIAL)) : string.Empty;
            if (material != null && (material.shader != settings.HeightShader || !material.enableInstancing))
            {
                if (XRayAssetMarker.ReadKey(HEIGHT_INSTANCING_MATERIAL) != HEIGHT_MATERIAL_KEY) throw new InvalidOperationException("Preserve the existing unowned height retention material at " + HEIGHT_INSTANCING_MATERIAL);
                material.shader = settings.HeightShader;
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                updated = true;
            }
            if (material == null)
            {
                var prepared = new Material(settings.HeightShader) { name = "PrecipitationHeightInstancing", enableInstancing = true };
                try
                {
                    PrepareFolder(TEXTURE_FOLDER);
                    AssetDatabase.CreateAsset(prepared, HEIGHT_INSTANCING_MATERIAL);
                    material = prepared;
                    prepared = null;
                }
                finally
                {
                    if (prepared != null) UnityEngine.Object.DestroyImmediate(prepared);
                }
            }
            if (created || updated)
            {
                AssetDatabase.SaveAssetIfDirty(material);
                XRayAssetMarker.Write(HEIGHT_INSTANCING_MATERIAL, new XRayAssetMarker(HEIGHT_MATERIAL_KEY, HEIGHT_MATERIAL_ROLE, "GameConfig.Precipitation.HeightShader", shaderPath, AssetDatabase.AssetPathToGUID(shaderPath)));
                material = AssetDatabase.LoadAssetAtPath<Material>(HEIGHT_INSTANCING_MATERIAL);
            }
            if (material == null || material.shader != settings.HeightShader || !material.enableInstancing || EditorUtility.IsDirty(material)) throw new InvalidOperationException("The saved retention material does not match the authoritative height Shader and instancing contract.");
            if (Hash(File.ReadAllBytes(ROOT_ASSET)) != rootBefore || Hash(File.ReadAllBytes(CONFIG_ASSET)) != configBefore) throw new InvalidOperationException("Preparing shader retention must not mutate shared configuration assets.");
            var record = new HeightMaterialRecord { assetPath = HEIGHT_INSTANCING_MATERIAL, assetGuid = AssetDatabase.AssetPathToGUID(HEIGHT_INSTANCING_MATERIAL), assetSha256 = Hash(File.ReadAllBytes(HEIGHT_INSTANCING_MATERIAL)), previousSha256 = previousSha256, shaderPath = shaderPath, shaderGuid = AssetDatabase.AssetPathToGUID(shaderPath), instancing = material.enableInstancing, created = created, updated = updated, configSha256 = configBefore, rootSha256 = rootBefore, retentionScope = "Serialized Resources material supplies the per-shader EnableInstancing build input. Runtime shader selection remains GameConfig.Precipitation.HeightShader. Native Player readback must verify final instanced height coverage." };
            string manifest = Path.Combine(output, "Manifest.json");
            File.WriteAllText(manifest, JsonUtility.ToJson(record, true));
            return manifest;
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
            ArrayRecord arrayRecord = null;
            var data = new SerializedObject(settings);
            SetReference(data, "_rainShader", rainShader);
            SetReference(data, "_heightShader", heightShader);
            SetReference(data, "_rainCompute", rainCompute);
            SetReference(data, "_rainTexture", ImportTexture(source, importer, "fx/fx_rain", records));
            SetReference(data, "_splashTexture", ImportTexture(source, importer, "fx/fx_rainsplash1", records));
            if (includeSurfaces)
            {
                SetReference(data, "_wetnessCompute", wetnessCompute);
                SerializedProperty arrayReference = RequireProperty(data, "_wetSurfaceTextures");
                if (arrayReference.objectReferenceValue != null && AssetDatabase.GetAssetPath(arrayReference.objectReferenceValue) != WET_SURFACE_ARRAY) throw new InvalidOperationException("Preserve the authored wet surface texture array reference.");
                Texture2DArray array = PrepareArray(source, out arrayRecord);
                SetReference(data, "_wetSurfaceTextures", array);
                for (int slice = 2; slice < WET_SURFACE_SOURCES.Length; slice++) BindSourceSlice(data, Path.GetFileName(WET_SURFACE_SOURCES[slice]).Replace("_puddles_mask", string.Empty), slice);
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
            var manifest = new ResourceManifest { profile = plan.ProfileName, enabledModCount = plan.ActiveModCount, modOrderFingerprint = plan.ModOrderFingerprint, includeSurfaces = includeSurfaces, rootAsset = ROOT_ASSET, rootGuid = AssetDatabase.AssetPathToGUID(ROOT_ASSET), configAsset = CONFIG_ASSET, configGuid = AssetDatabase.AssetPathToGUID(CONFIG_ASSET), configSha256 = Hash(File.ReadAllBytes(CONFIG_ASSET)), resources = records.ToArray(), wetSurfaceArray = arrayRecord, escapePuddleMask = "No effective terrain_escape_puddles_mask.dds exists; procedural slope/perlin fallback is an HDRP adaptation. The terrain detail mask is not a puddle mask." };
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

        private static void BindSourceSlice(SerializedObject data, string name, int slice)
        {
            SerializedProperty masks = RequireProperty(data, "_puddleMasks");
            for (int i = 0; i < masks.arraySize; i++)
            {
                SerializedProperty binding = masks.GetArrayElementAtIndex(i);
                if (!string.Equals(binding.FindPropertyRelative("_baseTextureName").stringValue, name, StringComparison.OrdinalIgnoreCase)) continue;
                binding.FindPropertyRelative("_slice").intValue = slice;
                return;
            }
            int index = masks.arraySize;
            masks.arraySize++;
            SerializedProperty added = masks.GetArrayElementAtIndex(index);
            added.FindPropertyRelative("_baseTextureName").stringValue = name;
            added.FindPropertyRelative("_mask").objectReferenceValue = null;
            added.FindPropertyRelative("_slice").intValue = slice;
        }

        private static Texture2DArray PrepareArray(XRayEffectiveGamedata source, out ArrayRecord record)
        {
            UnityEngine.Object existingObject = AssetDatabase.LoadMainAssetAtPath(WET_SURFACE_ARRAY);
            if (existingObject != null && (!(existingObject is Texture2DArray) || EditorUtility.IsDirty(existingObject))) throw new InvalidOperationException("Preserve authored or unsaved data at " + WET_SURFACE_ARRAY);
            var existing = existingObject as Texture2DArray;
            var levels = new List<byte[]>[WET_SURFACE_SOURCES.Length];
            var slices = new SliceRecord[WET_SURFACE_SOURCES.Length];
            using var payload = new MemoryStream();
            for (int slice = 0; slice < WET_SURFACE_SOURCES.Length; slice++)
            {
                string virtualPath = "textures/" + WET_SURFACE_SOURCES[slice] + ".dds";
                XRayEffectiveFile winner = source.Resolve(virtualPath);
                if (winner == null) throw new InvalidDataException("Missing effective wet surface resource " + virtualPath);
                byte[] bytes = source.ReadAllBytes(virtualPath);
                DdsHeader header = DdsHeader.Parse(bytes);
                if (header.Format != DdsFormat.BC1 || header.Width != 1024 || header.Height != 1024 || header.MipCount != 11 || header.IsCubemap || header.IsVolume || header.ArraySize != 1) throw new InvalidDataException("Wet surface source requires native 1024x1024 BC1 and all 11 authored mips: " + virtualPath);
                if (!DdsMipChain.TryReadFlipped(bytes, header, out levels[slice], out string reason)) throw new InvalidDataException("Cannot reuse the authored DDS mip pipeline for " + virtualPath + ": " + reason);
                var mipHashes = new string[header.MipCount];
                for (int mip = 0; mip < levels[slice].Count; mip++)
                {
                    byte[] mipBytes = levels[slice][mip];
                    mipHashes[mip] = Hash(mipBytes);
                    payload.Write(mipBytes, 0, mipBytes.Length);
                }
                slices[slice] = new SliceRecord { slice = slice, virtualPath = virtualPath, winner = winner.SourceName, location = winner.Location, sourceSha256 = Hash(bytes), sourceBytes = bytes.LongLength, width = header.Width, height = header.Height, mipCount = header.MipCount, flippedMipSha256 = mipHashes };
            }
            string expectedHash = Hash(payload.ToArray());
            bool update = existing == null;
            if (existing != null)
            {
                string currentHash = ArrayPayloadHash(existing);
                if (currentHash != expectedHash)
                {
                    XRayAssetMarker marker = XRayAssetMarker.ReadEffective(WET_SURFACE_ARRAY);
                    if (marker == null || marker.Key != ARRAY_KEY || marker.Role != ARRAY_ROLE || marker.Stamp != currentHash) throw new InvalidOperationException("Preserve authored wet surface array data. The existing payload does not match its source ownership marker.");
                    update = true;
                }
            }
            Texture2DArray array = existing;
            if (update)
            {
                var prepared = new Texture2DArray(1024, 1024, WET_SURFACE_SOURCES.Length, GraphicsFormat.RGBA_DXT1_UNorm, TextureCreationFlags.MipChain, 11);
                try
                {
                    if (prepared.mipmapCount != 11) throw new InvalidDataException("The native wet surface array did not allocate all 11 authored mip levels.");
                    prepared.name = "WetSurfaceTextures";
                    prepared.filterMode = FilterMode.Trilinear;
                    prepared.wrapMode = TextureWrapMode.Repeat;
                    prepared.anisoLevel = 1;
                    for (int slice = 0; slice < levels.Length; slice++)
                    {
                        for (int mip = 0; mip < levels[slice].Count; mip++) prepared.SetPixelData(levels[slice][mip], mip, slice);
                    }
                    prepared.Apply(false, false);
                    if (ArrayPayloadHash(prepared) != expectedHash) throw new InvalidDataException("Native array CPU payload differs from the authored flipped DDS mips.");
                    PrepareFolder(TEXTURE_FOLDER);
                    if (existing == null)
                    {
                        AssetDatabase.CreateAsset(prepared, WET_SURFACE_ARRAY);
                        array = prepared;
                        prepared = null;
                    }
                    else
                    {
                        EditorUtility.CopySerialized(prepared, existing);
                        EditorUtility.SetDirty(existing);
                        array = existing;
                    }
                    AssetDatabase.SaveAssetIfDirty(array);
                    XRayAssetMarker.Write(WET_SURFACE_ARRAY, new XRayAssetMarker(ARRAY_KEY, ARRAY_ROLE, "MO2 effective precipitation resources", string.Join("|", WET_SURFACE_SOURCES), expectedHash));
                    AssetDatabase.ImportAsset(WET_SURFACE_ARRAY, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(WET_SURFACE_ARRAY);
                }
                finally
                {
                    if (prepared != null) UnityEngine.Object.DestroyImmediate(prepared);
                }
            }
            if (array == null || ArrayPayloadHash(array) != expectedHash) throw new InvalidDataException("Reloaded serialized array payload differs from the authored DDS mips.");
            foreach (SliceRecord slice in slices)
            {
                slice.arrayMipSha256 = new string[slice.mipCount];
                for (int mip = 0; mip < slice.mipCount; mip++)
                {
                    slice.arrayMipSha256[mip] = Hash(array.GetPixelData<byte>(mip, slice.slice).ToArray());
                    if (slice.arrayMipSha256[mip] != slice.flippedMipSha256[mip]) throw new InvalidDataException("Serialized array mip mismatch at slice " + slice.slice + ", mip " + mip);
                }
            }
            record = new ArrayRecord { assetPath = WET_SURFACE_ARRAY, assetGuid = AssetDatabase.AssetPathToGUID(WET_SURFACE_ARRAY), assetSha256 = Hash(File.ReadAllBytes(WET_SURFACE_ARRAY)), payloadSha256 = expectedHash, payloadBytes = payload.Length, graphicsFormat = array.graphicsFormat.ToString(), width = array.width, height = array.height, depth = array.depth, mipCount = array.mipmapCount, readableForSerializedByteVerification = array.isReadable, createdOrUpdated = update, conversion = "Existing DdsHeader and DdsMipChain.TryReadFlipped with Texture2DArray.SetPixelData. BC1 authored blocks and mip layout are preserved. No rescale, decode or mip regeneration.", slices = slices };
            return array;
        }

        private static string ArrayPayloadHash(Texture2DArray array)
        {
            if (!array.isReadable || array.width != 1024 || array.height != 1024 || array.depth != WET_SURFACE_SOURCES.Length || array.mipmapCount != 11 || array.graphicsFormat != GraphicsFormat.RGBA_DXT1_UNorm) throw new InvalidDataException("Preserve the existing wet surface array. Its readable native layout differs from the source array contract.");
            using var payload = new MemoryStream();
            for (int slice = 0; slice < array.depth; slice++)
            {
                for (int mip = 0; mip < array.mipmapCount; mip++)
                {
                    byte[] bytes = array.GetPixelData<byte>(mip, slice).ToArray();
                    payload.Write(bytes, 0, bytes.Length);
                }
            }
            return Hash(payload.ToArray());
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
        private sealed class HeightMaterialRecord
        {
            public string assetPath;
            public string assetGuid;
            public string assetSha256;
            public string previousSha256;
            public string shaderPath;
            public string shaderGuid;
            public bool instancing;
            public bool created;
            public bool updated;
            public string configSha256;
            public string rootSha256;
            public string retentionScope;
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
            public ArrayRecord wetSurfaceArray;
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

        [Serializable]
        private sealed class ArrayRecord
        {
            public string assetPath;
            public string assetGuid;
            public string assetSha256;
            public string payloadSha256;
            public long payloadBytes;
            public string graphicsFormat;
            public int width;
            public int height;
            public int depth;
            public int mipCount;
            public bool readableForSerializedByteVerification;
            public bool createdOrUpdated;
            public string conversion;
            public SliceRecord[] slices;
        }

        [Serializable]
        private sealed class SliceRecord
        {
            public int slice;
            public string virtualPath;
            public string winner;
            public string location;
            public string sourceSha256;
            public long sourceBytes;
            public int width;
            public int height;
            public int mipCount;
            public string[] flippedMipSha256;
            public string[] arrayMipSha256;
        }
    }
}
