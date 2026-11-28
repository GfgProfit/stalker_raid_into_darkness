using System.Collections.Generic;
using System.IO;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    public static class InventoryDemoContentBuilder
    {
        public const string ROOT = "Assets/_Source/Settings/Inventory";
        public const string DEFINITIONS_FOLDER = "Assets/_Source/Items/Definitions";
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
        public const string EQUIPMENT_MASK = "HHTTDD\nHHOORR\nKKOOBB\nKKOONN\nPPPPP.\nPPPPP.\nSSSSS.\nSSSSS.\n1234..\navwxy.";

        internal const string CLOTHES_FOLDER = "Assets/_Source/Resources/Items/Clothes";

        internal static readonly string[] ITEM_DEFINITION_FOLDERS = { DEFINITIONS_FOLDER, GraWeaponAddonSetup.ADDONS_FOLDER, IwpAmmoSetup.AMMO_FOLDER, CLOTHES_FOLDER, IwpWeaponSetup.WEAPONS_FOLDER };

        internal static bool IsItemResourcePath(string path) => GraWeaponAddonSetup.IsAddonResourcePath(path) || IwpAmmoSetup.IsAmmoResourcePath(path) || IsClothesResourcePath(path) || IwpWeaponSetup.IsWeaponResourcePath(path);

        internal static bool IsClothesResourcePath(string path) => path?.StartsWith(CLOTHES_FOLDER + "/", System.StringComparison.Ordinal) == true;

        internal static string GetItemResourceName(ItemDefinition definition, string path)
        {
            if (IwpWeaponSetup.IsWeaponResourcePath(path))
            {
                string section = definition.GetComponent<WeaponStatsComponent>()?.Section;
                return string.IsNullOrEmpty(section) ? definition.name : section;
            }

            EquipmentStatsComponent stats = IsClothesResourcePath(path) ? definition.GetComponent<EquipmentStatsComponent>() : null;
            return stats != null && stats.Section.Length > 0 ? stats.Section : definition.name;
        }

        public static InventoryDemoContent Build()
        {
            EnsureFolder(LAYOUTS_FOLDER);
            EnsureFolder(RESOURCES_FOLDER);

            var content = new InventoryDemoContent();

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
                    var serialized = new SerializedObject(definition);
                    AddComponent(serialized.FindProperty("_components"), new ConsumableItemComponent());
                    serialized.ApplyModifiedProperties();
                }

                if (definition.IsCategory(WEAPON_CATEGORY) && definition.GetComponent<EquippableItemComponent>() == null)
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
            content.Settings = BuildGeneratorSettings(content.EquipmentLayout);
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
                new('H', SlotId.Head, "Шлем", SlotMode.Single, SlotRule.Create(categories: new[] { "Head" }), "Equipment"),
                new('T', SlotId.Device, "Устройство", SlotMode.Single, SlotRule.Create(categories: new[] { "Device" }), "Equipment"),
                new('D', SlotId.Pda, "ПДА", SlotMode.Single, SlotRule.Create(categories: new[] { "Pda" }), "Equipment"),
                new('O', SlotId.Outfit, "Костюм", SlotMode.Single, SlotRule.Create(categories: new[] { "Armor" }), "Equipment"),
                new('R', SlotId.Detector, "Детектор", SlotMode.Single, SlotRule.Create(requiredTags: new[] { "Detector" }), "Equipment"),
                new('K', SlotId.Backpack, "Рюкзак", SlotMode.Single, SlotRule.Create(categories: new[] { "Backpack" }), "Equipment"),
                new('B', SlotId.Binocular, "Бинокль", SlotMode.Single, SlotRule.Create(categories: new[] { "Binocular" }), "Equipment"),
                new('N', SlotId.Knife, "Холодное", SlotMode.Single, SlotRule.Create(categories: new[] { WEAPON_CATEGORY }, requiredTags: new[] { KNIFE_TAG, PISTOL_TAG }), "Weapons"),
                new('P', SlotId.Primary, "Основное", SlotMode.Single, SlotRule.Create(categories: new[] { WEAPON_CATEGORY }, forbiddenTags: new[] { KNIFE_TAG, PISTOL_TAG }), "Weapons"),
                new('S', SlotId.Secondary, "Вторичное", SlotMode.Single, SlotRule.Create(categories: new[] { WEAPON_CATEGORY }, forbiddenTags: new[] { KNIFE_TAG, PISTOL_TAG }), "Weapons")
            };

            (char Key, SlotId Id)[] quickSlots = { ('1', SlotId.QuickSlot1), ('2', SlotId.QuickSlot2), ('3', SlotId.QuickSlot3), ('4', SlotId.QuickSlot4) };

            for (int i = 0; i < quickSlots.Length; i++)
            {
                bindings.Add(new SlotLayoutBinding(quickSlots[i].Key, quickSlots[i].Id, $"F{i + 1}", SlotMode.Single, SlotRule.Create(requiredTags: new[] { "QuickUse" }), "Quick"));
            }

            (char Key, SlotId Id)[] beltSlots = { ('a', SlotId.Belt1), ('v', SlotId.Belt2), ('w', SlotId.Belt3), ('x', SlotId.Belt4), ('y', SlotId.Belt5) };

            for (int i = 0; i < beltSlots.Length; i++)
            {
                bindings.Add(new SlotLayoutBinding(beltSlots[i].Key, beltSlots[i].Id, $"Артефакт {i + 1}", SlotMode.Single, SlotRule.Create(categories: new[] { "Artifact" }, startsLocked: true), "Belt"));
            }

            return bindings;
        }

        private static InventorySlotLayout BuildEquipmentLayout()
        {
            var existing = AssetDatabase.LoadAssetAtPath<InventorySlotLayout>(EQUIPMENT_LAYOUT_PATH);

            if (existing != null)
            {
                return existing;
            }

            var layout = CreateOrLoad<InventorySlotLayout>(EQUIPMENT_LAYOUT_PATH);
            layout.SetData(EQUIPMENT_MASK, CreateEquipmentBindings());
            EditorUtility.SetDirty(layout);
            return layout;
        }

        private static InventorySeed BuildSeed() => CreateOrLoad<InventorySeed>(SEED_PATH);

        private static ItemIconGeneratorSettings BuildGeneratorSettings(InventorySlotLayout equipmentLayout)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ItemIconGeneratorSettings>(SETTINGS_PATH);

            if (existing != null)
            {
                return existing;
            }

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
                bool usesLargeWeaponSlots = slotProfiles[i] == (WEAPON_CATEGORY, 5, 2);
                profile.FindPropertyRelative("_equipmentLayout").objectReferenceValue = usesLargeWeaponSlots ? equipmentLayout : null;
                SerializedProperty slotIds = profile.FindPropertyRelative("_slotIds");
                slotIds.arraySize = usesLargeWeaponSlots ? 2 : 0;
                if (usesLargeWeaponSlots)
                {
                    slotIds.GetArrayElementAtIndex(0).intValue = (int)SlotId.Primary;
                    slotIds.GetArrayElementAtIndex(1).intValue = (int)SlotId.Secondary;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static SlotId[] PreferredWeaponSlots(ItemDefinition definition)
        {
            if (definition.HasTag(KNIFE_TAG) || definition.HasTag(PISTOL_TAG))
            {
                return new[] { SlotId.Knife };
            }

            return new[] { SlotId.Primary, SlotId.Secondary };
        }

        private static void SetPreferredSlots(ItemDefinition definition, SlotId[] slotIds)
        {
            var serialized = new SerializedObject(definition);
            SerializedProperty component = AddComponent(serialized.FindProperty("_components"), new EquippableItemComponent());
            SetSlotIds(component.FindPropertyRelative("_preferredSlotIds"), slotIds);
            serialized.ApplyModifiedProperties();
        }

        private static SerializedProperty AddComponent(SerializedProperty components, ItemDefinitionComponent component)
        {
            int index = components.arraySize;
            components.arraySize++;
            SerializedProperty entry = components.GetArrayElementAtIndex(index);
            entry.managedReferenceValue = component;
            return entry;
        }

        internal static void SetSlotIds(SerializedProperty array, IReadOnlyList<SlotId> slotIds)
        {
            array.arraySize = slotIds.Count;

            for (int i = 0; i < slotIds.Count; i++)
            {
                array.GetArrayElementAtIndex(i).intValue = (int)slotIds[i];
            }
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
