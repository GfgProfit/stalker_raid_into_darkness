using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Inventory;
using Inventory.UI;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Player.Editor;
using UnityEditor;
using UnityEngine;
using UnityOpenMcpBridge;
using XRay.Importer.Core.Ogf;
using XRay.Importer.Sources.Ltx;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    [BridgeToolType]
    public static class GraInventorySoundSetup
    {
        public const string SOUND_SET_PATH = "Assets/_Source/UI/Inventory/GRA/GraInventorySounds.asset";
        public const string THEME_PATH = "Assets/_Source/UI/Inventory/GRA/GraInventoryTheme.asset";
        public const string INTERFACE_AUDIO = "Assets/_Source/Audio/Interface";
        public const string ITEM_AUDIO = "Assets/_Source/Audio/Items";
        public const string ITEM_SOUNDS_LTX = "configs/items/settings/item_sounds/base_item_sound.ltx";
        public const string COLLIDE_AUDIO = "Assets/_Source/Audio/Collide";
        public const string COLLIDE_LIBRARY_PATH = "Assets/_Source/Settings/Player/Audio/StalkerCollideSoundLibrary.asset";
        public const string DEFINITIONS_FOLDER = "Assets/_Source/Items/Definitions";
        public const string UI_SCRIPT = "scripts/ui_inventory.script";

        private const string MENU = "Tools/Inventory/Setup GRA Inventory Sounds";
        private const string LOG = "[InventorySounds] ";
        private const float AUDIO_MIN_DISTANCE = 1f;

        private static readonly string[] INDEX_SCOPE = { "configs", "scripts", "sounds", "materials", StalkerGameMtlLibrary.GAMEMTL };
        private static readonly string[] UI_SOUNDS = { "open", "close", "item_to_slot", "item_to_ruck", "properties", "drop_item", "item_use" };
        private static readonly Regex SOUND_OBJECT = new Regex("local\\s+snd_(\\w+)\\s*=\\s*sound_object\\(\\s*\\[\\[([^\\]]+)\\]\\]\\s*\\)", RegexOptions.Compiled);
        private static readonly Regex ANTI_SPAM = new Regex("tg\\s*-\\s*self\\.tg_play\\s*<\\s*(\\d+)", RegexOptions.Compiled);

        [MenuItem(MENU)]
        public static void SetupFromMenu()
        {
            Setup();
        }

        public static void SetupBatch()
        {
            bool success = false;
            try
            {
                success = Setup();
            }
            catch (Exception exception)
            {
                Debug.LogError(LOG + "Setup failed: " + exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        public static bool Setup()
        {
            StalkerXRaySources sources = StalkerXRaySources.Open(INDEX_SCOPE, out string error);
            if (sources == null)
            {
                Debug.LogError(LOG + error);
                return false;
            }

            var log = new StringBuilder(sources.Describe()).AppendLine();
            bool ui = SetupInterfaceSounds(sources, log);
            int taken = SetupTakeSounds(sources, log);
            StalkerCollideSoundLibrary collide = SetupCollideSounds(sources, log);
            int prefabs = collide != null ? ConfigurePrefabs(collide, log) : 0;
            AssetDatabase.SaveAssets();
            Debug.Log(LOG + log);
            return ui && taken > 0 && collide != null && prefabs > 0;
        }

        public static void ConfigureImpact(GameObject root, ItemDefinition definition, StalkerCollideSoundLibrary library, StalkerSurfaceAudioLibrary surfaces)
        {
            if (root == null || definition == null || library == null || root.GetComponent<WorldItem>() == null)
            {
                return;
            }

            ReadPhysics(definition, out string material, out float mass);
            AudioSource audio = root.GetComponent<AudioSource>();
            if (audio == null)
            {
                audio = root.AddComponent<AudioSource>();
            }

            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Logarithmic;
            audio.minDistance = AUDIO_MIN_DISTANCE;
            audio.maxDistance = library.MaxDistance;
            WorldItemImpactSound impact = root.GetComponent<WorldItemImpactSound>();
            if (impact == null)
            {
                impact = root.AddComponent<WorldItemImpactSound>();
            }

            impact.Configure(library, surfaces, audio, material, mass);
        }

        private static bool SetupInterfaceSounds(StalkerXRaySources sources, StringBuilder log)
        {
            string script = sources.ReadScript(UI_SCRIPT);
            if (script == null)
            {
                log.AppendLine("ERROR: " + UI_SCRIPT + " is missing.");
                return false;
            }

            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in SOUND_OBJECT.Matches(script))
            {
                paths[match.Groups[1].Value] = match.Groups[2].Value.Trim();
            }

            Match spam = ANTI_SPAM.Match(script);
            float interval = spam.Success ? int.Parse(spam.Groups[1].Value, CultureInfo.InvariantCulture) / 1000f : 0.5f;
            log.AppendLine($"{UI_SCRIPT} ({sources.GetSourceName(UI_SCRIPT)}): PlaySND anti spam {interval:0.###} s");
            var clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
            var imported = new List<KeyValuePair<string, string>>();
            foreach (string key in UI_SOUNDS)
            {
                if (!paths.TryGetValue(key, out string sound))
                {
                    log.AppendLine($"ERROR: snd_{key} is not declared in {UI_SCRIPT}.");
                    return false;
                }

                string virtualPath = SoundPath(sound);
                string assetPath = sources.CopyToProject(virtualPath, INTERFACE_AUDIO, Path.GetFileName(virtualPath));
                if (assetPath == null)
                {
                    log.AppendLine($"ERROR: {virtualPath} is missing in the active sources.");
                    return false;
                }

                imported.Add(new KeyValuePair<string, string>(key, assetPath));
                log.AppendLine($"snd_{key} = {sound} <- {sources.GetSourceName(virtualPath)}");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (KeyValuePair<string, string> item in imported)
            {
                clips[item.Key] = ImportClip(item.Value);
            }

            var set = AssetDatabase.LoadAssetAtPath<InventorySoundSet>(SOUND_SET_PATH);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<InventorySoundSet>();
                AssetDatabase.CreateAsset(set, SOUND_SET_PATH);
            }

            set.SetClips(clips["open"], clips["close"], clips["item_to_slot"], clips["item_to_ruck"], clips["properties"], clips["drop_item"], clips["item_use"], interval);
            EditorUtility.SetDirty(set);
            var theme = AssetDatabase.LoadAssetAtPath<InventoryTheme>(THEME_PATH);
            if (theme == null)
            {
                log.AppendLine("ERROR: theme " + THEME_PATH + " is missing.");
                return false;
            }

            var serialized = new SerializedObject(theme);
            serialized.FindProperty("_sounds").objectReferenceValue = set;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static int SetupTakeSounds(StalkerXRaySources sources, StringBuilder log)
        {
            return SetupTakeSounds(sources, LoadDefinitions(), log);
        }

        public static int SetupTakeSounds(StalkerXRaySources sources, IEnumerable<ItemDefinition> definitions, StringBuilder log)
        {
            XRayLtxDatabase table = XRayLtxDatabase.LoadFile(sources.Index, ITEM_SOUNDS_LTX);
            var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (XRayLtxSection section in table.Sections.Values)
            {
                foreach (KeyValuePair<string, string> pair in section.Values)
                {
                    categories[pair.Key] = pair.Value;
                }
            }

            log.AppendLine($"{ITEM_SOUNDS_LTX}: {categories.Count} snd_on_take categories");
            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(sources.Index);
            var clipsByCategory = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var assigned = new List<KeyValuePair<ItemDefinition, string>>();
            foreach (ItemDefinition definition in definitions)
            {
                EquipmentStatsComponent stats = definition.GetComponent<EquipmentStatsComponent>();
                string section = stats != null && stats.Section.Length > 0 ? stats.Section : definition.name;
                string category = system.GetValue(section, "snd_on_take");
                category = category != null ? category.Trim() : null;
                if (string.IsNullOrEmpty(category) || !categories.TryGetValue(category, out string value))
                {
                    log.AppendLine($"{section}: no snd_on_take sound ({category ?? "key missing"})");
                    continue;
                }

                if (!clipsByCategory.ContainsKey(category))
                {
                    clipsByCategory[category] = CopyTakeSounds(sources, category, value, log);
                }

                assigned.Add(new KeyValuePair<ItemDefinition, string>(definition, category));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var clips = new Dictionary<string, List<AudioClip>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> pair in clipsByCategory)
            {
                var list = new List<AudioClip>();
                foreach (string assetPath in pair.Value)
                {
                    AudioClip clip = ImportClip(assetPath);
                    if (clip != null)
                    {
                        list.Add(clip);
                    }
                }

                clips[pair.Key] = list;
            }

            foreach (KeyValuePair<ItemDefinition, string> pair in assigned)
            {
                SetTakeSound(pair.Key, pair.Value, clips[pair.Value]);
            }

            log.AppendLine($"Item definitions with snd_on_take sounds: {assigned.Count}");
            return assigned.Count;
        }

        private static List<string> CopyTakeSounds(StalkerXRaySources sources, string category, string value, StringBuilder log)
        {
            string[] parts = value.Split(',');
            string sound = parts[0].Trim();
            var names = new List<string>();
            if (parts.Length >= 3 && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) && int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max))
            {
                for (int i = min; i <= max; i++)
                {
                    names.Add(sound + i.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            {
                names.Add(sound);
            }

            var result = new List<string>();
            foreach (string name in names)
            {
                string virtualPath = SoundPath(name);
                string assetPath = sources.CopyToProject(virtualPath, ITEM_AUDIO, Path.GetFileName(virtualPath));
                if (assetPath == null)
                {
                    log.AppendLine($"WARNING: {virtualPath} ({category}) is missing.");
                    continue;
                }

                result.Add(assetPath);
            }

            log.AppendLine($"snd_on_take {category} = {value}: {result.Count} files");
            return result;
        }

        private static void SetTakeSound(ItemDefinition definition, string category, List<AudioClip> clips)
        {
            var serialized = new SerializedObject(definition);
            SerializedProperty list = serialized.FindProperty("_components");
            int index = -1;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).managedReferenceValue is ItemTakeSoundComponent)
                {
                    index = i;
                }
            }

            if (index < 0)
            {
                index = list.arraySize;
                list.arraySize++;
                list.GetArrayElementAtIndex(index).managedReferenceValue = new ItemTakeSoundComponent();
            }

            SerializedProperty component = list.GetArrayElementAtIndex(index);
            component.FindPropertyRelative("_category").stringValue = category;
            SerializedProperty clipList = component.FindPropertyRelative("_clips");
            clipList.arraySize = clips.Count;
            for (int i = 0; i < clips.Count; i++)
            {
                clipList.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        [ProjectCommand("project.inventory.prepare_vegetation_contacts", Title = "Prepare Vegetation Contact Sounds", Package = "RaidIntoDarkness.Integrations.Inventory.Editor", Description = "Import missing original object contact sound pairs for bush_sux into the existing collide library without changing scenes, prefabs or item definitions.", IsMutating = true, IdempotentHint = true, Lifecycle = LifecyclePolicy.EditorSettle)]
        public static string PrepareVegetationContacts()
        {
            if (!SetupMissingSurfaceCollideSounds(StalkerSurfaceType.BushSux, out string report))
            {
                throw new InvalidOperationException(report);
            }

            return JsonUtility.ToJson(new VegetationContactResult { Imported = true, Report = report });
        }

        [Serializable]
        private sealed class VegetationContactResult
        {
            public bool Imported;
            public string Report;
        }

        public static bool SetupMissingSurfaceCollideSounds(StalkerSurfaceType surface, out string report)
        {
            var library = AssetDatabase.LoadAssetAtPath<StalkerCollideSoundLibrary>(COLLIDE_LIBRARY_PATH);
            if (library == null)
            {
                report = "Missing collide sound library: " + COLLIDE_LIBRARY_PATH;
                return false;
            }

            StalkerXRaySources sources = StalkerXRaySources.Open(new[] { "materials", "sounds/material", StalkerGameMtlLibrary.GAMEMTL }, out string error);
            if (sources == null)
            {
                report = error;
                return false;
            }

            StalkerGameMtlLibrary materials = StalkerGameMtlLibrary.Load(sources);
            if (materials == null)
            {
                report = "gamemtl.xr not found in the active MO2 profile.";
                return false;
            }

            var log = new StringBuilder(sources.Describe()).AppendLine();
            var entries = new List<StalkerCollideSounds>(library.Entries);
            var objectMaterials = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StalkerCollideSounds entry in entries)
            {
                objectMaterials.Add(entry.ObjectMaterial);
            }

            var pending = new List<KeyValuePair<List<AudioClip>, string>>();
            int added = 0;
            foreach (string material in objectMaterials)
            {
                if (library.Find(material, surface) != null)
                {
                    continue;
                }

                string surfaceName = StalkerSurfaceTypes.GetMaterialName(surface);
                StalkerGameMtlPair pair = materials.FindPair(material, surfaceName);
                var clips = new List<AudioClip>();
                foreach (string sound in StalkerGameMtlLibrary.SplitSounds(pair?.CollideSounds))
                {
                    string virtualPath = SoundPath(sound);
                    string assetPath = sources.CopyToProject(virtualPath, COLLIDE_AUDIO, virtualPath.Substring("sounds/".Length));
                    if (assetPath != null)
                    {
                        pending.Add(new KeyValuePair<List<AudioClip>, string>(clips, assetPath));
                    }

                    log.AppendLine($"{material} @ {surfaceName}: '{sound}' <- {sources.GetSourceName(virtualPath) ?? "MISSING"}; pair <- {pair?.Origin ?? "no pair"}");
                }

                entries.Add(new StalkerCollideSounds(material, surface, pair?.Origin ?? "no pair", clips));
                added++;
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (KeyValuePair<List<AudioClip>, string> item in pending)
            {
                AudioClip clip = ImportClip(item.Value);
                if (clip != null)
                {
                    item.Key.Add(clip);
                }
            }

            if (added > 0)
            {
                library.SetImportData(entries, library.MinVolume, library.MaxVolume, library.SourceReport + Environment.NewLine + log);
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
            }

            log.AppendLine($"Added {added} missing object material pairs for {surface}.");
            report = log.ToString();
            return library.Find("objects\\clothes", surface)?.Count > 0;
        }

        internal static StalkerCollideSoundLibrary SetupCollideSounds(StalkerXRaySources sources, StringBuilder log)
        {
            StalkerGameMtlLibrary materials = StalkerGameMtlLibrary.Load(sources);
            foreach (string line in materials.Log)
            {
                log.AppendLine(line);
            }

            var objectMaterials = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ItemDefinition definition in LoadDefinitions())
            {
                if (definition.TryGetComponent(out WorldItemComponent world) && world.Prefab != null)
                {
                    ReadPhysics(definition, out string material, out _);
                    objectMaterials.Add(material);
                }
            }

            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(sources.Index);
            StalkerXRaySources.TryFloat(system.GetValue("sound", "snd_collide_min_volume"), out float minVolume);
            StalkerXRaySources.TryFloat(system.GetValue("sound", "snd_collide_max_volume"), out float maxVolume);
            log.AppendLine($"[sound] snd_collide_min_volume = {minVolume}, snd_collide_max_volume = {maxVolume}");
            var pending = new List<KeyValuePair<List<AudioClip>, string>>();
            var entries = new List<StalkerCollideSounds>();
            foreach (string material in objectMaterials)
            {
                for (int i = 0; i < StalkerSurfaceTypes.Count; i++)
                {
                    var surface = (StalkerSurfaceType)i;
                    StalkerGameMtlPair pair = materials.FindPair(material, StalkerSurfaceTypes.GetMaterialName(surface));
                    List<string> sounds = pair != null ? StalkerGameMtlLibrary.SplitSounds(pair.CollideSounds) : new List<string>();
                    var clips = new List<AudioClip>();
                    foreach (string sound in sounds)
                    {
                        string virtualPath = SoundPath(sound);
                        string assetPath = sources.CopyToProject(virtualPath, COLLIDE_AUDIO, virtualPath.Substring("sounds/".Length));
                        if (assetPath == null)
                        {
                            log.AppendLine($"WARNING: {virtualPath} ({material}@{StalkerSurfaceTypes.GetMaterialName(surface)}) is missing.");
                            continue;
                        }

                        pending.Add(new KeyValuePair<List<AudioClip>, string>(clips, assetPath));
                    }

                    string origin = pair != null ? pair.Origin : "no pair";
                    entries.Add(new StalkerCollideSounds(material, surface, origin, clips));
                    log.AppendLine($"{material} @ {StalkerSurfaceTypes.GetMaterialName(surface)}: {sounds.Count} sounds ({origin})");
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (KeyValuePair<List<AudioClip>, string> item in pending)
            {
                AudioClip clip = ImportClip(item.Value);
                if (clip != null)
                {
                    item.Key.Add(clip);
                }
            }

            var library = AssetDatabase.LoadAssetAtPath<StalkerCollideSoundLibrary>(COLLIDE_LIBRARY_PATH);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<StalkerCollideSoundLibrary>();
                AssetDatabase.CreateAsset(library, COLLIDE_LIBRARY_PATH);
            }

            library.SetImportData(entries, minVolume, maxVolume, log.ToString());
            EditorUtility.SetDirty(library);
            return library;
        }

        private static int ConfigurePrefabs(StalkerCollideSoundLibrary library, StringBuilder log)
        {
            var surfaces = AssetDatabase.LoadAssetAtPath<StalkerSurfaceAudioLibrary>(StalkerSurfaceAudioBuilder.LIBRARY_PATH);
            int count = 0;
            foreach (ItemDefinition definition in LoadDefinitions())
            {
                if (!definition.TryGetComponent(out WorldItemComponent world) || world.Prefab == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(world.Prefab);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigureImpact(root, definition, library, surfaces);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    WorldItemImpactSound impact = root.GetComponent<WorldItemImpactSound>();
                    log.AppendLine($"{definition.name}: {impact.Material}, shell mass {impact.Mass:0.###} kg");
                    count++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            log.AppendLine($"World item prefabs with impact sounds: {count}");
            return count;
        }

        private static void ReadPhysics(ItemDefinition definition, out string material, out float mass)
        {
            material = WorldItemImpactSound.DEFAULT_MATERIAL;
            mass = 0f;
            GameObject source = definition.VisualPrefab != null ? PrefabUtility.GetCorrespondingObjectFromOriginalSource(definition.VisualPrefab) : null;
            source = source != null ? source : definition.VisualPrefab;
            string path = source != null ? AssetDatabase.GetAssetPath(source) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".ogf", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                return;
            }

            OgfModel model = OgfReader.ReadFile(File.ReadAllBytes(path), Path.GetFileName(path));
            bool found = false;
            foreach (OgfBone bone in model.Bones)
            {
                if (!found && !string.IsNullOrEmpty(bone.GameMaterial))
                {
                    material = bone.GameMaterial;
                    found = true;
                }

                if (bone.ShapeType != 0)
                {
                    mass += (float)bone.Mass;
                }
            }
        }

        private static List<ItemDefinition> LoadDefinitions()
        {
            var result = new List<ItemDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { DEFINITIONS_FOLDER }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null)
                {
                    result.Add(definition);
                }
            }

            return result;
        }

        private static AudioClip ImportClip(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is AudioImporter importer && importer.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        }

        private static string SoundPath(string sound)
        {
            return "sounds/" + sound.Replace('\\', '/').Trim().ToLowerInvariant() + ".ogg";
        }
    }
}
