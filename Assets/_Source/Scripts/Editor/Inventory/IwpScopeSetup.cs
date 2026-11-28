using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.IconGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Inventory.Editor
{
    public static class IwpScopeSetup
    {
        public const string WEAPON_PATH = IwpWeaponSetup.DEFINITION_PATH;
        public const string WEAPON_PREFAB_PATH = IwpWeaponSetup.PREFAB_PATH;

        private const string ARCHIVE_ROOT = "E:/Projects/stalker_raid_into_darkness_Archive";

        public static string LastResult { get; private set; }

        [MenuItem("Tools/Inventory/Setup AUG A3 Scopes")]
        public static void SetupFromMenu()
        {
            Debug.Log(Setup(Path.Combine(ARCHIVE_ROOT, "AugScopeAttachments", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"))));
            IwpSilencerSetup.BakeIcons();
        }

        public static string Inspect()
        {
            GameObject root = WorldItemPrefabBuilder.GetWeaponVisual(AssetDatabase.LoadAssetAtPath<GameObject>(WEAPON_PREFAB_PATH));
            Transform bone = FindBone(root, WeaponHudComponent.SCOPE_BONE);
            var result = new StringBuilder();
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            result.AppendLine($"Project={Application.dataPath}; stage={stage?.assetPath}; dirty={stage != null && stage.scene.isDirty}");

            foreach (Transform child in bone)
            {
                ItemDefinition definition = ResolveDefinition(child.gameObject);
                result.AppendLine($"{child.name}: {definition?.name}; active={child.gameObject.activeSelf}");
            }

            return result.ToString();
        }

        public static string Setup(string reportDirectory)
        {
            string report = Path.GetFullPath(reportDirectory);

            if (report.StartsWith(Path.GetFullPath(ARCHIVE_ROOT) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ArgumentException("Reports must remain inside Archive.", nameof(reportDirectory));
            }

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (stage != null && stage.assetPath == WEAPON_PREFAB_PATH)
            {
                throw new InvalidOperationException("Close the AUG Prefab Stage before running scope setup. Unsaved changes are preserved.");
            }

            Directory.CreateDirectory(report);
            ItemDefinition weapon = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WEAPON_PATH);
            Backup(WEAPON_PATH, report);
            Backup(WEAPON_PREFAB_PATH, report);
            GameObject root = PrefabUtility.LoadPrefabContents(WEAPON_PREFAB_PATH);
            var definitions = new List<ItemDefinition>();
            var log = new StringBuilder();

            try
            {
                GameObject visual = WorldItemPrefabBuilder.GetWeaponVisual(root);
                Transform scopeBone = FindBone(visual, WeaponHudComponent.SCOPE_BONE);
                var seen = new HashSet<ItemDefinition>();

                foreach (Transform child in scopeBone)
                {
                    ItemDefinition scope = ResolveDefinition(child.gameObject);

                    if (scope == null)
                    {
                        continue;
                    }

                    if (seen.Add(scope) == false)
                    {
                        throw new InvalidOperationException("Duplicate scope definition under wpn_scope: " + scope.name);
                    }

                    definitions.Add(scope);
                    Backup(AssetDatabase.GetAssetPath(scope), report);
                    ConfigureAim(scope);
                    StripMountedItem(child.gameObject);
                    ConfigureVariant(visual, scope, child.gameObject);
                    child.gameObject.SetActive(false);
                    log.AppendLine(scope.name);
                }

                if (definitions.Count == 0)
                {
                    throw new InvalidOperationException("No scope prefabs found under AUG wpn_scope.");
                }

                ConfigureDefaults(visual);
                ConfigureCompatibility(weapon, definitions);
                PrefabUtility.SaveAsPrefabAsset(root, WEAPON_PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            LastResult = $"Configured {definitions.Count} AUG scopes, shared visual variants and zero aim offsets.\n{log}";
            File.WriteAllText(Path.Combine(report, "setup.txt"), LastResult);
            return LastResult;
        }

        private static void ConfigureCompatibility(ItemDefinition weapon, List<ItemDefinition> definitions)
        {
            var serialized = new SerializedObject(weapon);
            SerializedProperty components = serialized.FindProperty("_components");

            for (int i = 0; i < components.arraySize; i++)
            {
                SerializedProperty component = components.GetArrayElementAtIndex(i);

                if (component.managedReferenceValue is not WeaponStatsComponent)
                {
                    continue;
                }

                SerializedProperty scopes = component.FindPropertyRelative("_compatibleScopes");
                scopes.arraySize = definitions.Count;

                for (int j = 0; j < definitions.Count; j++)
                {
                    scopes.GetArrayElementAtIndex(j).objectReferenceValue = definitions[j];
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureAim(ItemDefinition scope)
        {
            var serialized = new SerializedObject(scope);
            SerializedProperty components = serialized.FindProperty("_components");

            for (int i = 0; i < components.arraySize; i++)
            {
                SerializedProperty component = components.GetArrayElementAtIndex(i);

                if (component.managedReferenceValue is WeaponAddonComponent)
                {
                    component.FindPropertyRelative("_overrideAimOffsetPosition").boolValue = true;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureVariant(GameObject root, ItemDefinition scope, GameObject visual)
        {
            ItemIconVariantPart binding = null;

            foreach (ItemIconVariantPart part in root.GetComponents<ItemIconVariantPart>())
            {
                if (part.Group == WeaponScopeVisuals.SCOPE_GROUP && part.Part == scope)
                {
                    Object.DestroyImmediate(part);
                }
            }

            foreach (ItemIconVariantPart part in visual.GetComponents<ItemIconVariantPart>())
            {
                if (part.Part == scope)
                {
                    binding = part;
                    break;
                }
            }

            if (binding == null)
            {
                binding = visual.AddComponent<ItemIconVariantPart>();
            }

            binding.SetData(scope, WeaponScopeVisuals.SCOPE_GROUP, new[] { visual }, Array.Empty<GameObject>());
        }

        private static void ConfigureDefaults(GameObject root)
        {
            WeaponScopeVisuals visuals = root.GetComponent<WeaponScopeVisuals>();

            if (visuals == null)
            {
                visuals = root.AddComponent<WeaponScopeVisuals>();
            }

            var serialized = new SerializedObject(visuals);
            serialized.FindProperty("_defaultScope").objectReferenceValue = FindBone(root, "def_sight").gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ItemIconVisualConfigurators.Apply(root, null, Array.Empty<string>());
        }

        private static void StripMountedItem(GameObject root)
        {
            foreach (WorldItemImpactSound sound in root.GetComponentsInChildren<WorldItemImpactSound>(true))
            {
                Object.DestroyImmediate(sound);
            }

            foreach (WorldItem item in root.GetComponentsInChildren<WorldItem>(true))
            {
                Object.DestroyImmediate(item);
            }

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(body);
            }
        }

        private static ItemDefinition ResolveDefinition(GameObject root)
        {
            ItemDefinition definition = root.GetComponent<WorldItem>()?.Definition ?? root.GetComponent<ItemIconVariantPart>()?.Part;
            definition = definition != null ? definition : AssetDatabase.LoadAssetAtPath<ItemDefinition>(GraWeaponAddonSetup.ScopeDefinitionPath(root.name));
            return definition?.GetComponent<WeaponAddonComponent>()?.Kind == WeaponAddonKind.Scope ? definition : null;
        }

        private static Transform FindBone(GameObject root, string name)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name)
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Missing " + name + " in " + root.name);
        }

        private static void Backup(string assetPath, string directory)
        {
            string target = Path.Combine(directory, "backups", assetPath);

            if (File.Exists(target))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(assetPath, target);
            File.Copy(assetPath + ".meta", target + ".meta");
        }
    }
}
