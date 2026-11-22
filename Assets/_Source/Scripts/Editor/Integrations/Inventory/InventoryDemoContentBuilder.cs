using System.Collections.Generic;
using System.IO;
using Inventory;
using Inventory.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class InventoryDemoContentBuilder
    {
        public const string ROOT = "Assets/_Source/Settings/Inventory";
        public const string DEFINITIONS_FOLDER = "Assets/_Source/Items/Definitions";
        public const string LEGACY_DEMO_FOLDER = ROOT + "/Demo";
        public const string LAYOUTS_FOLDER = ROOT + "/Layouts";
        public const string RESOURCES_FOLDER = ROOT + "/Resources";
        public const string DATABASE_PATH = ROOT + "/ItemDatabase.asset";
        public const string SEED_PATH = ROOT + "/InventorySeed.asset";
        public const string EQUIPMENT_LAYOUT_PATH = LAYOUTS_FOLDER + "/EquipmentLayout.asset";
        public const string SETTINGS_PATH = RESOURCES_FOLDER + "/" + ItemIconGeneratorSettings.DEFAULT_RESOURCE_PATH + ".asset";
        public const string CATALOG_PATH = RESOURCES_FOLDER + "/" + BakedItemIconCatalog.DEFAULT_RESOURCE_PATH + ".asset";
        public const string BACKPACK = "Backpack";
        public const string STASH = "Stash";
        public const string EQUIPMENT = "Equipment";
        public const string OWNER = "Player";
        public const string CONSUMABLE_CATEGORY = "Consumable";
        public const string WEAPON_CATEGORY = "Weapon";
        public const string RIFLE_TAG = "Rifle";
        public const string PISTOL_TAG = "Pistol";
        public const string KNIFE_TAG = "Knife";
        public const string EQUIPMENT_MASK = "HHTTDD\nHHOORR\nKKOOBB\nKKOONN\nPPPPP.\nPPPPP.\nSSSSS.\nSSSSS.\n1234bg\navwxy.";

        internal const string CLOTHES_FOLDER = "Assets/_Source/Resources/Items/Clothes";

        internal static readonly string[] ITEM_DEFINITION_FOLDERS = { DEFINITIONS_FOLDER, GraWeaponAddonSetup.ADDONS_FOLDER, IwpAmmoSetup.AMMO_FOLDER, CLOTHES_FOLDER };

        internal static bool IsItemResourcePath(string path) => GraWeaponAddonSetup.IsAddonResourcePath(path) || IwpAmmoSetup.IsAmmoResourcePath(path) || IsClothesResourcePath(path);

        internal static bool IsClothesResourcePath(string path) => path?.StartsWith(CLOTHES_FOLDER + "/", System.StringComparison.Ordinal) == true;

        internal static string GetItemResourceName(ItemDefinition definition, string path)
        {
            EquipmentStatsComponent stats = IsClothesResourcePath(path) ? definition.GetComponent<EquipmentStatsComponent>() : null;
            return stats != null && stats.Section.Length > 0 ? stats.Section : definition.name;
        }

        public sealed class DemoContent
        {
            public readonly Dictionary<string, ItemDefinition> Items = new();
            public ItemDatabase Database;
            public InventorySeed Seed;
            public InventorySlotLayout EquipmentLayout;
            public ItemIconGeneratorSettings Settings;
            public BakedItemIconCatalog Catalog;
        }

        public static DemoContent Build()
        {
            EnsureFolder(LAYOUTS_FOLDER);
            EnsureFolder(RESOURCES_FOLDER);

            if (AssetDatabase.IsValidFolder(LEGACY_DEMO_FOLDER))
            {
                AssetDatabase.DeleteAsset(LEGACY_DEMO_FOLDER);
            }

            var content = new DemoContent();

            string[] definitionGuids = AssetDatabase.FindAssets("t:" + nameof(ItemDefinition), ITEM_DEFINITION_FOLDERS);

            foreach (string guid in definitionGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

                if (definition == null)
                {
                    continue;
                }

                string key = IsItemResourcePath(path) ? GetItemResourceName(definition, path) : Path.GetFileNameWithoutExtension(path);
                content.Items[key] = definition;

                if (definition.IsCategory(CONSUMABLE_CATEGORY) && definition.GetComponent<ConsumableItemComponent>() == null)
                {
                    SetComponents(definition, new ConsumableItemComponent());
                }

                if (definition.IsCategory(WEAPON_CATEGORY))
                {
                    SetPreferredSlots(definition, PreferredWeaponSlots(definition));
                }

                if (definition.IsCategory(WEAPON_CATEGORY) && definition.Icon.UseCustomSlotView)
                {
                    var serialized = new SerializedObject(definition);
                    serialized.FindProperty("_icon._useCustomSlotView").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(definition);
                }
            }

            content.Database = CreateOrLoad<ItemDatabase>(DATABASE_PATH);
            content.Database.SetDefinitions(content.Items.Values);
            EditorUtility.SetDirty(content.Database);
            content.EquipmentLayout = BuildEquipmentLayout();
            content.Settings = BuildGeneratorSettings();
            content.Catalog = CreateOrLoad<BakedItemIconCatalog>(CATALOG_PATH);
            content.Seed = BuildSeed();
            AssetDatabase.SaveAssets();
            ItemIconGeneratorSettings.ResetDefaultCache();
            BakedItemIconCatalog.ResetDefaultCache();
            return content;
        }

        public static List<SlotLayoutBinding> CreateEquipmentBindings()
        {
            var bindings = new List<SlotLayoutBinding>
            {
                new('H', "Head", "Шлем", SlotMode.Single, SlotRule.Create(categories: new[] { "Head" }), "Equipment"),
                new('T', "Device", "Устройство", SlotMode.Single, SlotRule.Create(categories: new[] { "Device" }), "Equipment"),
                new('D', "Pda", "ПДА", SlotMode.Single, SlotRule.Create(categories: new[] { "Pda" }), "Equipment"),
                new('O', "Outfit", "Костюм", SlotMode.Single, SlotRule.Create(categories: new[] { "Armor" }), "Equipment"),
                new('R', "Detector", "Детектор", SlotMode.Single, SlotRule.Create(requiredTags: new[] { "Detector" }), "Equipment"),
                new('K', "Backpack", "Рюкзак", SlotMode.Single, SlotRule.Create(categories: new[] { "Backpack" }), "Equipment"),
                new('B', "Binocular", "Бинокль", SlotMode.Single, SlotRule.Create(categories: new[] { "Binocular" }), "Equipment"),
                new('N', "Knife", "Холодное", SlotMode.Single, SlotRule.Create(categories: new[] { WEAPON_CATEGORY }, requiredTags: new[] { KNIFE_TAG, PISTOL_TAG }), "Weapons"),
                new('P', "Primary", "Основное", SlotMode.Single, SlotRule.Create(categories: new[] { WEAPON_CATEGORY }, forbiddenTags: new[] { KNIFE_TAG }), "Weapons"),
                new('S', "Secondary", "Вторичное", SlotMode.Single, SlotRule.Create(categories: new[] { WEAPON_CATEGORY }, forbiddenTags: new[] { KNIFE_TAG }), "Weapons"),
                new('b', "Bolt", "Болт", SlotMode.Single, SlotRule.Create(categories: new[] { "Bolt" }), "Quick"),
                new('g', "Grenade", "Граната", SlotMode.Single, SlotRule.Create(categories: new[] { "Grenade" }), "Quick")
            };

            char[] quickKeys = { '1', '2', '3', '4' };

            for (int i = 0; i < quickKeys.Length; i++)
            {
                bindings.Add(new SlotLayoutBinding(quickKeys[i], $"QuickSlot{i + 1}", $"F{i + 1}", SlotMode.Single, SlotRule.Create(requiredTags: new[] { "QuickUse" }), "Quick"));
            }

            char[] beltKeys = { 'a', 'v', 'w', 'x', 'y' };

            for (int i = 0; i < beltKeys.Length; i++)
            {
                bindings.Add(new SlotLayoutBinding(beltKeys[i], $"Belt{i + 1}", $"Артефакт {i + 1}", SlotMode.Single, SlotRule.Create(categories: new[] { "Artifact" }, startsLocked: true), "Belt"));
            }

            return bindings;
        }

        private static InventorySlotLayout BuildEquipmentLayout()
        {
            var layout = CreateOrLoad<InventorySlotLayout>(EQUIPMENT_LAYOUT_PATH);
            layout.SetData(EQUIPMENT_MASK, CreateEquipmentBindings());
            EditorUtility.SetDirty(layout);
            return layout;
        }

        private static InventorySeed BuildSeed()
        {
            var seed = CreateOrLoad<InventorySeed>(SEED_PATH);
            seed.SetEntries(new List<InventorySeedEntry>());
            EditorUtility.SetDirty(seed);
            return seed;
        }

        private static ItemIconGeneratorSettings BuildGeneratorSettings()
        {
            var settings = CreateOrLoad<ItemIconGeneratorSettings>(SETTINGS_PATH);
            var serialized = new SerializedObject(settings);
            Int(serialized, "_renderLayer", 31);
            Int(serialized, "_renderingLayer", 7);
            serialized.FindProperty("_renderOrigin").vector3Value = new Vector3(8192f, 0f, 8192f);
            Bool(serialized, "_excludeIconLayerFromSceneLights", true);
            Int(serialized, "_rendererLightProbeUsage", 0);
            Int(serialized, "_rendererReflectionProbeUsage", 0);
            Bool(serialized, "_rendererReceiveShadows", false);
            Int(serialized, "_renderTextureFormat", 2);
            Int(serialized, "_iconRenderScale", 1);
            Bool(serialized, "_allowHdr", true);
            Bool(serialized, "_useCameraMsaa", true);
            Float(serialized, "_cameraDistanceMultiplier", 3f);
            Float(serialized, "_cameraDistanceOffset", 1f);
            Float(serialized, "_nearClipPlane", 0.01f);
            Float(serialized, "_farClipBoundsMultiplier", 8f);
            Float(serialized, "_farClipOffset", 10f);
            Bool(serialized, "_enableDirectSpecularLighting", false);
            Bool(serialized, "_enableExposureControl", true);
            Bool(serialized, "_enableAtmosphericScattering", false);
            Bool(serialized, "_enablePostProcess", true);
            Bool(serialized, "_enableColorGrading", true);
            Bool(serialized, "_enableTonemapping", true);
            Bool(serialized, "_enableVolumetrics", false);
            Bool(serialized, "_enableSkyReflection", false);
            Bool(serialized, "_useFrontFillLight", true);
            Float(serialized, "_frontFillLightIntensityMultiplier", 0.65f);
            serialized.FindProperty("_frontFillLightCameraDirection").vector3Value = new Vector3(0f, 0.15f, 1f);
            Bool(serialized, "_useSideFillLight", true);
            Float(serialized, "_sideFillLightIntensityMultiplier", 0.5f);
            serialized.FindProperty("_sideFillLightCameraDirection").vector3Value = new Vector3(-0.85f, 0.1f, 1f);
            Bool(serialized, "_useRimLight", true);
            Float(serialized, "_rimLightIntensityMultiplier", 0.25f);
            serialized.FindProperty("_rimLightCameraDirection").vector3Value = new Vector3(0.35f, 0.2f, -1f);
            Int(serialized, "_generatedLightShadows", 0);
            Int(serialized, "_skyAmbientMode", 1);
            Int(serialized, "_skyRenderingSpace", 0);
            Int(serialized, "_physicallyBasedSkyModel", 1);
            serialized.FindProperty("_physicallyBasedSkyGroundTint").colorValue = new Color(0.122641504f, 0.1043775f, 0.09313812f, 1f);
            Bool(serialized, "_fogEnabled", false);
            Int(serialized, "_fogColorMode", 1);
            Float(serialized, "_maxFogDistance", 5000f);
            Float(serialized, "_meanFreePath", 400f);
            Bool(serialized, "_enableVolumetricFog", false);
            Float(serialized, "_fogAnisotropy", 0.65f);
            Int(serialized, "_exposureMode", 0);
            Float(serialized, "_fixedExposure", 0f);
            Float(serialized, "_exposureCompensation", 0.1f);
            Float(serialized, "_exposureLimitMin", 2f);
            Float(serialized, "_exposureLimitMax", 14f);
            serialized.FindProperty("_histogramPercentages").vector2Value = new Vector2(0f, 50f);
            Float(serialized, "_postExposure", 0.1f);
            Float(serialized, "_contrast", 0f);
            serialized.FindProperty("_colorFilter").colorValue = Color.white;
            Float(serialized, "_hueShift", 0f);
            Float(serialized, "_saturation", 18f);
            Int(serialized, "_tonemappingMode", 1);
            serialized.FindProperty("_iconOutlineColor").colorValue = new Color(0f, 0f, 0f, 0.8509804f);
            serialized.FindProperty("_iconShadowColor").colorValue = new Color(0f, 0f, 0f, 0.34901962f);
            Float(serialized, "_prewarmFrameBudgetMilliseconds", 6f);
            SerializedProperty profiles = serialized.FindProperty("_prewarmSlotProfiles");
            var slotProfiles = new (string Category, int Width, int Height)[] { ("Head", 2, 2), ("Armor", 2, 3), ("Weapon", 5, 2), ("Weapon", 2, 1), ("Accessory", 2, 1), ("Consumable", 1, 1) };
            profiles.arraySize = slotProfiles.Length;

            for (int i = 0; i < slotProfiles.Length; i++)
            {
                SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
                profile.FindPropertyRelative("_restrictCategory").boolValue = true;
                profile.FindPropertyRelative("_acceptedCategory").stringValue = slotProfiles[i].Category;
                profile.FindPropertyRelative("_width").intValue = slotProfiles[i].Width;
                profile.FindPropertyRelative("_height").intValue = slotProfiles[i].Height;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static string[] PreferredWeaponSlots(ItemDefinition definition)
        {
            if (definition.HasTag(KNIFE_TAG))
            {
                return new[] { "Knife" };
            }

            if (definition.HasTag(PISTOL_TAG))
            {
                return new[] { "Secondary", "Knife", "Primary" };
            }

            return new[] { "Primary", "Secondary" };
        }

        private static void SetPreferredSlots(ItemDefinition definition, string[] slotIds)
        {
            var serialized = new SerializedObject(definition);
            SerializedProperty list = serialized.FindProperty("_components");
            int index = -1;

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).managedReferenceValue is EquippableItemComponent)
                {
                    index = i;
                }
            }

            if (index < 0)
            {
                index = list.arraySize;
                list.arraySize++;
                list.GetArrayElementAtIndex(index).managedReferenceValue = new EquippableItemComponent();
            }

            SerializedProperty slots = list.GetArrayElementAtIndex(index).FindPropertyRelative("_preferredSlotIds");
            slots.arraySize = slotIds.Length;

            for (int i = 0; i < slotIds.Length; i++)
            {
                slots.GetArrayElementAtIndex(i).stringValue = slotIds[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        private static void SetComponents(ItemDefinition definition, params ItemDefinitionComponent[] components)
        {
            var serialized = new SerializedObject(definition);
            SerializedProperty list = serialized.FindProperty("_components");
            list.arraySize = components.Length;

            for (int i = 0; i < components.Length; i++)
            {
                list.GetArrayElementAtIndex(i).managedReferenceValue = components[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        private static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset != null)
            {
                return asset;
            }

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static void EnsureFolder(string folder)
        {
            string[] segments = folder.Replace('\\', '/').Split('/');
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

        private static void Int(SerializedObject serialized, string name, int value) => serialized.FindProperty(name).intValue = value;
        private static void Float(SerializedObject serialized, string name, float value) => serialized.FindProperty(name).floatValue = value;
        private static void Bool(SerializedObject serialized, string name, bool value) => serialized.FindProperty(name).boolValue = value;
    }
}
