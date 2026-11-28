using RaidIntoDarkness.Inventory.Infrastructure;
using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Player.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;

namespace RaidIntoDarkness.Inventory.Editor
{
    public static class GraWeaponAddonSetup
    {
        public const string ADDONS_FOLDER = "Assets/_Source/Resources/Items/Addons";
        public const string SCOPES_FOLDER = ADDONS_FOLDER + "/Scopes";
        public const string SILENCERS_FOLDER = ADDONS_FOLDER + "/Silencers";
        public const string ATTACHMENT_CATEGORY = "Attachment";
        public const string SILENCER_TAG = "Silencer";
        public const string SCOPE_TAG = "Scope";
        public const string GRENADE_LAUNCHER_TAG = "GrenadeLauncher";
        public const string SCENE_GROUP = "Addons";
        public const string ICONS_FOLDER = "Assets/_Source/UI/Item Icons";

        private const string MENU = "Tools/Inventory/Setup GRA Weapon Addons";
        private const string LOG = "[WeaponAddons] ";
        private const string DEFAULT_ICONS_TEXTURE = "ui\\ui_icon_equipment";
        private const float ICON_CELL = 50f;
        private const string SILENCER_CLASS = "WP_SILEN";
        internal const string AUG_SILENCER_SECTION = "wpn_sil_nato";
        private const string SCOPE_CLASS = "WP_SCOPE";
        private const string GRENADE_LAUNCHER_CLASS = "WP_GLAUN";
        private const int MAX_SETTLE_PASSES = 4;

        private static readonly string[] INDEX_SCOPE = { "configs", "sounds", "textures/ui", "meshes/dynamics/weapons/wpn_upgrade", "meshes/dynamics/weapons/wpn_addon_world_models", "meshes/dynamics/weapons/wpn_vssk", "meshes/attachments" };
        private static readonly string[] ADDON_LIST_KEYS = { "scopes", "silencer_name", "grenade_launcher_name" };
        private static readonly string[] EXCLUDED =
        {
            "placeholder", "1p87", "ac11090", "aim_acro", "bravo4", "c-more", "compm4s", "ekp8_18", "eot", "hamr", "march_f_shorty_alt",
            "mepro", "monstrum", "pk6", "po434", "point_aimpro", "romeo8", "spec_alt", "triji", "trihawk", "uh2",
            "wpn_addon_scope", "wpn_addon_scope_g43", "wpn_addon_scope_galil", "wpn_addon_scope_po4x34", "wpn_addon_scope_pu", "wpn_addon_scope_susat", "wpn_addon_scope_zf4",
            "wpn_addon_grenade_launcher", "wpn_addon_grenade_launcher_l85", "wpn_addon_grenade_launcher_m203", "wpn_addon_grenade_launcher_monolit", "wpn_addon_grenade_launcher_scar",
            "bossxe", "c79", "devo", "e0t2_magd", "e0t2_magd_flipped", "exps3_mag", "exps3_mag_flipped", "gee36", "gee36_dove", "side", "uh1_magd", "uh1_magd_flipped"
        };
        private static readonly Regex ITEM_LISTS = new Regex("trade|death|treasure|loot|craft|stash|drop", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LIST_KEY = new Regex("^\\s*([\\w.\\-]+)\\s*=", RegexOptions.Compiled | RegexOptions.Multiline);

        [MenuItem(MENU)]
        public static void SetupFromMenu()
        {
            RunAsync().Forget(Debug.LogException);
        }

        public static void SetupBatch()
        {
            RunBatchAsync().Forget();
        }

        public static async UniTask<bool> RunAsync()
        {
            List<ItemDefinition> definitions = Setup();

            if (definitions == null)
            {
                return false;
            }

            int baked = await ItemIconBatchBake.BakeAllAsync(false, CancellationToken.None);
            Debug.Log($"{LOG}Rebaked {baked} generated icon variant(s); addons use GRA HD icons.");
            return true;
        }

        public static List<ItemDefinition> Setup()
        {
            Scene scene = InventorySceneSetup.RequireTargetScene();
            StalkerXRaySources sources = StalkerXRaySources.Open(INDEX_SCOPE, out string error);

            if (sources == null)
            {
                Debug.LogError(LOG + error);
                return null;
            }

            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(sources.Index);
            XRayStringTable strings = XRayStringTable.Load(sources.Index, "rus");
            var log = new StringBuilder(sources.Describe()).AppendLine();
            SortedDictionary<string, int> used = CollectUsedAddons(sources, system, log);
            var models = new Dictionary<string, string>(StringComparer.Ordinal);
            var importedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var atlasSections = new List<string>();
            int failures = 0;
            foreach (string section in used.Keys)
            {
                WeaponAddonKind kind = GetAddonKind(Get(system, section, "class"));
                if (kind == WeaponAddonKind.GrenadeLauncher)
                {
                    atlasSections.Add(section);
                }

                string visual = ResolveVisual(sources, system, section);

                if (visual == null)
                {
                    continue;
                }

                string folder = AddonFolder(kind, section);
                string name = Path.GetFileName(visual);
                string assetPath = folder + "/" + name;
                if (!importedModels.Contains(assetPath) && sources.CopyToProject(visual, folder, name) == null)
                {
                    log.AppendLine($"ERROR: [{section}] model '{visual}' is missing in GRA.");
                    failures++;
                    continue;
                }

                importedModels.Add(assetPath);
                models[section] = assetPath;
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            failures += SettleMaterials(new List<string>(importedModels), log);
            Dictionary<string, Sprite> icons = ImportIcons(sources, system, atlasSections, log);
            var definitions = new List<ItemDefinition>();

            foreach (KeyValuePair<string, int> pair in used)
            {
                GameObject model = models.TryGetValue(pair.Key, out string path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;

                if (model == null)
                {
                    log.AppendLine($"ERROR: [{pair.Key}] has no imported model.");
                    failures++;
                    continue;
                }

                WeaponAddonKind kind = GetAddonKind(Get(system, pair.Key, "class"));
                if (icons.TryGetValue(pair.Key, out Sprite icon) == false && kind == WeaponAddonKind.GrenadeLauncher)
                {
                    log.AppendLine($"ERROR: [{pair.Key}] has no GRA icon.");
                    failures++;
                }

                definitions.Add(CreateDefinition(system, strings, kind, pair.Key, model, icon));
            }

            AddToDatabase(definitions);
            List<WorldItem> prefabs = WorldItemPrefabBuilder.Build(definitions);
            GraInventorySoundSetup.SetupTakeSounds(sources, definitions, log);
            AssetDatabase.SaveAssets();
            failures += PlaceInScene(scene, prefabs, log) ? 0 : 1;
            Debug.Log($"{LOG}{definitions.Count} addons from {used.Count} referenced sections, {importedModels.Count} models, failures {failures}.\n{log}");
            return failures == 0 ? definitions : null;
        }

        public static Vector2Int ToProjectSize(int graWidth, int graHeight)
        {
            return new Vector2Int(Mathf.Max(1, (graWidth + 1) / 2), Mathf.Max(1, (graHeight + 1) / 2));
        }

        internal static bool IsExcludedAddon(string section) => Array.IndexOf(EXCLUDED, section) >= 0;

        internal static string ScopeFolder(string section) => SCOPES_FOLDER + "/" + section;

        internal static string ScopeDefinitionPath(string section) => AddonDefinitionPath(WeaponAddonKind.Scope, section);

        internal static bool IsAddonResourcePath(string path) => path.StartsWith(ADDONS_FOLDER + "/", StringComparison.Ordinal);

        internal static string AddonFolder(WeaponAddonKind kind, string section)
        {
            return kind switch
            {
                WeaponAddonKind.Scope => ScopeFolder(section),
                WeaponAddonKind.Silencer => SILENCERS_FOLDER + "/" + section,
                _ => ADDONS_FOLDER + "/GrenadeLaunchers/" + section
            };
        }

        internal static string AddonDefinitionPath(WeaponAddonKind kind, string section) => AddonFolder(kind, section) + "/" + section + "_definition.asset";

        private static async UniTaskVoid RunBatchAsync()
        {
            bool success = false;

            try
            {
                success = await RunAsync();
            }
            catch (Exception exception)
            {
                Debug.LogError(LOG + "Setup failed: " + exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        private static SortedDictionary<string, int> CollectUsedAddons(StalkerXRaySources sources, XRayLtxDatabase system, StringBuilder log)
        {
            HashSet<string> excluded = GetExcludedSections(system);
            var addons = new HashSet<string>(StringComparer.Ordinal);

            foreach (string section in system.Sections.Keys)
            {
                if (excluded.Contains(section) == false && IsAddonClass(Get(system, section, "class")) && Get(system, section, "visual") != null)
                {
                    addons.Add(section);
                }
            }

            var used = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var rejected = new SortedSet<string>(StringComparer.Ordinal);
            int weapons = 0;

            foreach (string section in system.Sections.Keys)
            {
                string cls = Get(system, section, "class") ?? string.Empty;

                if (cls.StartsWith("WP_", StringComparison.Ordinal) == false || IsAddonClass(cls) || section.EndsWith("_hud", StringComparison.Ordinal))
                {
                    continue;
                }

                weapons++;

                foreach (string key in ADDON_LIST_KEYS)
                {
                    foreach (string item in system.GetList(section, key))
                    {
                        AddReference(item, addons, excluded, used, rejected);
                    }
                }

                string group = Get(system, section, "modular_scope_group");

                if (string.IsNullOrEmpty(group) == false)
                {
                    foreach (string item in system.GetList(group, "scopes"))
                    {
                        AddReference(item, addons, excluded, used, rejected);
                    }
                }
            }

            int fromWeapons = used.Count;
            int listed = 0;

            foreach (XRayIndexedFile file in sources.Index.Files)
            {
                if (file.VirtualPath.StartsWith("configs/", StringComparison.Ordinal) == false || file.VirtualPath.EndsWith(".ltx", StringComparison.Ordinal) == false || ITEM_LISTS.IsMatch(file.VirtualPath) == false)
                {
                    continue;
                }

                listed++;

                foreach (Match match in LIST_KEY.Matches(sources.ReadScript(file.VirtualPath) ?? string.Empty))
                {
                    string name = match.Groups[1].Value.ToLowerInvariant();

                    if (addons.Contains(name))
                    {
                        used[name] = used.TryGetValue(name, out int count) ? count + 1 : 1;
                    }
                }
            }

            var unused = new List<string>();

            foreach (string addon in addons)
            {
                if (used.ContainsKey(addon) == false)
                {
                    unused.Add(addon);
                }
            }

            log.AppendLine($"Addon sections: {addons.Count} (excluded: {excluded.Count}), used: {used.Count} ({fromWeapons} referenced by {weapons} weapons, the rest from {listed} trade/loot lists), unused: {string.Join(", ", unused)}.");
            log.AppendLine($"Non-addon references in weapon lists: {string.Join(", ", rejected)}.");
            return used;
        }

        private static HashSet<string> GetExcludedSections(XRayLtxDatabase system)
        {
            var excluded = new HashSet<string>(EXCLUDED, StringComparer.Ordinal);
            foreach (string section in system.Sections.Keys)
            {
                if (section != AUG_SILENCER_SECTION && Get(system, section, "class") == SILENCER_CLASS)
                {
                    excluded.Add(section);
                }
            }

            return excluded;
        }

        private static void AddReference(string item, HashSet<string> addons, HashSet<string> excluded, SortedDictionary<string, int> used, SortedSet<string> rejected)
        {
            string name = item.Trim().ToLowerInvariant();

            if (name.Length == 0 || excluded.Contains(name))
            {
                return;
            }

            if (addons.Contains(name))
            {
                used[name] = used.TryGetValue(name, out int count) ? count + 1 : 1;
            }
            else
            {
                rejected.Add(name);
            }
        }

        private static bool IsAddonClass(string cls)
        {
            return cls == SILENCER_CLASS || cls == SCOPE_CLASS || cls == GRENADE_LAUNCHER_CLASS;
        }

        internal static int SettleMaterials(List<string> models, StringBuilder log)
        {
            XRayAssetSourceOptions options = XRayAssetSourceSettings.GetEffective();

            for (int pass = 0; pass < MAX_SETTLE_PASSES; pass++)
            {
                List<string> pending = models.FindAll(model => XRayAssetGenerationCoordinator.NeedsGeneration(model, options));

                if (pending.Count == 0)
                {
                    break;
                }

                foreach (string model in pending)
                {
                    XRayAssetGenerationCoordinator.Process(model, options, false, out XRayResourceReport _);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            List<string> remaining = models.FindAll(model => XRayAssetGenerationCoordinator.NeedsGeneration(model, options));

            foreach (string model in remaining)
            {
                log.AppendLine($"ERROR: '{model}' still has unassigned material slots.");
            }

            return remaining.Count;
        }

        private static WeaponAddonKind GetAddonKind(string cls) => cls == SILENCER_CLASS ? WeaponAddonKind.Silencer : cls == SCOPE_CLASS ? WeaponAddonKind.Scope : WeaponAddonKind.GrenadeLauncher;

        private static ItemDefinition CreateDefinition(XRayLtxDatabase system, XRayStringTable strings, WeaponAddonKind kind, string section, GameObject model, Sprite icon)
        {
            string path = AddonDefinitionPath(kind, section);
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool preserveAddon = kind != WeaponAddonKind.GrenadeLauncher && definition != null;

            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(definition, path);
                definition.name = section;
            }

            string name = Get(system, section, "inv_name") ?? section;
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_definitionId").stringValue = AssetDatabase.AssetPathToGUID(path);
            serialized.FindProperty("_displayName").stringValue = strings.Translate(name);
            serialized.FindProperty("_shortName").stringValue = strings.Translate(Get(system, section, "inv_name_short") ?? name);
            serialized.FindProperty("_description").stringValue = string.Empty;
            serialized.FindProperty("_category").stringValue = ATTACHMENT_CATEGORY;
            SerializedProperty tags = serialized.FindProperty("_tags");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).stringValue = kind == WeaponAddonKind.Silencer ? SILENCER_TAG : kind == WeaponAddonKind.Scope ? SCOPE_TAG : GRENADE_LAUNCHER_TAG;
            serialized.FindProperty("_size").vector2IntValue = ToProjectSize(GetInt(system, section, "inv_grid_width"), GetInt(system, section, "inv_grid_height"));
            serialized.FindProperty("_weight").floatValue = GetFloat(system, section, "inv_weight", 0f);
            serialized.FindProperty("_maxStack").intValue = 0;
            serialized.FindProperty("_allowRotation").boolValue = true;
            serialized.FindProperty("_visualPrefab").objectReferenceValue = model;
            if (!preserveAddon)
            {
                ItemIconDefaults.Apply(serialized, ItemIconDefaults.SIDE_VIEW_ANGLES);
                serialized.FindProperty("_iconOverride").objectReferenceValue = icon;
                serialized.FindProperty("_icon._generateIcon").boolValue = icon == null;
            }

            SerializedProperty components = serialized.FindProperty("_components");
            SerializedProperty addon = null;
            if (preserveAddon)
            {
                for (int i = 0; i < components.arraySize; i++)
                {
                    SerializedProperty component = components.GetArrayElementAtIndex(i);
                    if (component.managedReferenceValue is WeaponAddonComponent)
                    {
                        addon = component;
                        break;
                    }
                }
            }
            else
            {
                components.ClearArray();
            }

            if (addon == null)
            {
                int index = components.arraySize;
                components.arraySize++;
                addon = components.GetArrayElementAtIndex(index);
                addon.managedReferenceValue = new WeaponAddonComponent();
            }

            addon.FindPropertyRelative("_section").stringValue = section;
            addon.FindPropertyRelative("_kind").enumValueIndex = (int)kind;
            addon.FindPropertyRelative("_cost").intValue = GetInt(system, section, "cost");
            addon.FindPropertyRelative("_bulletHitPowerFactor").floatValue = GetFloat(system, section, "bullet_hit_power_k", 1f);
            addon.FindPropertyRelative("_bulletHitImpulseFactor").floatValue = GetFloat(system, section, "bullet_hit_impulse_k", 1f);
            addon.FindPropertyRelative("_bulletSpeedFactor").floatValue = GetFloat(system, section, "bullet_speed_k", 1f);
            addon.FindPropertyRelative("_fireDispersionBaseFactor").floatValue = GetFloat(system, section, "fire_dispersion_base_k", 1f);
            addon.FindPropertyRelative("_camDispersionFactor").floatValue = GetFloat(system, section, "cam_dispersion_k", 1f);
            addon.FindPropertyRelative("_camDispersionIncrementFactor").floatValue = GetFloat(system, section, "cam_dispersion_inc_k", 1f);
            addon.FindPropertyRelative("_scopeZoomFactor").floatValue = GetFloat(system, section, "scope_zoom_factor", 0f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static bool PlaceInScene(Scene scene, List<WorldItem> prefabs, StringBuilder log)
        {
            StalkerPlayerController player = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                player = player != null ? player : root.GetComponentInChildren<StalkerPlayerController>(true);
            }

            if (player == null)
            {
                log.AppendLine($"ERROR: Scene '{scene.path}' has no player to place addons next to.");
                return false;
            }

            WorldItemPrefabBuilder.AppendToScene(scene, prefabs, player.transform, SCENE_GROUP);
            bool saved = EditorSceneManager.SaveScene(scene);
            log.AppendLine($"Scene group '{WorldItemPrefabBuilder.WORLD_ITEMS_ROOT}/{SCENE_GROUP}': {prefabs.Count} addons, saved {saved}.");
            return saved;
        }

        private static void AddToDatabase(List<ItemDefinition> definitions)
        {
            var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(InventoryDemoContentBuilder.DATABASE_PATH);

            if (database == null)
            {
                Debug.LogError($"{LOG}Item database '{InventoryDemoContentBuilder.DATABASE_PATH}' is missing.");
                return;
            }

            var all = new List<ItemDefinition>(database.Definitions);

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

        private static string ResolveVisual(StalkerXRaySources sources, XRayLtxDatabase system, string section)
        {
            string hud = Get(system, section, "hud");
            string attached = ModelPath(string.IsNullOrEmpty(hud) || hud == "0" ? null : Get(system, hud, "item_visual"));
            return attached != null && sources.Exists(attached) ? attached : ModelPath(Get(system, section, "visual"));
        }

        internal static Dictionary<string, Sprite> ImportIcons(StalkerXRaySources sources, XRayLtxDatabase system, IEnumerable<string> sections, StringBuilder log, string iconsFolder = ICONS_FOLDER)
        {
            HashSet<string> excluded = GetExcludedSections(system);
            var byTexture = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (string section in sections)
            {
                if (excluded.Contains(section))
                {
                    continue;
                }

                string texture = GetIconTexture(system, section);

                if (byTexture.TryGetValue(texture, out List<string> list) == false)
                {
                    list = new List<string>();
                    byTexture[texture] = list;
                }

                list.Add(section);
            }

            var icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            StalkerXRaySources.EnsureFolder(iconsFolder);

            foreach (KeyValuePair<string, List<string>> pair in byTexture)
            {
                string assetPath = iconsFolder + "/" + pair.Key.Replace("ui/", string.Empty).Replace('/', '_') + ".png";
                byte[] data = sources.ReadBytes("textures/" + pair.Key + ".dds");

                if (data == null)
                {
                    log.AppendLine($"ERROR: icon atlas 'textures/{pair.Key}.dds' is missing for {string.Join(", ", pair.Value)}.");
                    continue;
                }

                DdsHeader header = DdsHeader.Parse(data);

                if (DdsDecoder.CanDecode(header) == false)
                {
                    log.AppendLine($"ERROR: icon atlas '{pair.Key}' has unsupported format {header.Description}.");
                    continue;
                }

                var rects = new List<KeyValuePair<string, Rect>>();

                foreach (string section in pair.Value)
                {
                    rects.Add(new KeyValuePair<string, Rect>(section, GetIconRect(system, section, header.Height)));
                }

                byte[] pixels = DdsDecoder.DecodeTopLevel(data, header);
                ClearExcludedIconPixels(system, excluded, pair.Key, header, rects, pixels);
                File.WriteAllBytes(Path.GetFullPath(assetPath), EncodePng(pixels, header.Width, header.Height));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureSprites(assetPath, Math.Max(header.Width, header.Height), rects);

                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    if (asset is Sprite sprite)
                    {
                        icons[sprite.name] = sprite;
                    }
                }

                log.AppendLine($"Icons: {assetPath} ({header.Width}x{header.Height}), {pair.Value.Count} sprites.");
            }

            return icons;
        }

        private static string GetIconTexture(XRayLtxDatabase system, string section)
        {
            return Regex.Replace(Get(system, section, "icons_texture") ?? DEFAULT_ICONS_TEXTURE, "\\s+", string.Empty).Replace('\\', '/').ToLowerInvariant();
        }

        private static Rect GetIconRect(XRayLtxDatabase system, string section, int textureHeight)
        {
            int width = GetInt(system, section, "inv_grid_width");
            int height = GetInt(system, section, "inv_grid_height");
            float x = GetInt(system, section, "inv_grid_x") * ICON_CELL;
            float y = textureHeight - (GetInt(system, section, "inv_grid_y") + height) * ICON_CELL;
            return new Rect(x, y, width * ICON_CELL, height * ICON_CELL);
        }

        private static void ClearExcludedIconPixels(XRayLtxDatabase system, HashSet<string> excluded, string texture, DdsHeader header, List<KeyValuePair<string, Rect>> rects, byte[] pixels)
        {
            foreach (string section in excluded)
            {
                if (IsAddonClass(Get(system, section, "class")) == false || GetIconTexture(system, section) != texture)
                {
                    continue;
                }

                Rect rect = GetIconRect(system, section, header.Height);
                List<KeyValuePair<string, Rect>> overlaps = rects.FindAll(entry => entry.Value.Overlaps(rect));
                int left = Mathf.Max(0, (int)rect.xMin);
                int right = Mathf.Min(header.Width, (int)rect.xMax);
                int bottom = Mathf.Max(0, (int)rect.yMin);
                int top = Mathf.Min(header.Height, (int)rect.yMax);

                for (int y = bottom; y < top; y++)
                {
                    int row = (header.Height - 1 - y) * header.Width;
                    if (overlaps.Count == 0 && left < right)
                    {
                        Array.Clear(pixels, (row + left) * 4, (right - left) * 4);
                        continue;
                    }

                    for (int x = left; x < right; x++)
                    {
                        bool shared = false;
                        foreach (KeyValuePair<string, Rect> entry in overlaps)
                        {
                            if (entry.Value.Contains(new Vector2(x, y)))
                            {
                                shared = true;
                                break;
                            }
                        }

                        if (shared == false)
                        {
                            Array.Clear(pixels, (row + x) * 4, 4);
                        }
                    }
                }
            }
        }

        internal static void ConfigureSprites(string assetPath, int maxSize, List<KeyValuePair<string, Rect>> rects)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = Mathf.Min(8192, Mathf.NextPowerOfTwo(maxSize));
            importer.SaveAndReimport();
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var existing = new Dictionary<string, SpriteRect>(StringComparer.Ordinal);

            foreach (SpriteRect rect in provider.GetSpriteRects())
            {
                existing[rect.name] = rect;
            }

            var sprites = new List<SpriteRect>();
            var names = new List<SpriteNameFileIdPair>();

            foreach (KeyValuePair<string, Rect> pair in rects)
            {
                SpriteRect sprite = existing.TryGetValue(pair.Key, out SpriteRect found) ? found : new SpriteRect { name = pair.Key, spriteID = GUID.Generate() };
                sprite.rect = pair.Value;
                sprite.alignment = SpriteAlignment.Center;
                sprite.pivot = new Vector2(0.5f, 0.5f);
                sprites.Add(sprite);
                names.Add(new SpriteNameFileIdPair(sprite.name, sprite.spriteID));
            }

            provider.SetSpriteRects(sprites.ToArray());
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(names);
            provider.Apply();
            importer.SaveAndReimport();
        }

        internal static byte[] EncodePng(byte[] topDownRgba, int width, int height)
        {
            var bottomUp = new byte[topDownRgba.Length];
            int stride = width * 4;

            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(topDownRgba, y * stride, bottomUp, (height - 1 - y) * stride, stride);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            try
            {
                texture.SetPixelData(bottomUp, 0);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static string ModelPath(string visual)
        {
            if (string.IsNullOrEmpty(visual))
            {
                return null;
            }

            string path = "meshes/" + visual.Trim().Replace('\\', '/').ToLowerInvariant();
            return path.EndsWith(".ogf", StringComparison.Ordinal) ? path : path + ".ogf";
        }

        private static string Get(XRayLtxDatabase system, string section, string key)
        {
            string value = XRayLtxDatabase.Unquote(system.GetValue(section, key));
            return value != null ? value.Trim() : null;
        }

        private static int GetInt(XRayLtxDatabase system, string section, string key)
        {
            return Mathf.RoundToInt(GetFloat(system, section, key, 0f));
        }

        private static float GetFloat(XRayLtxDatabase system, string section, string key, float fallback)
        {
            string value = Get(system, section, key);
            return value != null && StalkerXRaySources.TryFloat(value.Split(',')[0], out float result) ? result : fallback;
        }
    }
}
