using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RaidIntoDarkness.FirstPerson;
using UnityEditor;
using UnityEngine;
using XRay.Importer;
using XRay.Importer.Core.Anm;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Omf;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;

namespace RaidIntoDarkness.Player.Editor
{
    public static class FirstPersonEquipmentAnimationSetup
    {
        public const string ROOT = "Assets/_Source/FirstPerson/Equipment";
        public const string MODELS = ROOT + "/Models";
        public const string ITEM_MOTIONS = ROOT + "/Animations";
        public const string CAMERA = ROOT + "/Camera";
        public const string SOUNDS = ROOT + "/Sounds";
        public const string LIBRARY_PATH = ROOT + "/EquipmentAnimationLibrary.asset";
        public const string FADE_MATERIAL_PATH = ROOT + "/ScreenAddColor.mat";
        public const string HANDS_MOTIONS = "Assets/_Source/FirstPerson/Animations/Hands";
        public const string HANDS_RIG_PATH = "Assets/_Source/FirstPerson/Hands/Models/wpn_hand_01.ogf";
        public const string FADE_SHADER = "FullScreen/RaidIntoDarkness/ScreenAddColor";
        public const string FADE_EFFECT = "anims/deimos_short.ppe";
        public const string HUD_AUDIO = "HudAudio";

        private const string MENU = "Tools/X-Ray/Setup Equipment Animations";
        private const string LOG = "[EquipmentAnimations] ";
        private const string HANDS_MOTION_FOLDER = "meshes/anomaly_weapons/hud_hands_animation/";
        private const string ITEM_MOTION_FOLDER = "meshes/anomaly_weapons/hud_animation/";
        private const string HEADGEAR_BLACKLIST = "configs/items/items/headgear_blacklist.ltx";
        private const int MAX_SETTLE_PASSES = 4;
        private const float FADE_SAMPLE_RATE = 60.0f;
        private const float ANM_FPS = 30.0f;

        private static readonly string[] INDEX_SCOPE = { "configs", "meshes/anomaly_weapons/hud_hands_animation", "meshes/anomaly_weapons/hud_animation", "meshes/anomaly_weapons/liz_outfit_animations", "meshes/dynamics/weapons/wpn_headgear", "anims", "sounds/actor" };

        private sealed class MotionSource
        {
            public string Section;
            public string Key;
            public string HandsMotion;
            public string ItemMotion;
            public string ItemVisual;
            public string Camera;
            public string Sound;
            public float HudFieldOfView;
            public Vector3 HandsPosition;
            public Vector3 HandsOrientation;
        }

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
            XRayAssetSourceOptions options = XRayAssetSourceSettings.GetEffective();
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(CloneWithScope(options.Sources));
            if (!plan.IsValid)
            {
                Debug.LogError(LOG + "X-Ray asset sources are invalid: " + string.Join("; ", plan.Errors));
                return false;
            }

            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(index);
            List<MotionSource> sources = CollectSources(system);
            List<string> fadeSections = system.Sections.Keys.Where(section => section.EndsWith(FirstPersonEquipmentAnimationLibrary.HEADGEAR_SUFFIX, StringComparison.Ordinal) && IsTrue(system.GetValue(section, "fade_use"))).OrderBy(section => section, StringComparer.Ordinal).ToList();
            List<string> blacklist = XRayLtxDatabase.LoadFile(index, HEADGEAR_BLACKLIST).Sections.Keys.OrderBy(section => section, StringComparer.Ordinal).ToList();
            Dictionary<string, string> handsMotions = MapMotions(index, HANDS_MOTION_FOLDER);
            Dictionary<string, string> itemMotions = MapMotions(index, ITEM_MOTION_FOLDER);
            Debug.Log($"{LOG}Profile '{plan.ProfileName}': {sources.Count} motions, {fadeSections.Count} fade sections, {blacklist.Count} blacklisted headgear, {handsMotions.Count} hands and {itemMotions.Count} item motions indexed.");

            var copies = new Dictionary<string, string>(StringComparer.Ordinal);
            var handsOmfs = new HashSet<string>(StringComparer.Ordinal);
            var itemOmfs = new Dictionary<string, string>(StringComparer.Ordinal);
            var models = new HashSet<string>(StringComparer.Ordinal);
            int failures = 0;
            foreach (MotionSource source in sources)
            {
                if (!handsMotions.TryGetValue(source.HandsMotion, out string handsOmf))
                {
                    Debug.LogError($"{LOG}[{source.Section}] {source.Key}: hands motion '{source.HandsMotion}' was not found in {HANDS_MOTION_FOLDER}.");
                    failures++;
                    continue;
                }

                copies[handsOmf] = HANDS_MOTIONS + "/" + Path.GetFileName(handsOmf);
                handsOmfs.Add(copies[handsOmf]);
                if (!string.IsNullOrEmpty(source.ItemVisual))
                {
                    string visual = ToMeshPath(source.ItemVisual);
                    string modelPath = MODELS + "/" + Path.GetFileName(visual);
                    copies[visual] = modelPath;
                    models.Add(modelPath);
                    if (itemMotions.TryGetValue(source.ItemMotion, out string itemOmf))
                    {
                        string itemOmfPath = ITEM_MOTIONS + "/" + Path.GetFileNameWithoutExtension(itemOmf) + "__" + Path.GetFileNameWithoutExtension(visual) + ".omf";
                        copies[itemOmf + "|" + itemOmfPath] = itemOmfPath;
                        itemOmfs[itemOmfPath] = modelPath;
                    }
                    else
                    {
                        Debug.LogError($"{LOG}[{source.Section}] {source.Key}: item motion '{source.ItemMotion}' was not found in {ITEM_MOTION_FOLDER}.");
                        failures++;
                    }
                }

                if (!string.IsNullOrEmpty(source.Camera))
                {
                    copies["anims/" + Slash(source.Camera)] = CAMERA + "/" + Path.GetFileName(Slash(source.Camera));
                }

                if (!string.IsNullOrEmpty(source.Sound))
                {
                    copies[SoundPath(source.Sound)] = SoundAssetPath(source.Sound);
                }
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (KeyValuePair<string, string> copy in copies)
                {
                    string virtualPath = copy.Key.Split('|')[0];
                    if (!index.TryGet(virtualPath, out XRayIndexedFile file))
                    {
                        Debug.LogError($"{LOG}'{virtualPath}' is missing in the active sources.");
                        failures++;
                        continue;
                    }

                    CopyFile(index.GetFullPath(file.Winner), copy.Value);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssignRig(handsOmfs, HANDS_RIG_PATH);
            foreach (KeyValuePair<string, string> pair in itemOmfs)
            {
                AssignRig(new[] { pair.Key }, pair.Value);
            }

            SettleResources(models.ToList(), options);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var motions = new List<FirstPersonHudMotion>();
            foreach (MotionSource source in sources)
            {
                FirstPersonHudMotion motion = BuildMotion(source, handsMotions, itemMotions, copies);
                if (motion == null)
                {
                    failures++;
                    continue;
                }

                motions.Add(motion);
            }

            failures += ValidateHands(motions);
            failures += ValidateItems(motions);
            FirstPersonEquipmentAnimationLibrary library = SaveLibrary(motions, fadeSections, blacklist);
            AnimationCurve fade = ReadFade(index);
            Material material = EnsureFadeMaterial();
            if (fade == null || material == null)
            {
                failures++;
            }

            ConfigurePrefab(library, fade, material);
            AssetDatabase.SaveAssets();
            Debug.Log($"{LOG}Built {motions.Count} motions into {LIBRARY_PATH}, {failures} failures.");
            return failures == 0;
        }

        private static XRaySourceConfiguration CloneWithScope(XRaySourceConfiguration source)
        {
            var configuration = new XRaySourceConfiguration
            {
                Mode = source.Mode,
                BaseAnomalyFolder = source.BaseAnomalyFolder,
                Mo2Folder = source.Mo2Folder,
                Mo2Instance = source.Mo2Instance,
                Mo2Profile = source.Mo2Profile,
                CustomFolder = source.CustomFolder,
                LocalAppDataFolder = source.LocalAppDataFolder,
                IncludeGameGamedata = source.IncludeGameGamedata,
                TreatModRootAsGamedata = source.TreatModRootAsGamedata
            };

            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(INDEX_SCOPE);
            return configuration;
        }

        private static List<MotionSource> CollectSources(XRayLtxDatabase system)
        {
            var result = new List<MotionSource>();
            AddSource(result, system, FirstPersonEquipmentAnimationLibrary.OUTFIT_SECTION, FirstPersonEquipmentAnimationLibrary.ANM_EQUIP, "cam", "snd");
            AddSource(result, system, FirstPersonEquipmentAnimationLibrary.OUTFIT_FAST_SECTION, FirstPersonEquipmentAnimationLibrary.ANM_EQUIP, "cam", "snd");
            foreach (string section in system.Sections.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (section.EndsWith(FirstPersonEquipmentAnimationLibrary.HEADGEAR_SUFFIX, StringComparison.Ordinal))
                {
                    AddSource(result, system, section, FirstPersonEquipmentAnimationLibrary.ANM_EQUIP, "cam_equip", "snd_equip");
                    AddSource(result, system, section, FirstPersonEquipmentAnimationLibrary.ANM_UNEQUIP, "cam_unequip", "snd_unequip");
                }
                else if ((section.StartsWith(FirstPersonEquipmentAnimationLibrary.BACKPACK_EQUIP_PREFIX, StringComparison.Ordinal) || section.StartsWith(FirstPersonEquipmentAnimationLibrary.BACKPACK_UNEQUIP_PREFIX, StringComparison.Ordinal)) && section.EndsWith(FirstPersonEquipmentAnimationLibrary.HUD_SUFFIX, StringComparison.Ordinal))
                {
                    AddSource(result, system, section, FirstPersonEquipmentAnimationLibrary.ANM_USE, "cam", "snd");
                }
            }

            return result;
        }

        private static void AddSource(List<MotionSource> result, XRayLtxDatabase system, string section, string key, string cameraKey, string soundKey)
        {
            string value = system.GetValue(section, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                Debug.LogWarning($"{LOG}[{section}] has no '{key}'.");
                return;
            }

            string[] names = value.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0).ToArray();
            string hands = names[0];
            result.Add(new MotionSource
            {
                Section = section,
                Key = key,
                HandsMotion = hands,
                ItemMotion = names.Length > 1 ? names[1] : hands,
                ItemVisual = Clean(system.GetValue(section, "item_visual")),
                Camera = Clean(system.GetValue(section, cameraKey)),
                Sound = Clean(system.GetValue(section, soundKey)),
                HudFieldOfView = ParseFloat(system.GetValue(section, "hud_fov"), 0.0f),
                HandsPosition = ParseVector(system.GetValue(section, "hands_position_16x9") ?? system.GetValue(section, "hands_position")),
                HandsOrientation = ParseVector(system.GetValue(section, "hands_orientation_16x9") ?? system.GetValue(section, "hands_orientation"))
            });
        }

        private static Dictionary<string, string> MapMotions(XRayAssetIndex index, string folder)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XRayIndexedFile file in index.Files.OrderBy(file => file.VirtualPath, StringComparer.Ordinal))
            {
                if (!file.VirtualPath.StartsWith(folder, StringComparison.Ordinal) || !file.VirtualPath.EndsWith(".omf", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    OmfData data = OmfReader.ReadFile(File.ReadAllBytes(index.GetFullPath(file.Winner)), file.VirtualPath);
                    foreach (OmfMotion motion in data.Motions)
                    {
                        if (!map.ContainsKey(motion.Name))
                        {
                            map.Add(motion.Name, file.VirtualPath);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"{LOG}'{file.VirtualPath}' could not be read: {exception.Message}");
                }
            }

            return map;
        }

        private static FirstPersonHudMotion BuildMotion(MotionSource source, Dictionary<string, string> handsMotions, Dictionary<string, string> itemMotions, Dictionary<string, string> copies)
        {
            string id = FirstPersonHudMotion.MakeId(source.Section, source.Key);
            if (!handsMotions.TryGetValue(source.HandsMotion, out string handsOmf))
            {
                return null;
            }

            AnimationClip hands = LoadClip(copies[handsOmf], source.HandsMotion);
            if (hands == null)
            {
                Debug.LogError($"{LOG}{id}: clip '{source.HandsMotion}' is missing in '{copies[handsOmf]}'.");
                return null;
            }

            GameObject itemModel = null;
            AnimationClip item = null;
            if (!string.IsNullOrEmpty(source.ItemVisual))
            {
                string visual = ToMeshPath(source.ItemVisual);
                itemModel = AssetDatabase.LoadAssetAtPath<GameObject>(copies[visual]);
                if (itemModel == null)
                {
                    Debug.LogError($"{LOG}{id}: item model '{copies[visual]}' was not imported.");
                    return null;
                }

                if (itemMotions.TryGetValue(source.ItemMotion, out string itemOmf))
                {
                    string path = ITEM_MOTIONS + "/" + Path.GetFileNameWithoutExtension(itemOmf) + "__" + Path.GetFileNameWithoutExtension(visual) + ".omf";
                    item = LoadClip(path, source.ItemMotion);
                }

                if (item == null)
                {
                    Debug.LogError($"{LOG}{id}: item clip '{source.ItemMotion}' is missing.");
                    return null;
                }
            }

            XRayAnmAsset camera = string.IsNullOrEmpty(source.Camera) ? null : AssetDatabase.LoadAssetAtPath<XRayAnmAsset>(CAMERA + "/" + Path.GetFileName(Slash(source.Camera)));
            AudioClip sound = string.IsNullOrEmpty(source.Sound) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(SoundAssetPath(source.Sound));
            if (!string.IsNullOrEmpty(source.Camera) && camera == null)
            {
                Debug.LogError($"{LOG}{id}: camera effect '{source.Camera}' was not imported.");
            }

            if (!string.IsNullOrEmpty(source.Sound) && sound == null)
            {
                Debug.LogError($"{LOG}{id}: sound '{source.Sound}' was not imported.");
            }

            return new FirstPersonHudMotion(id, hands, itemModel, item, camera, sound, source.HudFieldOfView, source.HandsPosition, source.HandsOrientation);
        }

        private static AnimationClip LoadClip(string path, string motion)
        {
            var library = AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(path);
            return library != null && library.TryGetClip(motion, out AnimationClip clip) ? clip : null;
        }

        private static int ValidateHands(List<FirstPersonHudMotion> motions)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FirstPersonOutfitCatalog>(FirstPersonPickupSetup.CATALOG_PATH);
            if (catalog == null)
            {
                Debug.LogError($"{LOG}Outfit catalog '{FirstPersonPickupSetup.CATALOG_PATH}' is missing.");
                return 1;
            }

            var clips = new HashSet<AnimationClip>(motions.Select(motion => motion.Hands));
            var checkedModels = new HashSet<GameObject>();
            int failures = 0;
            foreach (FirstPersonOutfitEntry entry in catalog.Entries)
            {
                if (entry.HandsModel == null || !checkedModels.Add(entry.HandsModel))
                {
                    continue;
                }

                foreach (AnimationClip clip in clips)
                {
                    string missing = FindMissingBinding(clip, entry.HandsModel);
                    if (missing != null)
                    {
                        Debug.LogError($"{LOG}'{clip.name}' animates '{missing}', which is missing in hands '{entry.HandsModel.name}'.");
                        failures++;
                    }
                }

                if (entry.HandsModel.GetComponent<XRayModel>() != null && entry.HandsModel.GetComponent<XRayModel>().FindBone(FirstPersonHudAnimator.ITEM_ANCHOR_BONE) == null)
                {
                    Debug.LogError($"{LOG}Hands '{entry.HandsModel.name}' have no '{FirstPersonHudAnimator.ITEM_ANCHOR_BONE}' bone.");
                    failures++;
                }
            }

            Debug.Log($"{LOG}Checked {clips.Count} hands clips against {checkedModels.Count} hands models.");
            return failures;
        }

        private static int ValidateItems(List<FirstPersonHudMotion> motions)
        {
            int failures = 0;
            foreach (FirstPersonHudMotion motion in motions)
            {
                if (motion.ItemModel == null || motion.Item == null)
                {
                    continue;
                }

                string missing = FindMissingBinding(motion.Item, motion.ItemModel);
                if (missing != null)
                {
                    Debug.LogError($"{LOG}{motion.Id}: '{motion.Item.name}' animates '{missing}', which is missing in '{motion.ItemModel.name}'.");
                    failures++;
                }
            }

            return failures;
        }

        private static string FindMissingBinding(AnimationClip clip, GameObject model)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path.Length > 0 && model.transform.Find(binding.path) == null)
                {
                    return binding.path;
                }
            }

            return null;
        }

        private static FirstPersonEquipmentAnimationLibrary SaveLibrary(List<FirstPersonHudMotion> motions, List<string> fadeSections, List<string> blacklist)
        {
            var library = AssetDatabase.LoadAssetAtPath<FirstPersonEquipmentAnimationLibrary>(LIBRARY_PATH);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<FirstPersonEquipmentAnimationLibrary>();
                AssetDatabase.CreateAsset(library, LIBRARY_PATH);
            }

            library.SetData(motions, fadeSections, blacklist);
            EditorUtility.SetDirty(library);
            return library;
        }

        private static AnimationCurve ReadFade(XRayAssetIndex index)
        {
            if (!index.TryGet(FADE_EFFECT, out XRayIndexedFile file))
            {
                Debug.LogError($"{LOG}'{FADE_EFFECT}' is missing in the active sources.");
                return null;
            }

            var reader = new XRayReader(File.ReadAllBytes(index.GetFullPath(file.Winner)), FADE_EFFECT);
            var warnings = new List<string>();
            reader.ReadUInt32();
            reader.ReadSingle();
            for (int i = 0; i < 3; i++)
            {
                XRayAnmReader.ReadEnvelope(reader, warnings);
            }

            reader.ReadSingle();
            XRayAnmEnvelope red = XRayAnmReader.ReadEnvelope(reader, warnings);
            XRayAnmEnvelope green = XRayAnmReader.ReadEnvelope(reader, warnings);
            XRayAnmEnvelope blue = XRayAnmReader.ReadEnvelope(reader, warnings);
            if (red.Keys.Count == 0)
            {
                Debug.LogError($"{LOG}'{FADE_EFFECT}' has no add color keys.");
                return null;
            }

            float end = (float)Math.Max(red.EndTime, Math.Max(green.EndTime, blue.EndTime));
            int samples = Mathf.Max(2, Mathf.CeilToInt(end * FADE_SAMPLE_RATE) + 1);
            var keys = new Keyframe[samples];
            for (int i = 0; i < samples; i++)
            {
                float time = Mathf.Min(end, i / FADE_SAMPLE_RATE);
                double value = (red.Evaluate(time, XRayAnmSemantics.Engine, ANM_FPS) + green.Evaluate(time, XRayAnmSemantics.Engine, ANM_FPS) + blue.Evaluate(time, XRayAnmSemantics.Engine, ANM_FPS)) / 3.0;
                keys[i] = new Keyframe(time, (float)value);
            }

            var curve = new AnimationCurve(keys);
            for (int i = 0; i < samples; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }

            foreach (string warning in warnings)
            {
                Debug.LogWarning($"{LOG}{FADE_EFFECT}: {warning}");
            }

            Debug.Log($"{LOG}{FADE_EFFECT}: add color {curve.Evaluate(0.0f):0.000} .. {keys.Min(key => key.value):0.000} over {end:0.000}s.");
            return curve;
        }

        private static Material EnsureFadeMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(FADE_MATERIAL_PATH);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find(FADE_SHADER);
            if (shader == null)
            {
                Debug.LogError($"{LOG}Shader '{FADE_SHADER}' was not found.");
                return null;
            }

            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(FADE_MATERIAL_PATH) };
            AssetDatabase.CreateAsset(material, FADE_MATERIAL_PATH);
            return material;
        }

        private static void ConfigurePrefab(FirstPersonEquipmentAnimationLibrary library, AnimationCurve fade, Material material)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(StalkerPlayerSetup.PREFAB_PATH);
            try
            {
                var rig = root.GetComponent<FirstPersonRig>();
                var player = root.GetComponent<StalkerPlayerController>();
                if (rig == null || player == null)
                {
                    throw new InvalidOperationException("FPS_Player.prefab must contain FirstPersonRig and StalkerPlayerController.");
                }

                Transform audioTransform = root.transform.Find(HUD_AUDIO);
                if (audioTransform == null)
                {
                    audioTransform = new GameObject(HUD_AUDIO).transform;
                    audioTransform.SetParent(root.transform, false);
                }

                AudioSource audio = audioTransform.GetComponent<AudioSource>();
                if (audio == null)
                {
                    audio = audioTransform.gameObject.AddComponent<AudioSource>();
                }

                audio.playOnAwake = false;
                audio.loop = false;
                audio.spatialBlend = 0.0f;
                audio.dopplerLevel = 0.0f;
                FirstPersonHudAnimator animator = root.GetComponent<FirstPersonHudAnimator>();
                if (animator == null)
                {
                    animator = root.AddComponent<FirstPersonHudAnimator>();
                }

                var animatorObject = new SerializedObject(animator);
                animatorObject.FindProperty("_rig").objectReferenceValue = rig;
                animatorObject.FindProperty("_library").objectReferenceValue = library;
                animatorObject.FindProperty("_audio").objectReferenceValue = audio;
                animatorObject.ApplyModifiedPropertiesWithoutUndo();
                StalkerScreenFade screenFade = root.GetComponent<StalkerScreenFade>();
                if (screenFade == null)
                {
                    screenFade = root.AddComponent<StalkerScreenFade>();
                }

                var fadeObject = new SerializedObject(screenFade);
                if (fade != null)
                {
                    fadeObject.FindProperty("_addColor").animationCurveValue = fade;
                }

                fadeObject.FindProperty("_material").objectReferenceValue = material;
                fadeObject.ApplyModifiedPropertiesWithoutUndo();
                var playerObject = new SerializedObject(player);
                playerObject.FindProperty("_hudAnimator").objectReferenceValue = animator;
                playerObject.FindProperty("_screenFade").objectReferenceValue = screenFade;
                playerObject.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, StalkerPlayerSetup.PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssignRig(IEnumerable<string> motionPaths, string rigPath)
        {
            string rigGuid = AssetDatabase.AssetPathToGUID(rigPath);
            if (string.IsNullOrEmpty(rigGuid))
            {
                Debug.LogError($"{LOG}Rig '{rigPath}' is missing.");
                return;
            }

            foreach (string path in motionPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as XRayOmfImporter;
                if (importer == null)
                {
                    Debug.LogError($"{LOG}'{path}' is not imported as an OMF.");
                    continue;
                }

                var serialized = new SerializedObject(importer);
                SerializedProperty rig = serialized.FindProperty("_rigGuid");
                SerializedProperty autoDetect = serialized.FindProperty("_autoDetectRig");
                if (rig.stringValue == rigGuid && !autoDetect.boolValue)
                {
                    continue;
                }

                rig.stringValue = rigGuid;
                autoDetect.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
            }
        }

        private static void SettleResources(List<string> models, XRayAssetSourceOptions options)
        {
            for (int pass = 0; pass < MAX_SETTLE_PASSES; pass++)
            {
                List<string> pending = models.Where(model => XRayAssetGenerationCoordinator.NeedsGeneration(model, options)).ToList();
                if (pending.Count == 0)
                {
                    return;
                }

                foreach (string model in pending)
                {
                    XRayAssetGenerationCoordinator.Process(model, options, false, out XRayResourceReport _);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            int remaining = models.Count(model => XRayAssetGenerationCoordinator.NeedsGeneration(model, options));
            if (remaining > 0)
            {
                Debug.LogWarning($"{LOG}{remaining} item models still have unassigned material slots.");
            }
        }

        private static void CopyFile(string sourcePath, string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            byte[] data = File.ReadAllBytes(sourcePath);
            if (File.Exists(fullPath) && File.ReadAllBytes(fullPath).SequenceEqual(data))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, data);
        }

        private static string ToMeshPath(string visual)
        {
            string path = Slash(visual);
            return "meshes/" + (path.EndsWith(".ogf", StringComparison.Ordinal) ? path : path + ".ogf");
        }

        private static string SoundPath(string sound)
        {
            return "sounds/" + Slash(sound) + ".ogg";
        }

        private static string SoundAssetPath(string sound)
        {
            return SOUNDS + "/" + Slash(sound).Replace('/', '_') + ".ogg";
        }

        private static string Slash(string value)
        {
            return value.Replace('\\', '/').Trim().ToLowerInvariant();
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : XRayLtxDatabase.Unquote(value.Trim());
        }

        private static bool IsTrue(string value)
        {
            string text = value != null ? value.Trim().ToLowerInvariant() : string.Empty;
            return text == "true" || text == "1" || text == "on" || text == "yes";
        }

        private static float ParseFloat(string value, float fallback)
        {
            return value != null && float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : fallback;
        }

        private static Vector3 ParseVector(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Vector3.zero;
            }

            string[] parts = value.Split(',');
            return new Vector3(parts.Length > 0 ? ParseFloat(parts[0], 0.0f) : 0.0f, parts.Length > 1 ? ParseFloat(parts[1], 0.0f) : 0.0f, parts.Length > 2 ? ParseFloat(parts[2], 0.0f) : 0.0f);
        }
    }
}
