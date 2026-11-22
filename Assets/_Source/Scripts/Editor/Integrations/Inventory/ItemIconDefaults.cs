using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Inventory;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class ItemIconDefaults
    {
        public const float LIGHT_INTENSITY = ItemIconViewSettings.DEFAULT_LIGHT_INTENSITY;
        public const float SLOT_LIGHT_INTENSITY = ItemIconViewSettings.DEFAULT_LIGHT_INTENSITY;
        public const bool USE_OUTLINE = false;

        private const string DEFINITIONS_FOLDER = "Assets/_Source/Items/Definitions";
        private const string ARCHIVE_ROOT = "E:/Projects/stalker_raid_into_darkness_Archive";
        private const string PRODUCTION_ROOT = "E:/Projects/stalker_raid_into_darkness";
        private const string LIGHT_INTENSITY_PATTERN = "(\"_(?:defaultView|slotView)\"\\s*:\\s*\\{.*?\"_lightIntensity\"\\s*:\\s*)-?[0-9]+(?:\\.[0-9]+)?(?:[Ee][+-]?[0-9]+)?";

        public static readonly Vector3 SIDE_VIEW_ANGLES = new Vector3(0f, -90f, 0f);

        public static void Apply(SerializedObject definition, Vector3 cameraAngles)
        {
            definition.FindProperty("_iconOverride").objectReferenceValue = null;
            definition.FindProperty("_icon._generateIcon").boolValue = true;
            definition.FindProperty("_icon._useOutline").boolValue = USE_OUTLINE;
            definition.FindProperty("_icon._useCustomSlotView").boolValue = false;
            definition.FindProperty("_icon._defaultView._cameraEulerAngles").vector3Value = cameraAngles;
            definition.FindProperty("_icon._defaultView._lightIntensity").floatValue = LIGHT_INTENSITY;
            definition.FindProperty("_icon._slotView._cameraEulerAngles").vector3Value = cameraAngles;
            definition.FindProperty("_icon._slotView._lightIntensity").floatValue = SLOT_LIGHT_INTENSITY;
        }

        public static string MigrateLightIntensity(string archive)
        {
            archive = NormalizePath(archive);
            if (!archive.StartsWith(NormalizePath(ARCHIVE_ROOT) + "/", StringComparison.OrdinalIgnoreCase) || !string.Equals(NormalizePath(Application.dataPath), NormalizePath(PRODUCTION_ROOT + "/Assets"), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Item icon migration requires Production and a task directory under the canonical Archive.");
            }

            AssertNoReparsePoints(archive);
            Directory.CreateDirectory(archive);
            string backupRoot = archive + "/ItemDefinitionsBefore";
            string run = archive + "/ItemIconIntensity_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Directory.CreateDirectory(run + "/Before");
            Directory.CreateDirectory(run + "/After");
            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", InventoryDemoContentBuilder.ITEM_DEFINITION_FOLDERS);
            Array.Sort(guids, StringComparer.Ordinal);
            var definitions = new List<ItemDefinition>();
            var paths = new List<string>();
            var metadata = new List<string>();
            var json = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
                bool itemPath = path.StartsWith(DEFINITIONS_FOLDER + "/", StringComparison.Ordinal) || InventoryDemoContentBuilder.IsItemResourcePath(path);
                if (!itemPath || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Unexpected ItemDefinition path: " + path);
                }

                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (definition == null)
                {
                    throw new InvalidOperationException("Cannot load ItemDefinition: " + path);
                }

                using (var serialized = new SerializedObject(definition))
                {
                    RequireIntensity(serialized, "_defaultView");
                    RequireIntensity(serialized, "_slotView");
                }

                string before = EditorJsonUtility.ToJson(definition);
                WithoutLightIntensities(before);
                string source = NormalizePath(PRODUCTION_ROOT + "/" + path);
                string backup = backupRoot + "/" + path;
                AssertNoReparsePoints(source);
                AssertNoReparsePoints(source + ".meta");
                AssertNoReparsePoints(backup);
                AssertNoReparsePoints(backup + ".meta");
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                BackupExact(source, backup);
                BackupExact(source + ".meta", backup + ".meta");
                File.WriteAllText(run + "/Before/" + guids[i] + ".json", before, new UTF8Encoding(false));
                definitions.Add(definition);
                paths.Add(path);
                metadata.Add(HashFile(source + ".meta"));
                json.Add(before);
            }

            int changed = 0;
            int failed = 0;
            var csv = new StringBuilder("Path,Guid,DefaultBefore,SlotBefore,DefaultAfter,SlotAfter,Changed,Error\n");
            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                float beforeDefault;
                float beforeSlot;
                using (var serialized = new SerializedObject(definition))
                {
                    beforeDefault = RequireIntensity(serialized, "_defaultView").floatValue;
                    beforeSlot = RequireIntensity(serialized, "_slotView").floatValue;
                }

                bool requiresChange = beforeDefault != LIGHT_INTENSITY || beforeSlot != SLOT_LIGHT_INTENSITY;
                string error = string.Empty;
                try
                {
                    if (requiresChange)
                    {
                        Undo.RecordObject(definition, "Set item icon light intensity to 20");
                        using (var serialized = new SerializedObject(definition))
                        {
                            RequireIntensity(serialized, "_defaultView").floatValue = LIGHT_INTENSITY;
                            RequireIntensity(serialized, "_slotView").floatValue = SLOT_LIGHT_INTENSITY;
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                        }

                        if (WithoutLightIntensities(json[i]) != WithoutLightIntensities(EditorJsonUtility.ToJson(definition)))
                        {
                            throw new InvalidOperationException("ItemDefinition changed outside the two icon light intensities.");
                        }

                        EditorUtility.SetDirty(definition);
                        AssetDatabase.SaveAssetIfDirty(definition);
                    }

                    string after = EditorJsonUtility.ToJson(definition);
                    File.WriteAllText(run + "/After/" + guids[i] + ".json", after, new UTF8Encoding(false));
                    if (WithoutLightIntensities(json[i]) != WithoutLightIntensities(after) || AssetDatabase.AssetPathToGUID(paths[i]) != guids[i] || HashFile(PRODUCTION_ROOT + "/" + paths[i] + ".meta") != metadata[i])
                    {
                        throw new InvalidOperationException("ItemDefinition data, GUID or metadata preservation failed.");
                    }

                    using (var serialized = new SerializedObject(definition))
                    {
                        if (RequireIntensity(serialized, "_defaultView").floatValue != LIGHT_INTENSITY || RequireIntensity(serialized, "_slotView").floatValue != SLOT_LIGHT_INTENSITY)
                        {
                            throw new InvalidOperationException("Item icon light intensities were not saved as 20.");
                        }
                    }

                    if (requiresChange)
                    {
                        changed++;
                    }
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    failed++;
                    EditorJsonUtility.FromJsonOverwrite(json[i], definition);
                    EditorUtility.SetDirty(definition);
                    AssetDatabase.SaveAssetIfDirty(definition);
                }

                using (var serialized = new SerializedObject(definition))
                {
                    csv.Append(Csv(paths[i])).Append(',').Append(guids[i]).Append(',').Append(beforeDefault.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(beforeSlot.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(RequireIntensity(serialized, "_defaultView").floatValue.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(RequireIntensity(serialized, "_slotView").floatValue.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(requiresChange && error.Length == 0 ? "true" : "false").Append(',').Append(Csv(error)).Append('\n');
                }
            }

            File.WriteAllText(run + "/BeforeAfter.csv", csv.ToString(), new UTF8Encoding(false));
            string result = "{\"passed\":" + (failed == 0 ? "true" : "false") + ",\"checked\":" + definitions.Count.ToString(CultureInfo.InvariantCulture) + ",\"changed\":" + changed.ToString(CultureInfo.InvariantCulture) + ",\"failed\":" + failed.ToString(CultureInfo.InvariantCulture) + ",\"report\":\"" + run + "/BeforeAfter.csv\"}";
            File.WriteAllText(run + "/Summary.json", result, new UTF8Encoding(false));
            return result;
        }

        private static SerializedProperty RequireIntensity(SerializedObject definition, string view)
        {
            SerializedProperty property = definition.FindProperty("_icon." + view + "._lightIntensity");
            if (property == null)
            {
                throw new InvalidOperationException("ItemDefinition is missing the serialized icon light intensity: " + view);
            }

            return property;
        }

        private static string WithoutLightIntensities(string json)
        {
            MatchCollection matches = Regex.Matches(json, LIGHT_INTENSITY_PATTERN, RegexOptions.Singleline);
            if (matches.Count != 2)
            {
                throw new InvalidOperationException("ItemDefinition must contain exactly two serialized icon view light intensities.");
            }

            return Regex.Replace(json, LIGHT_INTENSITY_PATTERN, match => match.Groups[1].Value + "0", RegexOptions.Singleline);
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        }

        private static void AssertNoReparsePoints(string path)
        {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Reparse-point files are not allowed: " + path);
            }

            DirectoryInfo directory = new DirectoryInfo(File.Exists(path) ? Path.GetDirectoryName(path) : path);
            while (directory != null)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("Reparse-point directories are not allowed: " + directory.FullName);
                }

                directory = directory.Parent;
            }
        }

        private static void BackupExact(string source, string destination)
        {
            if (File.Exists(destination))
            {
                if (HashFile(source) != HashFile(destination))
                {
                    throw new InvalidOperationException("An existing ItemDefinition backup has different bytes: " + destination);
                }

                return;
            }

            File.Copy(source, destination);
            if (HashFile(source) != HashFile(destination))
            {
                throw new InvalidOperationException("ItemDefinition backup verification failed: " + destination);
            }
        }

        private static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string Csv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
