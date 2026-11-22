using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Inventory;
using Inventory.Editor;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Player.Editor;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class IwpWeaponSetup
    {
        public const string IWP_ROOT = "E:/STALKER CoC IWP Unpacked pacth 16.03.2026";
        public const string SECTION = "wpn_aug_a3";
        public const string WEAPONS_FOLDER = "Assets/_Source/Resources/Items/Weapons";
        public const string DEFINITION_PATH = WEAPON_FOLDER + "/" + SECTION + "_definition.asset";
        public const string HANDS_MOTIONS_FOLDER = WEAPON_FOLDER + "/Animations/Hands";
        public const string HANDS_MOTION_PREFIX = "aug_";

        internal const string WEAPON_FOLDER = WEAPONS_FOLDER + "/" + SECTION;
        internal const string WORLD_VISUAL_PATH = WEAPON_FOLDER + "/" + SECTION + "_world_visual.prefab";
        internal const string HUD_VISUAL_PATH = WEAPON_FOLDER + "/" + SECTION + "_hud_visual.prefab";
        internal const string WEAPON_MOTIONS_FOLDER = WEAPON_FOLDER + "/Animations/Weapon";
        internal const string AUDIO_FOLDER = WEAPON_FOLDER + "/Audio";
        internal const string SHARED_AUDIO_FOLDER = WEAPONS_FOLDER + "/_Shared Audio";

        private const string WORLD_MODEL = "meshes/dynamics/weapons/wpn_aug/wpn_aug_a3.ogf";
        internal const string HUD_MODEL = "meshes/dynamics/weapons/wpn_aug/wpn_aug_a3_hud.ogf";
        internal const string WEAPON_MOTIONS = "meshes/dynamics/weapons/wpn_aug/wpn_aug_a3_hud_animation.omf";
        internal const string HANDS_MOTIONS = "meshes/dynamics/weapons/wpn_fn2000/wpn_hand_fn2000_hud_animation.omf";
        private const string MENU = "Tools/Inventory/Setup Test Weapon (AUG A3)";
        private const string LOG = "[IwpWeapon] ";
        private const string HANDS_RIG_PATH = "Assets/_Source/FirstPerson/Hands/Models/wpn_hand_01.ogf";
        private const string OUTFIT_CATALOG_PATH = "Assets/_Source/FirstPerson/Outfits/OutfitCatalog.asset";
        private const string RIFLE_TAG = InventoryDemoContentBuilder.RIFLE_TAG;
        private const float PLACE_DISTANCE = 2.5f;
        private const float DROP_HEIGHT = 0.3f;
        private const float GROUND_PROBE_HEIGHT = 50f;
        private const int MAX_GENERATION_PASSES = 4;

        private static readonly string[] GRA_SCOPE = { "configs", "sounds" };
        private static readonly string[] PREFERRED_SLOTS = { "Primary", "Secondary" };
        private static readonly string[] ATTACHMENT_BONES = { WeaponHudComponent.SCOPE_BONE, WeaponHudComponent.SILENCER_BONE, WeaponHudComponent.GRENADE_LAUNCHER_BONE };

        internal static bool IsWeaponResourcePath(string path) => path?.StartsWith(WEAPONS_FOLDER + "/", StringComparison.Ordinal) == true;

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
            ItemDefinition definition = Setup();

            if (definition == null)
            {
                return false;
            }

            int baked = await ItemIconBatchBake.BakeAsync(new[] { definition }, false, CancellationToken.None);
            Debug.Log($"{LOG}Baked {baked} icon variant(s) for {definition.name}.");
            return baked > 0;
        }

        public static ItemDefinition Setup()
        {
            StalkerXRaySources sources = StalkerXRaySources.Open(GRA_SCOPE, out string error);

            if (sources == null)
            {
                Debug.LogError(LOG + error);
                return null;
            }

            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(sources.Index);
            XRayStringTable strings = XRayStringTable.Load(sources.Index, "rus");

            if (system.HasSection(SECTION) == false)
            {
                Debug.LogError($"{LOG}GRA section [{SECTION}] is missing.");
                return null;
            }

            string worldPath = WEAPON_FOLDER + "/" + Path.GetFileName(WORLD_MODEL);
            string hudPath = WEAPON_FOLDER + "/" + Path.GetFileName(HUD_MODEL);
            string weaponMotionsPath = WEAPON_MOTIONS_FOLDER + "/" + Path.GetFileName(WEAPON_MOTIONS);
            string handsMotionsPath = HANDS_MOTIONS_FOLDER + "/" + Path.GetFileName(HANDS_MOTIONS);

            if (CopyFromIwp(new[] { WORLD_MODEL, HUD_MODEL, WEAPON_MOTIONS, HANDS_MOTIONS }, new[] { worldPath, hudPath, weaponMotionsPath, handsMotionsPath }) == false)
            {
                return null;
            }

            AssignRig(weaponMotionsPath, hudPath);
            AssignRig(handsMotionsPath, HANDS_RIG_PATH);
            int textureErrors = GenerateIwpMaterials(new[] { worldPath, hudPath });
            var worldModel = AssetDatabase.LoadAssetAtPath<GameObject>(worldPath);
            var hudModel = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath);
            var weaponMotions = AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(weaponMotionsPath);
            var handsMotions = AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(handsMotionsPath);

            if (worldModel == null || hudModel == null || weaponMotions == null || handsMotions == null)
            {
                Debug.LogError($"{LOG}IWP assets were not imported: world={worldModel != null}, hud={hudModel != null}, weapon motions={weaponMotions != null}, hands motions={handsMotions != null}.");
                return null;
            }

            var log = new StringBuilder();
            int failures = textureErrors;
            failures += ValidateWeaponMotions(weaponMotions, hudModel, log);
            failures += ValidateHandsMotions(handsMotions, log);
            List<string> bones = FindAttachmentBones(hudModel, log);
            GameObject worldVisual = CreateCleanVisual(worldModel, WORLD_VISUAL_PATH, log);
            GameObject hudVisual = CreateCleanVisual(hudModel, HUD_VISUAL_PATH, log);
            ItemDefinition definition = CreateDefinition(system, strings, GraTextFormatter.LoadColors(sources), worldVisual, hudVisual, weaponMotions, handsMotions, bones);
            AddToDatabase(definition);
            List<WorldItem> prefabs = WorldItemPrefabBuilder.Build(new[] { definition });
            GraInventorySoundSetup.SetupTakeSounds(sources, new[] { definition }, log);
            bool placed = prefabs.Count == 1 && PlaceInScene(prefabs[0], log);
            AssetDatabase.SaveAssets();
            Debug.Log($"{LOG}{definition.name}: size {definition.Width}x{definition.Height}, weight {definition.Weight}, attachment bones [{string.Join(", ", bones)}], placed {placed}, failures {failures}.\n{log}");
            return failures == 0 && placed ? definition : null;
        }

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

        internal static bool CopyFromIwp(IReadOnlyList<string> sources, IReadOnlyList<string> targets)
        {
            foreach (string target in targets)
            {
                StalkerXRaySources.EnsureFolder(Path.GetDirectoryName(target).Replace('\\', '/'));
            }

            AssetDatabase.StartAssetEditing();

            try
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    string source = Path.Combine(IWP_ROOT, sources[i]);

                    if (File.Exists(source) == false)
                    {
                        Debug.LogError($"{LOG}IWP file '{source}' is missing.");
                        return false;
                    }

                    string target = Path.GetFullPath(targets[i]);
                    byte[] data = File.ReadAllBytes(source);

                    if (File.Exists(target) == false || File.ReadAllBytes(target).AsSpan().SequenceEqual(data) == false)
                    {
                        File.WriteAllBytes(target, data);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return true;
        }

        internal static void AssignRig(string motionsPath, string rigPath)
        {
            string rigGuid = AssetDatabase.AssetPathToGUID(rigPath);
            var importer = AssetImporter.GetAtPath(motionsPath) as XRayOmfImporter;

            if (importer == null || string.IsNullOrEmpty(rigGuid))
            {
                Debug.LogError($"{LOG}Cannot assign rig '{rigPath}' to '{motionsPath}'.");
                return;
            }

            var serialized = new SerializedObject(importer);
            SerializedProperty rig = serialized.FindProperty("_rigGuid");
            SerializedProperty autoDetect = serialized.FindProperty("_autoDetectRig");

            if (rig.stringValue == rigGuid && autoDetect.boolValue == false)
            {
                return;
            }

            rig.stringValue = rigGuid;
            autoDetect.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
        }

        internal static int GenerateIwpMaterials(IReadOnlyList<string> models)
        {
            XRayAssetSourceOptions options = XRayAssetSourceSettings.GetEffective();
            options.Sources = new XRaySourceConfiguration { Mode = XRaySourceMode.CustomFolder, CustomFolder = Path.GetFullPath(IWP_ROOT), LocalAppDataFolder = options.Sources.LocalAppDataFolder, IncludeGameGamedata = false };
            options.Import.CreateTexturesFolder = true;
            options.Import.CreateMaterialsFolder = true;
            options.Import.PreserveExistingMaterials = false;
            int errors = 0;

            foreach (string model in models)
            {
                XRayGenerationOutcome outcome = XRayGenerationOutcome.Failed;
                XRayResourceReport report = null;

                for (int pass = 0; pass < MAX_GENERATION_PASSES && outcome != XRayGenerationOutcome.Completed; pass++)
                {
                    outcome = XRayAssetGenerationCoordinator.Process(model, options, true, out report);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }

                if (outcome != XRayGenerationOutcome.Completed || report == null)
                {
                    Debug.LogError($"{LOG}Materials for '{model}' were not generated: {outcome}.");
                    errors++;
                    continue;
                }

                foreach (string message in report.Errors)
                {
                    Debug.LogError($"{LOG}{model}: {message}");
                    errors++;
                }

                foreach (XRayReportMaterial material in report.Materials)
                {
                    foreach (XRayReportTexture texture in material.Textures)
                    {
                        if (texture.Role == "Albedo" && string.IsNullOrEmpty(texture.AssetPath))
                        {
                            Debug.LogError($"{LOG}{model}: albedo '{material.Texture}' was not resolved from IWP.");
                            errors++;
                        }
                    }
                }
            }

            return errors;
        }

        private static int ValidateWeaponMotions(XRayMotionLibrary motions, GameObject hudModel, StringBuilder log)
        {
            int failures = 0;

            foreach (XRayMotionEntry entry in motions.Motions)
            {
                failures += CountMissingBindings(entry.Clip, hudModel, log);
            }

            log.AppendLine($"Weapon motions: {motions.Motions.Count} ({string.Join(", ", MotionNames(motions, string.Empty))}), {failures} missing bindings.");
            return failures;
        }

        private static int ValidateHandsMotions(XRayMotionLibrary motions, StringBuilder log)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FirstPersonOutfitCatalog>(OUTFIT_CATALOG_PATH);

            if (catalog == null)
            {
                log.AppendLine("ERROR: outfit catalog is missing.");
                return 1;
            }

            List<string> names = MotionNames(motions, HANDS_MOTION_PREFIX);
            var checkedModels = new HashSet<GameObject>();
            int failures = names.Count == 0 ? 1 : 0;

            foreach (FirstPersonOutfitEntry outfit in catalog.Entries)
            {
                if (outfit.HandsModel == null || checkedModels.Add(outfit.HandsModel) == false)
                {
                    continue;
                }

                foreach (string name in names)
                {
                    failures += motions.TryGetClip(name, out AnimationClip clip) ? CountMissingBindings(clip, outfit.HandsModel, log) : 1;
                }
            }

            log.AppendLine($"Hands motions '{HANDS_MOTION_PREFIX}*': {names.Count} ({string.Join(", ", names)}), checked {checkedModels.Count} hands models, {failures} failures.");
            return failures;
        }

        private static int CountMissingBindings(AnimationClip clip, GameObject model, StringBuilder log)
        {
            if (clip == null)
            {
                return 0;
            }

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path.Length > 0 && model.transform.Find(binding.path) == null)
                {
                    log.AppendLine($"ERROR: '{clip.name}' animates '{binding.path}', which is missing in '{model.name}'.");
                    return 1;
                }
            }

            return 0;
        }

        private static List<string> MotionNames(XRayMotionLibrary motions, string prefix)
        {
            var names = new List<string>();

            foreach (XRayMotionEntry entry in motions.Motions)
            {
                if (entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(entry.Name);
                }
            }

            return names;
        }

        private static List<string> FindAttachmentBones(GameObject hudModel, StringBuilder log)
        {
            var found = new List<string>();
            var all = new HashSet<string>(StringComparer.Ordinal);

            foreach (Transform bone in hudModel.GetComponentsInChildren<Transform>(true))
            {
                all.Add(bone.name);
            }

            foreach (string bone in ATTACHMENT_BONES)
            {
                if (all.Contains(bone))
                {
                    found.Add(bone);
                }
            }

            log.AppendLine($"HUD model bones: {all.Count}, attachment bones: {string.Join(", ", found)}.");
            return found;
        }

        private static GameObject CreateCleanVisual(GameObject model, string path, StringBuilder log)
        {
            StalkerXRaySources.EnsureFolder(WEAPON_FOLDER);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            FirstPersonWeaponView view = existing != null ? existing.GetComponent<FirstPersonWeaponView>() : null;
            Transform muzzle = view != null ? view.Muzzle : null;
            string muzzleParent = muzzle != null && muzzle.parent != null ? muzzle.parent.name : null;
            Vector3 muzzlePosition = muzzle != null ? muzzle.localPosition : Vector3.zero;
            Quaternion muzzleRotation = muzzle != null ? muzzle.localRotation : Quaternion.identity;
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var hidden = new List<string>();

            try
            {
                foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (IsAttachmentMesh(renderer))
                    {
                        renderer.gameObject.SetActive(false);
                        hidden.Add(renderer.name + " (" + (renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "no material") + ")");
                    }
                }

                if (muzzleParent != null)
                {
                    IwpFirstPersonWeaponSetup.CreateMuzzle(root, muzzleParent, muzzlePosition, muzzleRotation);
                }

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                log.AppendLine($"{path}: hidden built-in addon meshes [{string.Join(", ", hidden)}].");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static bool IsAttachmentMesh(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;

            if (mesh == null || bones == null || bones.Length == 0)
            {
                return false;
            }

            foreach (BoneWeight weight in mesh.boneWeights)
            {
                if (IsAttachmentWeight(bones, weight.boneIndex0, weight.weight0) == false || IsAttachmentWeight(bones, weight.boneIndex1, weight.weight1) == false || IsAttachmentWeight(bones, weight.boneIndex2, weight.weight2) == false || IsAttachmentWeight(bones, weight.boneIndex3, weight.weight3) == false)
                {
                    return false;
                }
            }

            return mesh.boneWeights.Length > 0;
        }

        private static bool IsAttachmentWeight(Transform[] bones, int index, float weight)
        {
            if (weight <= 0f)
            {
                return true;
            }

            Transform bone = index >= 0 && index < bones.Length ? bones[index] : null;

            while (bone != null)
            {
                if (Array.IndexOf(ATTACHMENT_BONES, bone.name) >= 0)
                {
                    return true;
                }

                bone = bone.parent;
            }

            return false;
        }

        private static ItemDefinition CreateDefinition(XRayLtxDatabase system, XRayStringTable strings, IReadOnlyDictionary<string, Color32> colors, GameObject worldModel, GameObject hudModel, XRayMotionLibrary weaponMotions, XRayMotionLibrary handsMotions, List<string> bones)
        {
            StalkerXRaySources.EnsureFolder(WEAPON_FOLDER);
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(DEFINITION_PATH);

            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(definition, DEFINITION_PATH);
            }

            string name = Get(system, "inv_name");
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_definitionId").stringValue = AssetDatabase.AssetPathToGUID(DEFINITION_PATH);
            serialized.FindProperty("_displayName").stringValue = strings.Translate(name);
            serialized.FindProperty("_shortName").stringValue = strings.Translate(Get(system, "inv_name_short") ?? name);
            string description = strings.Translate(Get(system, "description"))?.Replace(", C-more Railway Tactical", string.Empty, StringComparison.Ordinal).Replace(", «EOTech»", string.Empty, StringComparison.Ordinal);
            serialized.FindProperty("_description").stringValue = GraTextFormatter.Format(description, colors);
            serialized.FindProperty("_category").stringValue = InventoryDemoContentBuilder.WEAPON_CATEGORY;
            SerializedProperty tags = serialized.FindProperty("_tags");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).stringValue = RIFLE_TAG;
            serialized.FindProperty("_size").vector2IntValue = new Vector2Int(GetInt(system, "inv_grid_width"), GetInt(system, "inv_grid_height"));
            serialized.FindProperty("_weight").floatValue = GetFloat(system, "inv_weight");
            serialized.FindProperty("_maxStack").intValue = 1;
            serialized.FindProperty("_allowRotation").boolValue = true;
            serialized.FindProperty("_visualPrefab").objectReferenceValue = worldModel;
            ItemIconDefaults.Apply(serialized, ItemIconDefaults.SIDE_VIEW_ANGLES);
            SerializedProperty components = serialized.FindProperty("_components");
            components.ClearArray();
            SerializedProperty equippable = AddComponent(components, new EquippableItemComponent());
            SetStrings(equippable.FindPropertyRelative("_preferredSlotIds"), PREFERRED_SLOTS);
            FillStats(AddComponent(components, new WeaponStatsComponent()), system);
            SerializedProperty hud = AddComponent(components, new WeaponHudComponent());
            hud.FindPropertyRelative("_hudModel").objectReferenceValue = hudModel;
            hud.FindPropertyRelative("_weaponMotions").objectReferenceValue = weaponMotions;
            hud.FindPropertyRelative("_handsMotions").objectReferenceValue = handsMotions;
            hud.FindPropertyRelative("_handsMotionPrefix").stringValue = HANDS_MOTION_PREFIX;
            SetStrings(hud.FindPropertyRelative("_attachmentBones"), bones);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }

        private static void FillStats(SerializedProperty stats, XRayLtxDatabase system)
        {
            stats.FindPropertyRelative("_section").stringValue = SECTION;
            stats.FindPropertyRelative("_weaponClass").stringValue = Get(system, "weapon_class") ?? string.Empty;
            stats.FindPropertyRelative("_cost").intValue = GetInt(system, "cost");
            stats.FindPropertyRelative("_repairType").stringValue = Get(system, "repair_type") ?? string.Empty;
            stats.FindPropertyRelative("_hitPower").floatValue = FirstFloat(Get(system, "hit_power"));
            stats.FindPropertyRelative("_hitImpulse").floatValue = GetFloat(system, "hit_impulse");
            stats.FindPropertyRelative("_bulletSpeed").floatValue = GetFloat(system, "bullet_speed");
            stats.FindPropertyRelative("_fireDistance").floatValue = GetFloat(system, "fire_distance");
            stats.FindPropertyRelative("_rpm").floatValue = GetFloat(system, "rpm");
            List<string> modes = system.GetList(SECTION, "fire_modes") ?? new List<string>();
            SerializedProperty fireModes = stats.FindPropertyRelative("_fireModes");
            fireModes.arraySize = modes.Count;

            for (int i = 0; i < modes.Count; i++)
            {
                fireModes.GetArrayElementAtIndex(i).intValue = int.TryParse(modes[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode) ? mode : 0;
            }

            stats.FindPropertyRelative("_magazineSize").intValue = GetInt(system, "ammo_mag_size");
            SetStrings(stats.FindPropertyRelative("_ammoClasses"), IwpAmmoSetup.ReadAugAmmoClasses());
            stats.FindPropertyRelative("_fireDispersionBase").floatValue = GetFloat(system, "fire_dispersion_base");
            stats.FindPropertyRelative("_camDispersion").floatValue = GetFloat(system, "cam_dispersion");
            stats.FindPropertyRelative("_camDispersionIncrement").floatValue = GetFloat(system, "cam_dispersion_inc");
            stats.FindPropertyRelative("_conditionShotDecrement").floatValue = GetFloat(system, "condition_shot_dec");
            stats.FindPropertyRelative("_misfireProbability").floatValue = GetFloat(system, "misfire_probability");
            stats.FindPropertyRelative("_silencerStatus").enumValueIndex = GetInt(system, "silencer_status");
            stats.FindPropertyRelative("_silencerSection").stringValue = Get(system, "silencer_name") ?? string.Empty;
            stats.FindPropertyRelative("_scopeStatus").enumValueIndex = GetInt(system, "scope_status");
            List<string> scopes = system.GetList(SECTION, "scopes") ?? new List<string>();
            scopes.RemoveAll(GraWeaponAddonSetup.IsExcludedAddon);
            SetStrings(stats.FindPropertyRelative("_scopes"), scopes);
            string launcher = Get(system, "grenade_launcher_name") ?? string.Empty;
            bool excludedLauncher = GraWeaponAddonSetup.IsExcludedAddon(launcher);
            stats.FindPropertyRelative("_grenadeLauncherStatus").enumValueIndex = excludedLauncher ? (int)WeaponAddonStatus.None : GetInt(system, "grenade_launcher_status");
            stats.FindPropertyRelative("_grenadeLauncherSection").stringValue = excludedLauncher ? string.Empty : launcher;
        }

        private static SerializedProperty AddComponent(SerializedProperty components, ItemDefinitionComponent component)
        {
            int index = components.arraySize;
            components.arraySize++;
            SerializedProperty element = components.GetArrayElementAtIndex(index);
            element.managedReferenceValue = component;
            return element;
        }

        private static void SetStrings(SerializedProperty array, IReadOnlyList<string> values)
        {
            array.arraySize = values.Count;

            for (int i = 0; i < values.Count; i++)
            {
                array.GetArrayElementAtIndex(i).stringValue = values[i].Trim();
            }
        }

        private static void AddToDatabase(ItemDefinition definition)
        {
            var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(InventoryDemoContentBuilder.DATABASE_PATH);

            if (database == null)
            {
                Debug.LogError($"{LOG}Item database '{InventoryDemoContentBuilder.DATABASE_PATH}' is missing.");
                return;
            }

            var definitions = new List<ItemDefinition>(database.Definitions);

            if (definitions.Contains(definition) == false)
            {
                definitions.Add(definition);
                database.SetDefinitions(definitions);
                EditorUtility.SetDirty(database);
            }
        }

        private static bool PlaceInScene(WorldItem prefab, StringBuilder log)
        {
            Scene scene = EditorSceneManager.OpenScene(InventoryIntegrationSetup.SCENE_PATH, OpenSceneMode.Single);
            GameObject root = null;
            StalkerPlayerController player = null;

            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name == WorldItemPrefabBuilder.WORLD_ITEMS_ROOT)
                {
                    root = candidate;
                }

                player = player != null ? player : candidate.GetComponentInChildren<StalkerPlayerController>(true);
            }

            if (root == null || player == null)
            {
                log.AppendLine($"ERROR: scene has no '{WorldItemPrefabBuilder.WORLD_ITEMS_ROOT}' root or player.");
                return false;
            }

            GameObject instance = null;

            foreach (Transform child in root.transform)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == prefab.gameObject)
                {
                    instance = child.gameObject;
                }
            }

            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, root.transform);
            }

            Vector3 forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
            forward = forward.sqrMagnitude > 0f ? forward : Vector3.forward;
            Vector3 position = player.transform.position + forward * PLACE_DISTANCE;
            instance.SetActive(false);
            Physics.SyncTransforms();
            float ground = Physics.Raycast(position + Vector3.up * GROUND_PROBE_HEIGHT, Vector3.down, out RaycastHit hit, GROUND_PROBE_HEIGHT * 2f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : player.transform.position.y;
            instance.SetActive(true);
            var collider = instance.GetComponent<BoxCollider>();
            float bottom = collider != null ? collider.center.y - collider.size.y * 0.5f : 0f;
            instance.transform.SetPositionAndRotation(new Vector3(position.x, ground + DROP_HEIGHT - bottom, position.z), Quaternion.LookRotation(Vector3.Cross(Vector3.up, forward), Vector3.up));
            bool saved = EditorSceneManager.SaveScene(scene);
            log.AppendLine($"Scene instance '{instance.name}' at {instance.transform.position}, saved {saved}.");
            return saved;
        }

        private static string Get(XRayLtxDatabase system, string key)
        {
            string value = XRayLtxDatabase.Unquote(system.GetValue(SECTION, key));
            return value != null ? value.Trim() : null;
        }

        private static int GetInt(XRayLtxDatabase system, string key)
        {
            return Mathf.RoundToInt(GetFloat(system, key));
        }

        private static float GetFloat(XRayLtxDatabase system, string key)
        {
            return FirstFloat(Get(system, key));
        }

        private static float FirstFloat(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0f;
            }

            return StalkerXRaySources.TryFloat(value.Split(',')[0], out float result) ? result : 0f;
        }
    }
}
