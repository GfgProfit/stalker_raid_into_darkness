using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Inventory.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace Inventory.Editor
{
    internal static class ItemIconGeneratedAssetWriter
    {
        public const string DEFAULT_RESOURCES_FOLDER = "Assets/Resources";
        public const string GENERATED_RELATIVE_FOLDER = "Generated/ItemIcons";

        public static string CatalogAssetPath
        {
            get
            {
                string existing = FindResourceAsset<BakedItemIconCatalog>(BakedItemIconCatalog.DEFAULT_RESOURCE_PATH);
                return string.IsNullOrEmpty(existing) ? $"{SettingsResourcesFolder}/{BakedItemIconCatalog.DEFAULT_RESOURCE_PATH}.asset" : existing;
            }
        }

        public static string ResourcesFolder => Path.GetDirectoryName(CatalogAssetPath).Replace('\\', '/');
        public static string GeneratedFolderPath => $"{ResourcesFolder}/{GENERATED_RELATIVE_FOLDER}";

        private static string SettingsResourcesFolder
        {
            get
            {
                string settingsPath = FindResourceAsset<ItemIconGeneratorSettings>(ItemIconGeneratorSettings.DEFAULT_RESOURCE_PATH);
                return string.IsNullOrEmpty(settingsPath) ? DEFAULT_RESOURCES_FOLDER : Path.GetDirectoryName(settingsPath).Replace('\\', '/');
            }
        }

        public static ItemIconGeneratorSettings LoadOrCreateSettings()
        {
            string existing = FindResourceAsset<ItemIconGeneratorSettings>(ItemIconGeneratorSettings.DEFAULT_RESOURCE_PATH);

            if (string.IsNullOrEmpty(existing) == false)
            {
                return AssetDatabase.LoadAssetAtPath<ItemIconGeneratorSettings>(existing);
            }

            EnsureFolder(DEFAULT_RESOURCES_FOLDER);
            var settings = ScriptableObject.CreateInstance<ItemIconGeneratorSettings>();
            AssetDatabase.CreateAsset(settings, $"{DEFAULT_RESOURCES_FOLDER}/{ItemIconGeneratorSettings.DEFAULT_RESOURCE_PATH}.asset");
            AssetDatabase.SaveAssets();
            ItemIconGeneratorSettings.ResetDefaultCache();
            return settings;
        }

        public static BakedItemIconCatalog LoadOrCreateCatalog()
        {
            string path = CatalogAssetPath;
            var catalog = AssetDatabase.LoadAssetAtPath<BakedItemIconCatalog>(path);

            if (catalog != null)
            {
                return catalog;
            }

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            catalog = ScriptableObject.CreateInstance<BakedItemIconCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
            AssetDatabase.SaveAssets();
            BakedItemIconCatalog.ResetDefaultCache();
            return catalog;
        }

        public static string WriteTexture(Texture2D texture, BakedItemIconEntry keyEntry, ItemDefinition definition, ItemIconGeneratorSettings settings)
        {
            string folder = GeneratedFolderPath;
            EnsureFolder(folder);
            string fileName = $"{Sanitize(keyEntry.ItemId)}_{keyEntry.StableKeyHash:x16}.png";
            string assetPath = $"{folder}/{fileName}";
            File.WriteAllBytes(Path.GetFullPath(assetPath), texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = definition.Icon.GetSpritePixelsPerUnit(settings == null ? ItemIconGeneratorSettings.LoadDefault().IconRenderScale : settings.IconRenderScale);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(texture.width, texture.height)), 32, 8192);
                importer.SaveAndReimport();
            }

            return GENERATED_RELATIVE_FOLDER + "/" + Path.GetFileNameWithoutExtension(fileName);
        }

        public static void ClearGeneratedAssets()
        {
            BakedItemIconCatalog catalog = LoadOrCreateCatalog();
            Undo.RecordObject(catalog, "Clear Generated Item Icons");
            catalog.ClearEntries();
            EditorUtility.SetDirty(catalog);
            string folder = GeneratedFolderPath;

            if (AssetDatabase.IsValidFolder(folder))
            {
                var paths = new List<string>();

                foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                    if (string.IsNullOrEmpty(assetPath) == false && assetPath != folder)
                    {
                        paths.Add(assetPath);
                    }
                }

                paths.Sort((left, right) => right.Length.CompareTo(left.Length));

                foreach (string path in paths)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }

            AssetDatabase.SaveAssets();
            BakedItemIconCatalog.ResetDefaultCache();
            ItemIconCache.Clear();
        }

        public static void DeleteGeneratedAssetsExcept(IReadOnlyList<BakedItemIconEntry> retainedEntries)
        {
            string folder = GeneratedFolderPath;

            if (AssetDatabase.IsValidFolder(folder) == false)
            {
                return;
            }

            var retained = new HashSet<string>(StringComparer.Ordinal);

            foreach (BakedItemIconEntry entry in retainedEntries)
            {
                if (entry != null)
                {
                    retained.Add(ResourcePathToAssetPath(entry.SpriteResourcePath));
                }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrEmpty(assetPath) == false && retained.Contains(assetPath) == false)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
            }
        }

        public static string ResourcePathToAssetPath(string resourcePath) => string.IsNullOrEmpty(resourcePath) ? string.Empty : $"{ResourcesFolder}/{resourcePath}.png";

        public static void EnsureFolder(string folderPath)
        {
            string[] segments = folderPath.Replace('\\', '/').Split('/');
            string current = segments[0];

            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";

                if (AssetDatabase.IsValidFolder(next) == false)
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }
        }

        private static string FindResourceAsset<T>(string resourceName) where T : UnityEngine.Object
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (path.Contains("/Resources/") && Path.GetFileNameWithoutExtension(path) == resourceName)
                {
                    return path;
                }
            }

            return null;
        }

        private static string Sanitize(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (char character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) || character == '_' || character == '-' ? character : '_');
            }

            return builder.ToString();
        }
    }

    internal static class ItemIconBakeSignatureBuilder
    {
        public static string Build(ItemIconBakeVariant variant, ItemIconGeneratorSettings settings)
        {
            var source = new StringBuilder(512);
            BakedItemIconEntry keyEntry = BakedItemIconEntry.Create(variant.Definition, variant.VariantIds, variant.Width, variant.Height, variant.ProfileType, settings, string.Empty, string.Empty, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(variant.Definition)), variant.EstimatedTextureBytes);
            source.Append(BuildIdentity(keyEntry));
            AppendDependency(source, variant.Definition);
            AppendDependency(source, variant.Definition.VisualPrefab);
            AppendDependency(source, settings);
            Dictionary<string, ItemDefinition> definitions = ItemIconVariantEnumerator.GetDefinitionsById();
            var parts = new List<string>(variant.VariantIds);

            foreach (ItemDefinition defaultPart in variant.Definition.DefaultIconVariant)
            {
                if (defaultPart != null)
                {
                    parts.Add(defaultPart.DefinitionId);
                }
            }

            parts.Sort(StringComparer.Ordinal);

            foreach (string id in parts)
            {
                if (definitions.TryGetValue(id, out ItemDefinition part))
                {
                    AppendDependency(source, part);
                    AppendDependency(source, part.VisualPrefab);
                }
            }

            return Hash128.Compute(source.ToString()).ToString();
        }

        public static string BuildIdentity(BakedItemIconEntry entry)
        {
            var identity = new StringBuilder(192);
            identity.Append(entry.ItemId).Append('|');

            foreach (string id in entry.VariantIds)
            {
                identity.Append(id).Append(',');
            }

            identity.Append('|').Append(entry.Width).Append('x').Append(entry.Height).Append('|').Append((int)entry.ProfileType).Append('|').Append(entry.VisualSignature);
            return identity.ToString();
        }

        private static void AppendDependency(StringBuilder source, UnityEngine.Object asset)
        {
            if (asset == null)
            {
                source.Append("null;");
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            source.Append(assetPath).Append(':');
            source.Append(string.IsNullOrEmpty(assetPath) ? "unsaved" : AssetDatabase.GetAssetDependencyHash(assetPath).ToString()).Append(';');
        }
    }
}
