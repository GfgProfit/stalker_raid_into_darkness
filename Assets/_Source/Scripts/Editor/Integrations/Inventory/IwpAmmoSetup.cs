using System;
using System.Collections.Generic;
using System.IO;
using Inventory;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Player.Editor;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Ogf;
using XRay.Importer.Sources.Vfs;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class IwpAmmoSetup
    {
        public const string AMMO_FOLDER = "Assets/_Source/Resources/Items/Ammo";

        private const string SCENE_PATH = "Assets/TEST_SCENE.unity";
        private const string SCENE_GROUP = "Ammo";
        private const string SCOPE_TEMPLATE_GUID = "867a7f373388760469fc24ccebf9f005";
        private const string AUG_DEFINITION = IwpWeaponSetup.DEFINITION_PATH;
        private const string SHARED_AUDIO_FOLDER = AMMO_FOLDER + "/_Shared Audio";
        private const uint OGF_DESCRIPTION_CHUNK = 0x12;
        private const int OGF_HEADER_SIZE = 44;

        [MenuItem("Tools/Inventory/Setup IWP Ammo")]
        public static void SetupFromMenu()
        {
            CheckScene();
            ImportModels();
            GenerateMaterials(0, LoadCatalog().Entries.Length);
            BuildItems();
            Debug.Log(PlaceItems());
        }

        internal static bool IsAmmoResourcePath(string path) => path.StartsWith(AMMO_FOLDER + "/", StringComparison.Ordinal);

        internal static string ItemFolder(string section) => AMMO_FOLDER + "/" + section;

        internal static string DefinitionPath(string section) => ItemFolder(section) + "/" + section + "_definition.asset";

        internal static string[] ReadAugAmmoClasses() => LoadCatalog().AugAmmo;

        public static int ImportModels()
        {
            Catalog catalog = LoadCatalog();
            foreach (Entry entry in catalog.Entries)
            {
                string source = Path.Combine(IwpWeaponSetup.IWP_ROOT, entry.Model);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException("Missing IWP ammo model.", source);
                }

                StalkerXRaySources.EnsureFolder(ItemFolder(entry.Section));
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (Entry entry in catalog.Entries)
                {
                    string source = Path.Combine(IwpWeaponSetup.IWP_ROOT, entry.Model);
                    string target = ModelPath(entry);
                    byte[] model = ReadModel(source);
                    if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(model))
                    {
                        File.WriteAllBytes(target, model);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return catalog.Entries.Length;
        }

        public static int GenerateMaterials(int offset, int count)
        {
            Entry[] entries = LoadCatalog().Entries;
            var paths = new List<string>(count);
            for (int i = offset; i < offset + count; i++)
            {
                paths.Add(ModelPath(entries[i]));
            }

            int failures = IwpWeaponSetup.GenerateIwpMaterials(paths);
            if (failures != 0)
            {
                throw new InvalidOperationException("IWP ammo material generation failed: " + failures);
            }

            return paths.Count;
        }

        public static int BuildItems()
        {
            Catalog catalog = LoadCatalog();
            var scope = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(SCOPE_TEMPLATE_GUID));
            var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(InventoryDemoContentBuilder.DATABASE_PATH);
            var weapon = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AUG_DEFINITION);
            if (scope == null || database == null || weapon == null)
            {
                throw new InvalidOperationException("The scope template, AUG item and ItemDatabase are required.");
            }

            var models = new List<GameObject>(catalog.Entries.Length);
            var sounds = new Dictionary<string, List<AudioClip>>(StringComparer.Ordinal);
            foreach (Entry entry in catalog.Entries)
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(entry));
                if (model == null)
                {
                    throw new InvalidOperationException("Missing IWP ammo model: " + entry.Section);
                }

                models.Add(model);
                if (!sounds.ContainsKey(entry.TakeSoundCategory))
                {
                    sounds.Add(entry.TakeSoundCategory, LoadPickupSounds(entry.TakeSoundCategory));
                }
            }

            using var scopeSerialized = new SerializedObject(scope);
            var definitions = new List<ItemDefinition>(catalog.Entries.Length);
            for (int i = 0; i < catalog.Entries.Length; i++)
            {
                Entry entry = catalog.Entries[i];
                ItemDefinition definition = CreateDefinition(entry, models[i], scopeSerialized.FindProperty("_icon"));
                GraInventorySoundSetup.SetTakeSound(definition, entry.TakeSoundCategory, sounds[entry.TakeSoundCategory]);
                definitions.Add(definition);
            }

            WorldItemPrefabBuilder.Build(definitions, true);
            AmmoBallisticsSetup.Configure();
            var items = new List<ItemDefinition>(database.Definitions);
            items.RemoveAll(item => item != null && item.GetComponent<AmmoStatsComponent>() != null);
            items.AddRange(definitions);
            database.SetDefinitions(items);
            EditorUtility.SetDirty(database);
            ConfigureAug(weapon, catalog.AugAmmo);
            AssetDatabase.SaveAssets();
            return definitions.Count;
        }

        public static int PlaceItems()
        {
            Scene scene = CheckScene();
            StalkerPlayerController player = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (player == null)
                {
                    player = root.GetComponentInChildren<StalkerPlayerController>(true);
                }
            }

            if (player == null)
            {
                throw new InvalidOperationException("The inventory test scene has no player.");
            }

            var prefabs = new List<WorldItem>();
            foreach (Entry entry in LoadCatalog().Entries)
            {
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(DefinitionPath(entry.Section));
                var prefab = definition?.GetComponent<WorldItemComponent>()?.Prefab;
                if (prefab == null)
                {
                    throw new InvalidOperationException("Missing IWP ammo prefab: " + entry.Section);
                }

                prefabs.Add(prefab);
            }

            WorldItemPrefabBuilder.AppendToScene(scene, prefabs, player.transform, SCENE_GROUP);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == WorldItemPrefabBuilder.WORLD_ITEMS_ROOT)
                {
                    foreach (WorldItem item in root.transform.Find(SCENE_GROUP).GetComponentsInChildren<WorldItem>())
                    {
                        item.name = item.Definition.name;
                    }

                    break;
                }
            }

            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException("Could not save the ammo sample group.");
            }

            return prefabs.Count;
        }

        private static ItemDefinition CreateDefinition(Entry entry, GameObject model, SerializedProperty icon)
        {
            string path = DefinitionPath(entry.Section);
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool initializeIcon = definition == null;
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            definition.name = entry.Section;
            using var serialized = new SerializedObject(definition);
            serialized.FindProperty("_definitionId").stringValue = AssetDatabase.AssetPathToGUID(path);
            serialized.FindProperty("_displayName").stringValue = entry.DisplayName;
            serialized.FindProperty("_shortName").stringValue = entry.ShortName;
            serialized.FindProperty("_description").stringValue = entry.Description;
            serialized.FindProperty("_category").stringValue = SCENE_GROUP;
            SerializedProperty tags = serialized.FindProperty("_tags");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).stringValue = SCENE_GROUP;
            if (definition.IconOverride == null)
            {
                serialized.FindProperty("_size").vector2IntValue = GraWeaponAddonSetup.ToProjectSize(entry.GridWidth, entry.GridHeight);
            }

            serialized.FindProperty("_weight").floatValue = entry.BoxWeight / entry.BoxSize;
            serialized.FindProperty("_maxStack").intValue = 0;
            serialized.FindProperty("_allowRotation").boolValue = true;
            serialized.FindProperty("_visualPrefab").objectReferenceValue = model;
            if (initializeIcon)
            {
                using SerializedProperty current = icon.Copy();
                using SerializedProperty end = current.GetEndProperty();
                while (current.Next(!current.isArray) && !SerializedProperty.EqualContents(current, end))
                {
                    if (current.propertyType != SerializedPropertyType.Generic && current.propertyPath != "_icon._generateIcon")
                    {
                        serialized.CopyFromSerializedProperty(current);
                    }
                }

                serialized.FindProperty("_icon._generateIcon").boolValue = false;
            }

            SerializedProperty components = serialized.FindProperty("_components");
            components.ClearArray();
            components.arraySize = 1;
            SerializedProperty stats = components.GetArrayElementAtIndex(0);
            stats.managedReferenceValue = new AmmoStatsComponent();
            stats.FindPropertyRelative("_section").stringValue = entry.Section;
            stats.FindPropertyRelative("_boxSize").intValue = entry.BoxSize;
            stats.FindPropertyRelative("_boxWeight").floatValue = entry.BoxWeight;
            stats.FindPropertyRelative("_cost").intValue = entry.Cost;
            stats.FindPropertyRelative("_armorPiercing").floatValue = entry.ArmorPiercing;
            stats.FindPropertyRelative("_hitMultiplier").floatValue = entry.HitMultiplier;
            stats.FindPropertyRelative("_impulseMultiplier").floatValue = entry.ImpulseMultiplier;
            stats.FindPropertyRelative("_speedMultiplier").floatValue = entry.SpeedMultiplier;
            stats.FindPropertyRelative("_dispersionMultiplier").floatValue = entry.DispersionMultiplier;
            stats.FindPropertyRelative("_airResistance").floatValue = entry.AirResistance;
            stats.FindPropertyRelative("_buckshot").intValue = entry.Data.ProjectileCount;
            stats.FindPropertyRelative("_averageWeaponHitPower").floatValue = -1f;
            stats.FindPropertyRelative("_monsterDamageMultiplier").floatValue = 1f;
            stats.FindPropertyRelative("_penetration").intValue = Mathf.RoundToInt(entry.ArmorPiercing / 1.5f * 100f);
            stats.FindPropertyRelative("_damagePerProjectile").intValue = -1;
            stats.FindPropertyRelative("_hasTooltipParameters").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static List<AudioClip> LoadPickupSounds(string category)
        {
            string sound = category == "ammoshell" ? "shotshell" : category;
            string prefix = "inv_items_" + sound + "_";
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SHARED_AUDIO_FOLDER }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal))
                {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.Ordinal);
            if (paths.Count == 0)
            {
                throw new InvalidOperationException("No existing pickup sounds for " + category);
            }

            return paths.ConvertAll(path => AssetDatabase.LoadAssetAtPath<AudioClip>(path));
        }

        private static void ConfigureAug(ItemDefinition weapon, string[] sections)
        {
            using var serialized = new SerializedObject(weapon);
            SerializedProperty components = serialized.FindProperty("_components");
            for (int i = 0; i < components.arraySize; i++)
            {
                SerializedProperty component = components.GetArrayElementAtIndex(i);
                if (component.managedReferenceValue is WeaponStatsComponent)
                {
                    SerializedProperty classes = component.FindPropertyRelative("_ammoClasses");
                    classes.arraySize = sections.Length;
                    for (int index = 0; index < sections.Length; index++)
                    {
                        classes.GetArrayElementAtIndex(index).stringValue = sections[index];
                    }

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(weapon);
                    return;
                }
            }

            throw new InvalidOperationException("AUG has no weapon stats.");
        }

        private static Catalog LoadCatalog() => JsonUtility.FromJson<Catalog>(File.ReadAllText(AmmoBallisticsSetup.PROFILES_PATH));

        private static string ModelPath(Entry entry) => ItemFolder(entry.Section) + "/" + Path.GetFileName(entry.Model);

        private static byte[] ReadModel(string path)
        {
            byte[] source = File.ReadAllBytes(path);
            using var output = new MemoryStream(source.Length);
            using var writer = new BinaryWriter(output);
            bool changed = false;
            bool isOg7b = string.Equals(Path.GetFileName(path), "ammo_og_7b.ogf", StringComparison.OrdinalIgnoreCase);
            int offset = 0;
            while (source.Length - offset >= 8)
            {
                uint type = BitConverter.ToUInt32(source, offset);
                int length = BitConverter.ToInt32(source, offset + 4);
                if (length < 0 || length > source.Length - offset - 8)
                {
                    throw new InvalidDataException("Invalid IWP model chunk: " + path);
                }

                if (isOg7b && type == (OgfFormat.CHUNK_HEADER | XRayChunks.COMPRESSED_MASK) && length == OGF_HEADER_SIZE && source[offset + 8] == 4)
                {
                    writer.Write(OgfFormat.CHUNK_HEADER);
                    writer.Write(length);
                    writer.Write(source, offset + 8, length);
                    changed = true;
                }
                else if (isOg7b && type == (OGF_DESCRIPTION_CHUNK | XRayChunks.COMPRESSED_MASK))
                {
                    changed = true;
                }
                else if ((type & XRayChunks.COMPRESSED_MASK) != 0)
                {
                    byte[] bytes = XRayCompression.DecompressLzhuf(source, offset + 8, length);
                    writer.Write(type & ~XRayChunks.COMPRESSED_MASK);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    changed = true;
                }
                else
                {
                    writer.Write(source, offset, 8 + length);
                }

                offset += 8 + length;
            }

            writer.Write(source, offset, source.Length - offset);
            return changed ? output.ToArray() : source;
        }

        private static Scene CheckScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SCENE_PATH || scene.isDirty)
            {
                throw new InvalidOperationException("Open the saved TEST_SCENE in Edit Mode before placing IWP ammo.");
            }

            return scene;
        }

        [Serializable]
        private sealed class Catalog
        {
            [SerializeField] private string[] _augAmmo;
            [SerializeField] private Entry[] _entries;

            public string[] AugAmmo => _augAmmo;
            public Entry[] Entries => _entries;
        }

        [Serializable]
        private sealed class Entry
        {
            [SerializeField] private string _section;
            [SerializeField] private string _model;
            [SerializeField] private string _displayName;
            [SerializeField] private string _shortName;
            [SerializeField] private string _description;
            [SerializeField] private int _boxSize;
            [SerializeField] private float _boxWeight;
            [SerializeField] private int _gridWidth;
            [SerializeField] private int _gridHeight;
            [SerializeField] private int _cost;
            [SerializeField] private string _takeSoundCategory;
            [SerializeField] private float _armorPiercing;
            [SerializeField] private float _hitMultiplier;
            [SerializeField] private float _impulseMultiplier;
            [SerializeField] private float _speedMultiplier;
            [SerializeField] private float _dispersionMultiplier;
            [SerializeField] private float _airResistance;
            [SerializeField] private BallisticAmmoData _data;

            public string Section => _section;
            public string Model => _model;
            public string DisplayName => _displayName;
            public string ShortName => _shortName;
            public string Description => _description;
            public int BoxSize => _boxSize;
            public float BoxWeight => _boxWeight;
            public int GridWidth => _gridWidth;
            public int GridHeight => _gridHeight;
            public int Cost => _cost;
            public string TakeSoundCategory => _takeSoundCategory;
            public float ArmorPiercing => _armorPiercing;
            public float HitMultiplier => _hitMultiplier;
            public float ImpulseMultiplier => _impulseMultiplier;
            public float SpeedMultiplier => _speedMultiplier;
            public float DispersionMultiplier => _dispersionMultiplier;
            public float AirResistance => _airResistance;
            public BallisticAmmoData Data => _data;
        }
    }
}
