using System;
using System.Collections.Generic;
using System.IO;
using Inventory;
using RaidIntoDarkness.Player;
using UnityEditor;
using UnityEngine;
using UnityOpenMcpBridge;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    [BridgeToolType]
    public static class AnomalyBalanceSetup
    {
        public const string AUTHORING_PATH = "Assets/_Source/Anomalies/Balance/AnomalyBalance.json";
        public const string BALANCE_PATH = "Assets/_Source/Anomalies/Balance/AnomalyBalance.asset";

        private const string PRODUCTION_PATH = "E:/Projects/stalker_raid_into_darkness";
        private const string ROOT_FOLDER = "Assets/_Source/Anomalies/Balance";
        private const string ARCHETYPE_FOLDER = ROOT_FOLDER + "/Archetypes";
        private const string VARIANT_FOLDER = ROOT_FOLDER + "/Variants";
        private const string DEFINITIONS_FOLDER = "Assets/_Source/Items/Definitions/";
        private const string ANOMALIES_FOLDER = "Assets/_Source/Anomalies/";
        private const string GAME_CONFIG_PATH = "Assets/_Source/Settings/Resources/GameConfig.asset";
        private const string PLAYER_PATH = "Assets/_Source/FirstPerson/FPS_Player.prefab";

        [MenuItem("Tools/Anomalies/Apply Authored Balance")]
        public static void RunFromMenu()
        {
            Debug.Log(Run());
        }

        public static string Run()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(project, PRODUCTION_PATH, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Anomaly balance setup requires Production: " + PRODUCTION_PATH + ". Active project: " + project);
            }

            AuthoringData data = JsonUtility.FromJson<AuthoringData>(File.ReadAllText(Path.Combine(project, AUTHORING_PATH)));
            ValidateInput(data);
            GameConfig gameConfig = AssetDatabase.LoadAssetAtPath<GameConfig>(GAME_CONFIG_PATH);
            if (gameConfig == null)
            {
                throw new InvalidOperationException("Missing authoritative GameConfig: " + GAME_CONFIG_PATH);
            }

            ValidateTargets(data);
            EnsureFolder(ROOT_FOLDER);
            EnsureFolder(ARCHETYPE_FOLDER);
            EnsureFolder(VARIANT_FOLDER);
            var archetypes = new Dictionary<string, AnomalyArchetypeConfig>(StringComparer.Ordinal);
            foreach (FamilyData family in data.families)
            {
                AnomalyArchetypeConfig archetype = LoadOrCreate<AnomalyArchetypeConfig>(ARCHETYPE_FOLDER + "/" + family.id + ".asset");
                var serialized = new SerializedObject(archetype);
                RequireProperty(serialized, "_id").stringValue = family.id;
                RequireProperty(serialized, "_damageType").enumValueIndex = (int)ParseDamageType(family.damageType);
                RequireProperty(serialized, "_baseDamage").floatValue = family.baseDamage;
                RequireProperty(serialized, "_equipmentWearPerHitPercent").floatValue = family.equipmentWearPerHitPercent;
                ApplyAndSave(serialized);
                if (!archetype.TryValidate(out string diagnostic))
                {
                    throw new InvalidOperationException(family.id + ": " + diagnostic);
                }
                archetypes.Add(family.id, archetype);
            }

            var variants = new Dictionary<string, AnomalyVariantConfig>(StringComparer.Ordinal);
            foreach (VariantData entry in data.variants)
            {
                AnomalyVariantConfig variant = LoadOrCreate<AnomalyVariantConfig>(VARIANT_FOLDER + "/" + entry.section + ".asset");
                var serialized = new SerializedObject(variant);
                RequireProperty(serialized, "_sourceSection").stringValue = entry.section;
                RequireProperty(serialized, "_archetype").objectReferenceValue = archetypes[entry.familyId];
                RequireProperty(serialized, "_strength").enumValueIndex = (int)ParseStrength(entry.strength);
                RequireProperty(serialized, "_damageMultiplier").floatValue = entry.multiplier;
                RequireProperty(serialized, "_continuous").boolValue = entry.continuous;
                RequireProperty(serialized, "_hitInterval").floatValue = entry.interval;
                ApplyAndSave(serialized);
                if (!variant.TryValidate(out string diagnostic))
                {
                    throw new InvalidOperationException(entry.section + ": " + diagnostic);
                }
                variants.Add(entry.section, variant);
            }

            AnomalyBalanceConfig balance = LoadOrCreate<AnomalyBalanceConfig>(BALANCE_PATH);
            var balanceSerialized = new SerializedObject(balance);
            RequireProperty(balanceSerialized, "_conditionEfficiencyCurve").animationCurveValue = CreateCurve(data.protectionEfficiencyCurve);
            RequireProperty(balanceSerialized, "_radiationRecoveryPerHour").floatValue = data.radiationRecoveryPerHour;
            RequireProperty(balanceSerialized, "_radiationDamagePerSecond").floatValue = data.radiationDamagePerSecond;
            SerializedProperty helmetTypes = RequireProperty(balanceSerialized, "_helmetDamageTypes");
            helmetTypes.arraySize = data.helmetDamageTypes.Length;
            for (int i = 0; i < data.helmetDamageTypes.Length; i++)
            {
                helmetTypes.GetArrayElementAtIndex(i).enumValueIndex = (int)ParseDamageType(data.helmetDamageTypes[i]);
            }
            SerializedProperty archetypeArray = RequireProperty(balanceSerialized, "_archetypes");
            archetypeArray.arraySize = data.families.Length;
            for (int i = 0; i < data.families.Length; i++)
            {
                archetypeArray.GetArrayElementAtIndex(i).objectReferenceValue = archetypes[data.families[i].id];
            }
            SerializedProperty variantArray = RequireProperty(balanceSerialized, "_variants");
            variantArray.arraySize = data.variants.Length;
            for (int i = 0; i < data.variants.Length; i++)
            {
                variantArray.GetArrayElementAtIndex(i).objectReferenceValue = variants[data.variants[i].section];
            }
            ApplyAndSave(balanceSerialized);
            if (!balance.TryValidate(out string balanceDiagnostic))
            {
                throw new InvalidOperationException(balanceDiagnostic);
            }

            var configSerialized = new SerializedObject(gameConfig);
            RequireProperty(configSerialized, "_anomalyBalance").objectReferenceValue = balance;
            ApplyAndSave(configSerialized);
            foreach (ItemProtectionData item in data.itemProtections)
            {
                ConfigureItem(item);
            }

            int changedPrefabs = 0;
            foreach (PrefabBindingData binding in data.prefabBindings)
            {
                if (binding.isBase && ConfigurePrefab(binding, variants[binding.section]))
                {
                    changedPrefabs++;
                }
            }
            foreach (PrefabBindingData binding in data.prefabBindings)
            {
                if (!binding.isBase && ConfigurePrefab(binding, variants[binding.section]))
                {
                    changedPrefabs++;
                }
            }
            bool changedPlayer = ConfigurePlayer();
            return "Anomaly balance applied: " + archetypes.Count + " archetypes, " + variants.Count + " variants, " + data.itemProtections.Length + " item definitions, " + data.prefabBindings.Length + " prefab bindings (" + changedPrefabs + " changed), player receiver " + (changedPlayer ? "updated" : "unchanged") + ". No scenes were modified.";
        }

        public static string ApplyArchetypeWear()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(project, PRODUCTION_PATH, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Anomaly balance setup requires Production: " + PRODUCTION_PATH + ". Active project: " + project);
            }

            AuthoringData data = JsonUtility.FromJson<AuthoringData>(File.ReadAllText(Path.Combine(project, AUTHORING_PATH)));
            ValidateInput(data);
            var targets = new SerializedObject[data.families.Length];
            for (int i = 0; i < data.families.Length; i++)
            {
                FamilyData family = data.families[i];
                string path = ARCHETYPE_FOLDER + "/" + family.id + ".asset";
                AnomalyArchetypeConfig archetype = AssetDatabase.LoadAssetAtPath<AnomalyArchetypeConfig>(path);
                if (archetype == null || archetype.Id != family.id)
                {
                    throw new InvalidOperationException("Missing existing authored anomaly archetype: " + path);
                }
                if (EditorUtility.IsDirty(archetype))
                {
                    throw new InvalidOperationException("Cannot apply authored wear while an archetype has unsaved changes: " + path);
                }
                targets[i] = new SerializedObject(archetype);
                RequireProperty(targets[i], "_equipmentWearPerHitPercent");
            }
            for (int i = 0; i < targets.Length; i++)
            {
                RequireProperty(targets[i], "_equipmentWearPerHitPercent").floatValue = data.families[i].equipmentWearPerHitPercent;
                ApplyAndSave(targets[i]);
            }
            return "Authored anomaly equipment wear applied to " + targets.Length + " existing archetypes.";
        }

        public static string ApplyEquipmentProtections()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(project, PRODUCTION_PATH, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Anomaly balance setup requires Production: " + PRODUCTION_PATH + ". Active project: " + project);
            }

            AuthoringData data = JsonUtility.FromJson<AuthoringData>(File.ReadAllText(Path.Combine(project, AUTHORING_PATH)));
            ValidateInput(data);
            ValidateTargets(data);
            foreach (ItemProtectionData item in data.itemProtections)
            {
                ConfigureItem(item);
            }
            return "Authored anomaly protections applied to " + data.itemProtections.Length + " item definitions. Prefabs, balance assets and scenes were not modified.";
        }

        private static void ValidateInput(AuthoringData data)
        {
            if (data?.helmetDamageTypes == null)
            {
                throw new InvalidOperationException("Authored anomaly balance requires explicit helmet applicability.");
            }
            foreach (string type in data.helmetDamageTypes)
            {
                if (ParseDamageType(type) == AnomalyDamageType.None)
                {
                    throw new InvalidOperationException("Helmet applicability cannot include None damage.");
                }
            }
            if (data == null || data.families == null || data.families.Length != 24 || data.variants == null || data.variants.Length != 66 || data.itemProtections == null || data.itemProtections.Length != 188 || data.prefabBindings == null || data.prefabBindings.Length == 0 || data.protectionEfficiencyCurve == null || data.protectionEfficiencyCurve.Length < 2)
            {
                throw new InvalidOperationException("Authored anomaly balance requires 24 archetypes, 66 variants, 188 item definitions, prefab bindings and an efficiency curve.");
            }
            if (!float.IsFinite(data.radiationRecoveryPerHour) || data.radiationRecoveryPerHour < 0f || !float.IsFinite(data.radiationDamagePerSecond) || data.radiationDamagePerSecond < 0f)
            {
                throw new InvalidOperationException("Authored radiation rates must be finite and non-negative.");
            }
            var families = new HashSet<string>(StringComparer.Ordinal);
            foreach (FamilyData family in data.families)
            {
                RequireIdentifier(family.id);
                ParseDamageType(family.damageType);
                if (!families.Add(family.id) || !float.IsFinite(family.baseDamage) || family.baseDamage < 0f || !float.IsFinite(family.equipmentWearPerHitPercent) || family.equipmentWearPerHitPercent < 0f || family.equipmentWearPerHitPercent > 100f)
                {
                    throw new InvalidOperationException("Invalid or duplicate authored archetype: " + family.id);
                }
            }
            var variants = new Dictionary<string, VariantData>(StringComparer.Ordinal);
            foreach (VariantData variant in data.variants)
            {
                RequireIdentifier(variant.section);
                AnomalyStrength strength = ParseStrength(variant.strength);
                if (!variants.TryAdd(variant.section, variant) || !families.Contains(variant.familyId) || !float.IsFinite(variant.multiplier) || variant.multiplier <= 0f || !float.IsFinite(variant.interval) || variant.interval <= 0f || strength == AnomalyStrength.Weak && variant.multiplier >= 1f || strength == AnomalyStrength.Average && variant.multiplier != 1f || strength == AnomalyStrength.Strong && variant.multiplier <= 1f)
                {
                    throw new InvalidOperationException("Invalid authored variant or strength multiplier: " + variant.section);
                }
            }
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (PrefabBindingData binding in data.prefabBindings)
            {
                RequireAssetPath(binding.assetPath, ANOMALIES_FOLDER, ".prefab");
                if (!paths.Add(binding.assetPath) || !variants.TryGetValue(binding.section, out VariantData variant))
                {
                    throw new InvalidOperationException("Invalid or duplicate prefab binding: " + binding.assetPath);
                }
                string filename = Path.GetFileNameWithoutExtension(binding.assetPath);
                foreach (string strength in Enum.GetNames(typeof(AnomalyStrength)))
                {
                    if (filename.EndsWith(strength, StringComparison.Ordinal) && variant.strength != strength)
                    {
                        throw new InvalidOperationException("Prefab strength does not match its default variant: " + binding.assetPath);
                    }
                }
            }
            foreach (VariantData variant in data.variants)
            {
                RequireAssetPath(variant.prefabPath, ANOMALIES_FOLDER, ".prefab");
                if (!paths.Contains(variant.prefabPath))
                {
                    throw new InvalidOperationException("Catalog prefab has no authored default binding: " + variant.prefabPath);
                }
            }
            paths.Clear();
            foreach (ItemProtectionData item in data.itemProtections)
            {
                string prefix = InventoryDemoContentBuilder.IsClothesResourcePath(item.assetPath) ? InventoryDemoContentBuilder.CLOTHES_FOLDER + "/" : DEFINITIONS_FOLDER;
                RequireAssetPath(item.assetPath, prefix, ".asset");
                if (!paths.Add(item.assetPath) || item.entries == null || item.entries.Length != 8)
                {
                    throw new InvalidOperationException("Each item requires exactly eight authored anomaly protections: " + item.assetPath);
                }
                var types = new HashSet<AnomalyDamageType>();
                foreach (ProtectionData entry in item.entries)
                {
                    AnomalyDamageType type = ParseDamageType(entry.type);
                    if (type == AnomalyDamageType.None || !types.Add(type) || !IsWholePercent(entry.weak) || !IsWholePercent(entry.average) || !IsWholePercent(entry.strong) || entry.weak < entry.average || entry.average < entry.strong)
                    {
                        throw new InvalidOperationException("Item protection must contain unique types and descending whole percentages: " + item.assetPath);
                    }
                }
            }
        }

        private static void ValidateTargets(AuthoringData data)
        {
            foreach (ItemProtectionData item in data.itemProtections)
            {
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(item.assetPath);
                if (definition == null || definition.GetComponent<EquipmentStatsComponent>() == null || FindStats(new SerializedObject(definition)) == null)
                {
                    throw new InvalidOperationException("Missing EquipmentStatsComponent on authored item: " + item.assetPath);
                }
            }
            foreach (PrefabBindingData binding in data.prefabBindings)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(binding.assetPath);
                if (prefab == null || prefab.GetComponent<AnomalyPlayerReaction>() == null || binding.isBase && PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant)
                {
                    throw new InvalidOperationException("Anomaly prefab requires an existing reaction and a regular base before its variants: " + binding.assetPath);
                }
            }
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PATH);
            if (player == null || player.GetComponent<StalkerActorCondition>() == null)
            {
                throw new InvalidOperationException("Player prefab requires its existing actor condition: " + PLAYER_PATH);
            }
        }

        private static void ConfigureItem(ItemProtectionData item)
        {
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(item.assetPath);
            var serialized = new SerializedObject(definition);
            SerializedProperty stats = FindStats(serialized);
            SerializedProperty entries = stats.FindPropertyRelative("_anomalyProtections");
            if (entries == null)
            {
                throw new InvalidOperationException("Equipment stats do not expose authored anomaly protections: " + item.assetPath);
            }
            entries.arraySize = item.entries.Length;
            for (int i = 0; i < item.entries.Length; i++)
            {
                ProtectionData entry = item.entries[i];
                SerializedProperty target = entries.GetArrayElementAtIndex(i);
                target.FindPropertyRelative("_type").enumValueIndex = (int)ParseDamageType(entry.type);
                SerializedProperty values = target.FindPropertyRelative("_values");
                values.FindPropertyRelative("_weak").floatValue = entry.weak;
                values.FindPropertyRelative("_average").floatValue = entry.average;
                values.FindPropertyRelative("_strong").floatValue = entry.strong;
            }
            ApplyAndSave(serialized);
        }

        private static SerializedProperty FindStats(SerializedObject serialized)
        {
            SerializedProperty components = serialized.FindProperty("_components");
            if (components == null)
            {
                return null;
            }
            for (int i = 0; i < components.arraySize; i++)
            {
                SerializedProperty component = components.GetArrayElementAtIndex(i);
                if (component.managedReferenceValue is EquipmentStatsComponent)
                {
                    return component;
                }
            }
            return null;
        }

        private static bool ConfigurePrefab(PrefabBindingData binding, AnomalyVariantConfig variant)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(binding.assetPath);
            try
            {
                Dictionary<Component, string> preserved = CaptureComponents(root);
                AnomalyDamageSource source = root.GetComponent<AnomalyDamageSource>();
                bool changed = source == null;
                if (source == null)
                {
                    if (!binding.isBase)
                    {
                        throw new InvalidOperationException("Variant did not inherit the damage source from its prepared base: " + binding.assetPath);
                    }
                    source = root.AddComponent<AnomalyDamageSource>();
                }
                if (root.GetComponents<AnomalyDamageSource>().Length != 1)
                {
                    throw new InvalidOperationException("Anomaly root must contain exactly one damage source: " + binding.assetPath);
                }
                string previous = EditorJsonUtility.ToJson(source);
                source.ConfigureDamage(variant);
                changed |= previous != EditorJsonUtility.ToJson(source);
                RequirePreservedComponents(preserved, binding.assetPath);
                if (changed)
                {
                    EditorUtility.SetDirty(source);
                    if (PrefabUtility.IsPartOfPrefabInstance(source))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(source);
                    }
                    if (PrefabUtility.SaveAsPrefabAsset(root, binding.assetPath) == null)
                    {
                        throw new InvalidOperationException("Failed to save anomaly prefab: " + binding.assetPath);
                    }
                }
                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool ConfigurePlayer()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PLAYER_PATH);
            try
            {
                Dictionary<Component, string> preserved = CaptureComponents(root);
                AnomalyDamageReceiver receiver = root.GetComponent<AnomalyDamageReceiver>();
                bool changed = receiver == null;
                if (receiver == null)
                {
                    receiver = root.AddComponent<AnomalyDamageReceiver>();
                }
                var serialized = new SerializedObject(receiver);
                RequireProperty(serialized, "_condition").objectReferenceValue = root.GetComponent<StalkerActorCondition>();
                changed |= serialized.ApplyModifiedPropertiesWithoutUndo();
                RequirePreservedComponents(preserved, PLAYER_PATH);
                if (changed)
                {
                    EditorUtility.SetDirty(receiver);
                    if (PrefabUtility.IsPartOfPrefabInstance(receiver))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(receiver);
                    }
                    if (PrefabUtility.SaveAsPrefabAsset(root, PLAYER_PATH) == null)
                    {
                        throw new InvalidOperationException("Failed to save player prefab: " + PLAYER_PATH);
                    }
                }
                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Dictionary<Component, string> CaptureComponents(GameObject root)
        {
            var result = new Dictionary<Component, string>();
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component != null && component is not AnomalyDamageSource && component is not AnomalyDamageReceiver)
                {
                    result.Add(component, EditorJsonUtility.ToJson(component));
                }
            }
            return result;
        }

        private static void RequirePreservedComponents(Dictionary<Component, string> preserved, string path)
        {
            foreach (KeyValuePair<Component, string> item in preserved)
            {
                if (item.Key == null || item.Value != EditorJsonUtility.ToJson(item.Key))
                {
                    throw new InvalidOperationException("Setup changed a pre-existing component outside anomaly damage authoring: " + path);
                }
            }
        }

        private static AnimationCurve CreateCurve(CurvePointData[] points)
        {
            var keys = new Keyframe[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                keys[i] = new Keyframe(points[i].conditionPercent, points[i].efficiencyPercent);
            }
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            return curve;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path))
                {
                    throw new InvalidOperationException("An existing asset has an incompatible type: " + path);
                }
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static void ApplyAndSave(SerializedObject serialized)
        {
            if (serialized.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(serialized.targetObject);
                AssetDatabase.SaveAssetIfDirty(serialized.targetObject);
            }
        }

        private static SerializedProperty RequireProperty(SerializedObject serialized, string name)
        {
            return serialized.FindProperty(name) ?? throw new InvalidOperationException(serialized.targetObject.name + " is missing serialized field " + name);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void RequireIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Authored identifiers must not be empty.");
            }
            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    throw new InvalidOperationException("Authored identifiers may contain only letters, digits and underscores: " + value);
                }
            }
        }

        private static void RequireAssetPath(string path, string prefix, string extension)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(extension, StringComparison.Ordinal) || path.Contains("..") || path.Contains('\\'))
            {
                throw new InvalidOperationException("Authored asset path is outside its permitted scope: " + path);
            }
        }

        private static bool IsWholePercent(float value)
        {
            return float.IsFinite(value) && value >= 0f && value <= 100f && value == Mathf.Round(value);
        }

        private static AnomalyDamageType ParseDamageType(string value)
        {
            return Enum.TryParse(value, false, out AnomalyDamageType result) && Enum.IsDefined(typeof(AnomalyDamageType), result) ? result : throw new InvalidOperationException("Unknown anomaly damage type: " + value);
        }

        private static AnomalyStrength ParseStrength(string value)
        {
            return Enum.TryParse(value, false, out AnomalyStrength result) && Enum.IsDefined(typeof(AnomalyStrength), result) ? result : throw new InvalidOperationException("Unknown anomaly strength: " + value);
        }

        [Serializable]
        private sealed class AuthoringData
        {
            public string[] helmetDamageTypes;
            public FamilyData[] families;
            public VariantData[] variants;
            public float radiationRecoveryPerHour;
            public float radiationDamagePerSecond;
            public CurvePointData[] protectionEfficiencyCurve;
            public ItemProtectionData[] itemProtections;
            public PrefabBindingData[] prefabBindings;
        }

        [Serializable]
        private sealed class FamilyData
        {
            public string id;
            public string damageType;
            public float baseDamage;
            public float equipmentWearPerHitPercent;
        }

        [Serializable]
        private sealed class VariantData
        {
            public string section;
            public string familyId;
            public string prefabPath;
            public string strength;
            public float multiplier;
            public bool continuous;
            public float interval;
        }

        [Serializable]
        private sealed class CurvePointData
        {
            public float conditionPercent;
            public float efficiencyPercent;
        }

        [Serializable]
        private sealed class ItemProtectionData
        {
            public string assetPath;
            public ProtectionData[] entries;
        }

        [Serializable]
        private sealed class ProtectionData
        {
            public string type;
            public float weak;
            public float average;
            public float strong;
        }

        [Serializable]
        private sealed class PrefabBindingData
        {
            public string assetPath;
            public string section;
            public bool isBase;
        }
    }
}
