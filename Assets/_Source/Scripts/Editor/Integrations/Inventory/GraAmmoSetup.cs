using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Inventory;
using Inventory.Editor;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Player.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Sources.Ltx;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class GraAmmoSetup
    {
        public const string MODELS_FOLDER = "Assets/_Source/Items/Ammo/Models";
        public const string DEFINITIONS_FOLDER = "Assets/_Source/Items/Definitions/Ammo";
        public const string SCENE_GROUP = "Ammo";

        private const string LOG = "[GraAmmo] ";
        private const string CATEGORY = "Ammo";
        private const string STASH_CONFIG = "configs/items/settings/grok_treasure_manager.ltx";
        private const string BALLISTICS_SCRIPT = "scripts/grok_bo.script";
        private const int EXPECTED_COUNT = 45;

        private static readonly string[] INDEX_SCOPE = { "configs", "meshes/dynamics/weapons/wpn_ammo", "textures/ui", "sounds", "materials", "scripts", StalkerGameMtlLibrary.GAMEMTL };
        private static readonly string[] SECTIONS = { "ammo_9x18_fmj", "ammo_9x18_pmm", "ammo_9x18_ap", "ammo_9x19_fmj", "ammo_9x19_pbp", "ammo_9x19_ap", "ammo_5.7x28_ss190", "ammo_5.7x28_ss195", "ammo_7.62x25_p", "ammo_7.62x25_ps", "ammo_11.43x23_fmj", "ammo_11.43x23_hydro", "ammo_5.45x39_fmj", "ammo_5.45x39_ep", "ammo_5.45x39_ap", "ammo_5.56x45_fmj", "ammo_5.56x45_ss190", "ammo_5.56x45_ap", "ammo_9x39_pab9", "ammo_9x39_ap", "ammo_12x70_buck", "ammo_12x76_zhekan", "ammo_12x70_buck_self", "ammo_12x76_bull", "ammo_12x76_dart", "ammo_7.62x39_fmj", "ammo_7.62x39_ap", "ammo_7.92x33_fmj", "ammo_7.92x33_ap", "ammo_7.62x51_fmj", "ammo_7.62x51_ap", "ammo_pkm_100", "ammo_7.62x54_7h1", "ammo_7.62x54_ap", "ammo_7.62x54_7h14", "ammo_12.7x55_fmj", "ammo_12.7x55_ap", "ammo_magnum_300", "ammo_357_hp_mag", "ammo_23x75_shrapnel", "ammo_23x75_barrikada", "ammo_9x21_sp10", "ammo_20x70_buck", "ammo_338_federal", "ammo_12x70_raid" };
        private static readonly int[] PENETRATION_THRESHOLDS = { 10, 20, 30, 40, 50, 70, 10000 };
        private static readonly int[] DAMAGE_THRESHOLDS = { 24, 35, 45, 130, 1000, 10000 };
        private static readonly HashSet<string> WEAPON_KINDS = new HashSet<string>(StringComparer.Ordinal) { "w_pistol", "w_shotgun", "w_smg", "w_rifle", "w_sniper" };
        private static readonly Regex MULTIPLIER_TABLE = new Regex("monster_ammo_damage_mult\\s*=\\s*\\{(.*?)\\}", RegexOptions.Singleline);
        private static readonly Regex MULTIPLIER_ENTRY = new Regex("\\[\"([^\"]+)\"\\]\\s*=\\s*([\\d.]+)");

        [MenuItem("Tools/Inventory/Setup GRA Ammo")]
        public static void SetupFromMenu()
        {
            ImportModels();
            CreateItems();
            PlaceItems();
        }

        public static void SetupBatch()
        {
            bool success = false;

            try
            {
                SetupFromMenu();
                success = true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        public static int UpdateInventorySettings()
        {
            int updated = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { DEFINITIONS_FOLDER }))
            {
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));

                if (definition == null || definition.GetComponent<AmmoStatsComponent>() == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(definition);
                serialized.FindProperty("_maxStack").intValue = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(definition);
                updated++;
            }

            AssetDatabase.SaveAssets();
            return updated;
        }

        public static string ImportModels()
        {
            CheckScene();
            StalkerXRaySources sources = OpenSources();
            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(sources.Index);
            List<string> sections = CollectSections(system);
            var models = new SortedSet<string>(StringComparer.Ordinal);
            var log = new StringBuilder(sources.Describe()).AppendLine();
            StalkerXRaySources.EnsureFolder(MODELS_FOLDER);

            foreach (string section in sections)
            {
                string model = ModelPath(system, section);

                if (sources.Exists(model) == false)
                {
                    throw new InvalidOperationException(LOG + section + ": missing " + model);
                }

                models.Add(model);
            }

            var imported = new List<string>();
            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (string model in models)
                {
                    imported.Add(sources.CopyToProject(model, MODELS_FOLDER, Path.GetFileName(model)));
                    log.AppendLine(model + " <- " + sources.GetSourceName(model));
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            int failures = GraWeaponAddonSetup.SettleMaterials(imported, log);

            if (failures > 0)
            {
                throw new InvalidOperationException(LOG + log);
            }

            Debug.Log(LOG + log);
            return $"Imported {models.Count} OGF models for {sections.Count} ammo sections.";
        }

        public static string CreateItems()
        {
            CheckScene();
            StalkerXRaySources sources = OpenSources();
            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(sources.Index);
            XRayStringTable strings = XRayStringTable.Load(sources.Index, "rus");
            Dictionary<string, Color32> colors = GraTextFormatter.LoadColors(sources);
            List<string> sections = CollectSections(system);
            Dictionary<string, double> averages = CollectWeaponAverages(sources, system);
            Dictionary<string, double> multipliers = ReadMonsterMultipliers(sources);
            var log = new StringBuilder(sources.Describe()).AppendLine();
            Dictionary<string, Sprite> icons = GraWeaponAddonSetup.ImportIcons(sources, system, sections, log);
            var definitions = new List<ItemDefinition>();
            StalkerXRaySources.EnsureFolder(DEFINITIONS_FOLDER);

            foreach (string section in sections)
            {
                string modelPath = MODELS_FOLDER + "/" + Path.GetFileName(ModelPath(system, section));
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

                if (model == null || icons.TryGetValue(section, out Sprite icon) == false)
                {
                    throw new InvalidOperationException(LOG + section + ": imported model or HD GRA sprite is missing.\n" + log);
                }

                double average = averages.TryGetValue(section, out double value) ? value : -1.0;
                double multiplier = multipliers.TryGetValue(section, out value) ? value : 0.85;
                definitions.Add(CreateDefinition(system, strings, colors, section, model, icon, average, multiplier));
            }

            RegisterDefinitions(definitions);
            int sounded = GraInventorySoundSetup.SetupTakeSounds(sources, definitions, log);

            if (sounded != sections.Count)
            {
                throw new InvalidOperationException(LOG + "Some ammo sections have no pickup sounds.\n" + log);
            }

            List<WorldItem> prefabs = WorldItemPrefabBuilder.Build(definitions);

            foreach (WorldItem prefab in prefabs)
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                GameObject root = PrefabUtility.LoadPrefabContents(path);

                try
                {
                    AmmoStatsComponent stats = prefab.Definition.GetComponent<AmmoStatsComponent>();
                    root.GetComponent<WorldItem>().Configure(prefab.Definition, stats.BoxSize);
                    root.GetComponent<Rigidbody>().mass = Mathf.Max(0.001f, stats.BoxWeight);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            StalkerCollideSoundLibrary collide = GraInventorySoundSetup.SetupCollideSounds(sources, log);

            if (collide == null)
            {
                throw new InvalidOperationException(LOG + "Collision sound library is missing.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log(LOG + log);
            return $"Created {definitions.Count} definitions, HD sprites and full-box world prefabs with pickup and impact sounds.";
        }

        public static string PlaceItems()
        {
            Scene scene = CheckScene();
            var definitions = new List<ItemDefinition>();
            var prefabs = new List<WorldItem>();
            StalkerPlayerController player = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                player = player != null ? player : root.GetComponentInChildren<StalkerPlayerController>(true);
            }

            if (player == null || scene.GetRootGameObjects().Length == 0)
            {
                throw new InvalidOperationException(LOG + "OutdoorsScene has no player.");
            }

            foreach (string section in CollectSections(XRayLtxDatabase.LoadSystem(OpenSources().Index)))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(DEFINITIONS_FOLDER + "/" + section + ".asset");

                if (definition == null || definition.GetComponent<WorldItemComponent>()?.Prefab == null)
                {
                    throw new InvalidOperationException(LOG + section + ": world prefab is missing.");
                }

                definitions.Add(definition);
            }

            definitions.Sort((left, right) => string.CompareOrdinal(left.name, right.name));

            foreach (ItemDefinition definition in definitions)
            {
                prefabs.Add(definition.GetComponent<WorldItemComponent>().Prefab);
            }

            WorldItemPrefabBuilder.AppendToScene(scene, prefabs, player.transform, SCENE_GROUP);

            if (EditorSceneManager.SaveScene(scene) == false)
            {
                throw new InvalidOperationException(LOG + "Could not save OutdoorsScene.");
            }

            return $"Placed {prefabs.Count} ammo boxes in WorldItems/{SCENE_GROUP}.";
        }

        private static ItemDefinition CreateDefinition(XRayLtxDatabase system, XRayStringTable strings, IReadOnlyDictionary<string, Color32> colors, string section, GameObject model, Sprite icon, double average, double monsterMultiplier)
        {
            string path = DEFINITIONS_FOLDER + "/" + section + ".asset";
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            int boxSize = Mathf.Max(1, (int)Number(system, section, "box_size", 1));
            float boxWeight = (float)Number(system, section, "inv_weight", 0);
            int buckshot = Math.Max(1, (int)Number(system, section, "buck_shot", 1));
            double armorPiercing = Number(system, section, "k_ap", 1);
            double hitMultiplier = Number(system, section, "k_hit", 1);
            int penetration = Normalize(armorPiercing);
            double damage = hitMultiplier * (1.0 + (monsterMultiplier - 0.85) / 2.0) * average;
            damage *= section == "ammo_12x70_raid" ? 20.0 : 1.0;
            int damagePerProjectile = average >= 0 ? Normalize(damage) : -1;
            string description = strings.Translate(Get(system, section, "description"));

            if (average >= 0)
            {
                string penetrationLabel = "st_raid_penetrates_class_armor_" + ThresholdIndex(penetration, PENETRATION_THRESHOLDS) + "_desc";
                string penetrationText = penetration < PENETRATION_THRESHOLDS[0] ? strings.Translate(penetrationLabel) : strings.Translate("st_raid_penetrates_class_armor_desc") + " " + strings.Translate(penetrationLabel);
                string damageLabel = "st_raid_tissue_damage_" + (ThresholdIndex(damagePerProjectile * buckshot, DAMAGE_THRESHOLDS) + 1) + "_desc";
                description += "\\n" + penetrationText + " (" + penetration + ")\\n" + strings.Translate("st_raid_tissue_damage_desc") + " " + strings.Translate(damageLabel) + " (" + damagePerProjectile * buckshot + ")";
                description += buckshot > 1 ? "[" + damagePerProjectile + "x" + buckshot + "]" : string.Empty;
            }

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_definitionId").stringValue = AssetDatabase.AssetPathToGUID(path);
            serialized.FindProperty("_displayName").stringValue = strings.Translate(Get(system, section, "inv_name"));
            serialized.FindProperty("_shortName").stringValue = strings.Translate(Get(system, section, "inv_name_short"));
            serialized.FindProperty("_description").stringValue = GraTextFormatter.Format(description, colors);
            serialized.FindProperty("_category").stringValue = CATEGORY;
            SerializedProperty tags = serialized.FindProperty("_tags");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).stringValue = CATEGORY;
            serialized.FindProperty("_size").vector2IntValue = GraWeaponAddonSetup.ToProjectSize((int)Number(system, section, "inv_grid_width", 2), (int)Number(system, section, "inv_grid_height", 2));
            serialized.FindProperty("_weight").floatValue = boxWeight / boxSize;
            serialized.FindProperty("_maxStack").intValue = 0;
            serialized.FindProperty("_allowRotation").boolValue = true;
            serialized.FindProperty("_visualPrefab").objectReferenceValue = model;
            serialized.FindProperty("_iconOverride").objectReferenceValue = icon;
            serialized.FindProperty("_icon._generateIcon").boolValue = false;
            SerializedProperty components = serialized.FindProperty("_components");
            components.ClearArray();
            components.arraySize = 1;
            SerializedProperty stats = components.GetArrayElementAtIndex(0);
            stats.managedReferenceValue = new AmmoStatsComponent();
            stats.FindPropertyRelative("_section").stringValue = section;
            stats.FindPropertyRelative("_boxSize").intValue = boxSize;
            stats.FindPropertyRelative("_boxWeight").floatValue = boxWeight;
            stats.FindPropertyRelative("_cost").intValue = (int)Number(system, section, "cost", 0);
            stats.FindPropertyRelative("_armorPiercing").floatValue = (float)armorPiercing;
            stats.FindPropertyRelative("_hitMultiplier").floatValue = (float)hitMultiplier;
            stats.FindPropertyRelative("_impulseMultiplier").floatValue = (float)Number(system, section, "k_impulse", 1);
            stats.FindPropertyRelative("_speedMultiplier").floatValue = (float)Number(system, section, "k_bullet_speed", 1);
            stats.FindPropertyRelative("_dispersionMultiplier").floatValue = (float)Number(system, section, "k_disp", 1);
            stats.FindPropertyRelative("_airResistance").floatValue = (float)Number(system, section, "k_air_resistance", 0);
            stats.FindPropertyRelative("_buckshot").intValue = buckshot;
            stats.FindPropertyRelative("_averageWeaponHitPower").floatValue = (float)average;
            stats.FindPropertyRelative("_monsterDamageMultiplier").floatValue = (float)monsterMultiplier;
            stats.FindPropertyRelative("_penetration").intValue = penetration;
            stats.FindPropertyRelative("_damagePerProjectile").intValue = damagePerProjectile;
            stats.FindPropertyRelative("_hasTooltipParameters").boolValue = average >= 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static Dictionary<string, double> CollectWeaponAverages(StalkerXRaySources sources, XRayLtxDatabase system)
        {
            XRayLtxDatabase stash = XRayLtxDatabase.LoadFile(sources.Index, STASH_CONFIG);
            var sums = new Dictionary<string, double>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> pair in stash.Sections["possible_items"].Values)
            {
                string section = pair.Key;

                if (system.HasSection(section) == false || Number(system, section, "cost", 0) <= 1 || WEAPON_KINDS.Contains(Get(system, section, "kind") ?? string.Empty) == false)
                {
                    continue;
                }

                List<string> ammo = system.GetList(section, "ammo_class");
                List<string> upgrades = system.GetList(section, "installed_upgrades");
                double hitPower = Number(system, section, "hit_power", 0);
                bool replacedAmmo = false;

                foreach (string upgrade in upgrades)
                {
                    string upgradeSection = Get(system, upgrade, "section");

                    if (string.IsNullOrEmpty(upgradeSection))
                    {
                        continue;
                    }

                    if (Get(system, upgradeSection, "hit_power") != null)
                    {
                        hitPower += Math.Max(hitPower, Number(system, upgradeSection, "hit_power", 0));
                    }

                    List<string> properties = system.GetList(upgrade, "property");

                    if (replacedAmmo == false && (properties.Contains("prop_calibre") || properties.Contains("prop_no_buck")))
                    {
                        ammo = system.GetList(upgradeSection, "ammo_class");
                        replacedAmmo = true;
                    }
                }

                int repetitions = int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight) ? Math.Max(0, weight) : 1;

                foreach (string cartridge in ammo)
                {
                    sums.TryGetValue(cartridge, out double sum);
                    counts.TryGetValue(cartridge, out int count);
                    sums[cartridge] = sum + hitPower * repetitions;
                    counts[cartridge] = count + repetitions;
                }
            }

            var averages = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, double> pair in sums)
            {
                if (counts[pair.Key] > 0)
                {
                    averages[pair.Key] = pair.Value / counts[pair.Key];
                }
            }

            return averages;
        }

        private static Dictionary<string, double> ReadMonsterMultipliers(StalkerXRaySources sources)
        {
            Match table = MULTIPLIER_TABLE.Match(sources.ReadScript(BALLISTICS_SCRIPT) ?? string.Empty);

            if (table.Success == false)
            {
                throw new InvalidOperationException(LOG + "GRA monster_ammo_damage_mult table is missing.");
            }

            var result = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (Match match in MULTIPLIER_ENTRY.Matches(table.Groups[1].Value))
            {
                result[match.Groups[1].Value] = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            }

            return result;
        }

        private static List<string> CollectSections(XRayLtxDatabase system)
        {
            var sections = new List<string>();

            foreach (string section in SECTIONS)
            {
                if (system.HasSection(section) && (Get(system, section, "class") ?? string.Empty).StartsWith("AMMO", StringComparison.Ordinal) && string.IsNullOrEmpty(Get(system, section, "visual")) == false)
                {
                    sections.Add(section);
                }
            }

            if (sections.Count != EXPECTED_COUNT)
            {
                throw new InvalidOperationException(LOG + $"Expected {EXPECTED_COUNT} GRA ammo sections, got {sections.Count}.");
            }

            return sections;
        }

        private static void RegisterDefinitions(List<ItemDefinition> definitions)
        {
            var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(InventoryDemoContentBuilder.DATABASE_PATH);

            if (database == null)
            {
                throw new InvalidOperationException(LOG + "Item database is missing.");
            }

            var all = new List<ItemDefinition>(database.Definitions);
            all.RemoveAll(definition => definition != null && definition.IsCategory(CATEGORY) && (definition.name.EndsWith("_bad", StringComparison.Ordinal) || definition.name.EndsWith("_verybad", StringComparison.Ordinal)));

            foreach (ItemDefinition definition in definitions)
            {
                if (all.Contains(definition) == false)
                {
                    all.Add(definition);
                }
            }

            database.SetDefinitions(all);
            EditorUtility.SetDirty(database);
        }

        private static StalkerXRaySources OpenSources()
        {
            StalkerXRaySources sources = StalkerXRaySources.Open(INDEX_SCOPE, out string error);
            return sources ?? throw new InvalidOperationException(LOG + error);
        }

        private static Scene CheckScene()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != InventoryIntegrationSetup.SCENE_PATH || scene.isDirty)
            {
                throw new InvalidOperationException(LOG + "Open the saved OutdoorsScene in Edit Mode before setup.");
            }

            return scene;
        }

        private static string ModelPath(XRayLtxDatabase system, string section)
        {
            string path = "meshes/" + Get(system, section, "visual").Replace('\\', '/').ToLowerInvariant();
            return path.EndsWith(".ogf", StringComparison.Ordinal) ? path : path + ".ogf";
        }

        private static string Get(XRayLtxDatabase system, string section, string key) => XRayLtxDatabase.Unquote(system.GetValue(section, key))?.Trim();

        private static double Number(XRayLtxDatabase system, string section, string key, double fallback)
        {
            string value = Get(system, section, key)?.Split(',')[0];
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : fallback;
        }

        private static int Normalize(double value) => (int)Math.Floor(Math.Max(0.0, value / 1.5) * 100.0 + 0.5);

        private static int ThresholdIndex(int value, int[] thresholds)
        {
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (value < thresholds[i])
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
