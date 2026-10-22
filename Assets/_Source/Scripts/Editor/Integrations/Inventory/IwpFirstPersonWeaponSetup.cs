using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Inventory;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Player.Editor;
using RaidIntoDarkness.Weapons;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using XRay.Importer;
using XRay.Importer.Sources.Ltx;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class IwpFirstPersonWeaponSetup
    {
        public const string FIRST_PERSON_FOLDER = IwpWeaponSetup.WEAPONS_FOLDER + "/FirstPerson";
        public const string CAMERA_FOLDER = IwpWeaponSetup.WEAPONS_FOLDER + "/Animations/Camera";
        public const string BLEND_FOLDER = IwpWeaponSetup.WEAPONS_FOLDER + "/Animations/Blend";
        public const string SOUNDS_FOLDER = "Assets/_Source/Audio/Weapons";
        public const string ASSET_PATH = FIRST_PERSON_FOLDER + "/" + IwpWeaponSetup.SECTION + ".asset";
        public const string WEAPON_AUDIO = "WeaponAudio";
        public const string WEAPON_SHOT_AUDIO = "WeaponShotAudio";
        public const string WEAPON_HUD = "WeaponHud";
        public const string AMMO_TEXT = "Ammo";
        public const string MUZZLE_POINT = "MuzzlePoint";

        private const string MENU = "Tools/Inventory/Setup First Person Weapon (AUG A3)";
        private const string LOG = "[FirstPersonWeapon] ";
        private const string HUD_SECTION = IwpWeaponSetup.SECTION + "_hud";
        private const string A1_WEAPON_MOTIONS = "meshes/dynamics/weapons/wpn_aug/wpn_aug_hud_animation.omf";
        private const string IWP_CAMERA = "anims/camera_effects/weapon/";
        private const string IWP_BLEND = "anims/blend/";
        private const string IWP_SOUNDS = "sounds/weapons/";
        private const string BLEND_TYPE = "f2000";
        private const string MOTION_PREFIX = "aug_";
        private const int LOW_AMMO_THRESHOLD = 5;
        private const int MAX_SHOT_VARIANTS = 6;
        private const float AMMO_FONT_SIZE = 32f;
        private const int AIM_SOUND_VARIANTS = 10;
        private const string DRAW_SOUND = "generic_draw";
        private const string HOLSTER_SOUND = "generic_holster";

        private static readonly string[] GRA_SCOPE = { "configs" };
        private static readonly string[] CAMERA_EFFECTS = { "aug_a3_reload", "aug_a3_reload_empty", "aug_draw", "aug_firemode", "aug_misfire", "aug_shoot", "aug_shoot1", "aug_shoot2", "aug_shoot_aim", "aug_shoot_aim1", "aug_shoot_aim2" };
        private static readonly string[] BLENDS = { "aim_start_" + BLEND_TYPE, "aim_idle_" + BLEND_TYPE, "aim_end_" + BLEND_TYPE };
        private static readonly Vector3 AIM_OFFSET = new Vector3(0.0127f, -0.006f, 0.0f);
        private static readonly Color32 AMMO_COLOR = new Color32(240, 217, 182, 255);

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
                success = Setup() != null;
            }
            catch (Exception exception)
            {
                Debug.LogError(LOG + "Setup failed: " + exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        [MenuItem("Tools/Inventory/Setup IWP Weapon Aim and Sounds (AUG A3)")]
        public static void SetupAimAndSoundsFromMenu()
        {
            SetupAimAndSounds();
        }

        public static bool SetupAimAndSounds()
        {
            var asset = AssetDatabase.LoadAssetAtPath<FirstPersonWeaponAsset>(ASSET_PATH);

            if (asset == null)
            {
                Debug.LogError(LOG + "First person weapon asset is missing: " + ASSET_PATH);
                return false;
            }

            var sources = new List<string>();
            var targets = new List<string>();
            AddCopy(sources, targets, IWP_SOUNDS + DRAW_SOUND + ".ogg", SoundPath(DRAW_SOUND));
            AddCopy(sources, targets, IWP_SOUNDS + HOLSTER_SOUND + ".ogg", SoundPath(HOLSTER_SOUND));

            for (int i = 1; i <= AIM_SOUND_VARIANTS; i++)
            {
                foreach (string name in new[] { "aimin/aim_in_" + i, "aimout/aim_out_" + i })
                {
                    AddCopy(sources, targets, IWP_SOUNDS + name + ".ogg", SoundPath(name));
                }
            }

            if (!IwpWeaponSetup.CopyFromIwp(sources, targets))
            {
                return false;
            }

            Undo.RecordObject(asset, "Configure IWP weapon aim and sounds");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("_aimZoom").floatValue = 1.15f;

            foreach (string direction in new[] { "In", "Out" })
            {
                SerializedProperty sounds = serialized.FindProperty("_aim" + direction + "Sounds");
                sounds.arraySize = AIM_SOUND_VARIANTS;

                for (int i = 0; i < AIM_SOUND_VARIANTS; i++)
                {
                    sounds.GetArrayElementAtIndex(i).objectReferenceValue = LoadSound("aim" + direction.ToLowerInvariant() + "/aim_" + direction.ToLowerInvariant() + "_" + (i + 1));
                }
            }

            SerializedProperty motions = serialized.FindProperty("_motions");

            for (int i = 0; i < motions.arraySize; i++)
            {
                SerializedProperty motion = motions.GetArrayElementAtIndex(i);
                var kind = (FirstPersonWeaponMotionKind)motion.FindPropertyRelative("_kind").intValue;

                if (kind == FirstPersonWeaponMotionKind.Draw || kind == FirstPersonWeaponMotionKind.Holster)
                {
                    motion.FindPropertyRelative("_sound").objectReferenceValue = LoadSound(kind == FirstPersonWeaponMotionKind.Draw ? DRAW_SOUND : HOLSTER_SOUND);
                }
            }

            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(asset);
            Debug.Log(LOG + "Configured 1.15x aim zoom, 20 IWP aim sounds and IWP draw/holster sounds.");
            return true;
        }

        public static FirstPersonWeaponAsset Setup()
        {
            StalkerXRaySources sources = StalkerXRaySources.Open(GRA_SCOPE, out string error);

            if (sources == null)
            {
                Debug.LogError(LOG + error);
                return null;
            }

            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(sources.Index);

            if (system.HasSection(IwpWeaponSetup.SECTION) == false || system.HasSection(HUD_SECTION) == false)
            {
                Debug.LogError($"{LOG}GRA sections [{IwpWeaponSetup.SECTION}] / [{HUD_SECTION}] are missing.");
                return null;
            }

            var log = new StringBuilder();
            var copySources = new List<string>();
            var copyTargets = new List<string>();
            string a1MotionsPath = IwpWeaponSetup.MODELS_FOLDER + "/" + Path.GetFileName(A1_WEAPON_MOTIONS);
            AddCopy(copySources, copyTargets, A1_WEAPON_MOTIONS, a1MotionsPath);

            foreach (string effect in CAMERA_EFFECTS)
            {
                AddCopy(copySources, copyTargets, IWP_CAMERA + effect + ".anm", CAMERA_FOLDER + "/" + effect + ".anm");
            }

            foreach (string blend in BLENDS)
            {
                AddCopy(copySources, copyTargets, IWP_BLEND + blend + ".anm", BLEND_FOLDER + "/" + blend + ".anm");
            }

            var shotNames = new List<string>();

            for (int i = 0; i <= MAX_SHOT_VARIANTS; i++)
            {
                string name = "aug/aug_muzzle_stereo" + (i == 0 ? string.Empty : i.ToString());

                if (File.Exists(Path.Combine(IwpWeaponSetup.IWP_ROOT, IWP_SOUNDS + name + ".ogg")))
                {
                    shotNames.Add(name);
                }
            }

            string drawSound = DRAW_SOUND;
            string holsterSound = HOLSTER_SOUND;
            string emptySound = ResolveSound(Get(system, HUD_SECTION, "snd_empty"), "aug/aug_empty");
            var sounds = new List<string>(shotNames) { drawSound, holsterSound, emptySound, "aug/aug_reload", "aug/aug_a3_reload_empty", "aug/aug_reload_empty", "aug/aug_firemode", "aug/aug_misfire", "last_shell_out", "mag_shot" };

            foreach (string sound in sounds)
            {
                AddCopy(copySources, copyTargets, IWP_SOUNDS + sound + ".ogg", SoundPath(sound));
            }

            if (IwpWeaponSetup.CopyFromIwp(copySources, copyTargets) == false)
            {
                return null;
            }

            string hudModelPath = IwpWeaponSetup.MODELS_FOLDER + "/" + Path.GetFileName(IwpWeaponSetup.HUD_MODEL);
            IwpWeaponSetup.AssignRig(a1MotionsPath, hudModelPath);
            string flamePath = XRayParticleImporter.GroupPrefabPath(Get(system, IwpWeaponSetup.SECTION, "flame_particles"));
            string smokePath = XRayParticleImporter.GroupPrefabPath(Get(system, IwpWeaponSetup.SECTION, "smoke_particles"));

            if (AssetDatabase.LoadAssetAtPath<GameObject>(flamePath) == null || AssetDatabase.LoadAssetAtPath<GameObject>(smokePath) == null)
            {
                XRayParticleImporter.Import();
            }

            string hudVisualPath = IwpWeaponSetup.VISUALS_FOLDER + "/" + IwpWeaponSetup.SECTION + "_hud.prefab";
            ConfigureMuzzle(hudVisualPath, log);
            var hudVisual = AssetDatabase.LoadAssetAtPath<GameObject>(hudVisualPath);
            var handsMotions = AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(IwpWeaponSetup.HANDS_MOTIONS_FOLDER + "/" + Path.GetFileName(IwpWeaponSetup.HANDS_MOTIONS));
            var a3Motions = AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(IwpWeaponSetup.MODELS_FOLDER + "/" + Path.GetFileName(IwpWeaponSetup.WEAPON_MOTIONS));
            var a1Motions = AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(a1MotionsPath);

            if (hudVisual == null || handsMotions == null || a3Motions == null || a1Motions == null)
            {
                Debug.LogError($"{LOG}Run '{nameof(IwpWeaponSetup)}' first: hud visual={hudVisual != null}, hands={handsMotions != null}, a3={a3Motions != null}, a1={a1Motions != null}.");
                return null;
            }

            var motions = new List<FirstPersonWeaponMotion>();
            int missing = 0;
            missing += Add(motions, FirstPersonWeaponMotionKind.Draw, handsMotions, "draw", a3Motions, a1Motions, "draw", "aug_draw", drawSound, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.Holster, handsMotions, "holster", a3Motions, a1Motions, "idle", null, holsterSound, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.Idle, handsMotions, "idle", a3Motions, a1Motions, "idle", null, null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.IdleEmpty, handsMotions, "idle", a3Motions, a1Motions, "idle_empty", null, null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.IdleAim, handsMotions, "idle_aim", a3Motions, a1Motions, "idle", null, null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.Moving, handsMotions, "moving", a3Motions, a1Motions, "idle", null, null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.MovingAim, handsMotions, "moving_aim", a3Motions, a1Motions, "idle", null, null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.MovingSlow, handsMotions, "moving_slow", a3Motions, a1Motions, "idle", null, null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.MovingCrouch, handsMotions, "crouch", a3Motions, a1Motions, "idle", null, null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.SprintStart, handsMotions, "sprint_start", a3Motions, a1Motions, "sprint_start", null, null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.Sprint, handsMotions, "sprint", a3Motions, a1Motions, "sprint", null, null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.SprintEnd, handsMotions, "sprint_end", a3Motions, a1Motions, "sprint_end", null, null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.Shoot, handsMotions, "shoot", a3Motions, a1Motions, "shoot", "aug_shoot", null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.Shoot, handsMotions, "shoot1", a3Motions, a1Motions, "shoot", "aug_shoot1", null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.Shoot, handsMotions, "shoot2", a3Motions, a1Motions, "shoot", "aug_shoot2", null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.ShootAim, handsMotions, "shoot_aim", a3Motions, a1Motions, "shoot", "aug_shoot_aim", null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.ShootAim, handsMotions, "shoot_aim1", a3Motions, a1Motions, "shoot", "aug_shoot_aim1", null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.ShootAim, handsMotions, "shoot_aim2", a3Motions, a1Motions, "shoot", "aug_shoot_aim2", null, log);
            missing += Add(motions, FirstPersonWeaponMotionKind.Reload, handsMotions, "a3_reload", a3Motions, a1Motions, "reload", "aug_a3_reload", "aug/aug_reload", log);
            missing += Add(motions, FirstPersonWeaponMotionKind.ReloadEmpty, handsMotions, "a3_reload_empty", a3Motions, a1Motions, "reload_empty", "aug_a3_reload_empty", "aug/aug_a3_reload_empty", log);
            missing += Add(motions, FirstPersonWeaponMotionKind.FireMode, handsMotions, "firemode", a3Motions, a1Motions, "firemode", "aug_firemode", "aug/aug_firemode", log);
            missing += Add(motions, FirstPersonWeaponMotionKind.Misfire, handsMotions, "misfire", a3Motions, a1Motions, "misfire", "aug_misfire", "aug/aug_misfire", log);
            string[] overlayBones = FindLeftArm(handsMotions, log);
            FirstPersonWeaponAsset asset = LoadOrCreate();
            Vector3 handsPosition = Vector3.zero;
            Vector3 handsOrientation = Vector3.zero;
            Vector3 aimPosition = AIM_OFFSET;
            Vector3 aimRotation = Vector3.zero;
            float aimTime = GetFloat(system, HUD_SECTION, "zoom_rotate_time", 0.25f);
            asset.SetModel(hudVisual, handsPosition, handsOrientation, aimPosition, aimRotation, aimTime, WeaponHudComponent.SILENCER_BONE, overlayBones);
            asset.SetAnimations(motions, LoadBlend(BLENDS[0]), LoadBlend(BLENDS[1]), LoadBlend(BLENDS[2]));
            int rpm = Mathf.RoundToInt(GetFloat(system, IwpWeaponSetup.SECTION, "rpm", 600f));
            int emptyRpm = Mathf.RoundToInt(GetFloat(system, IwpWeaponSetup.SECTION, "rpm_empty_click", 200f));
            int magazine = Mathf.RoundToInt(GetFloat(system, IwpWeaponSetup.SECTION, "ammo_mag_size", 30f));
            asset.SetFire(rpm, emptyRpm, ParseFireModes(Get(system, IwpWeaponSetup.SECTION, "fire_modes")), magazine, AssetDatabase.LoadAssetAtPath<GameObject>(flamePath), AssetDatabase.LoadAssetAtPath<GameObject>(smokePath));
            var shots = new List<AudioClip>();

            foreach (string name in shotNames)
            {
                AudioClip clip = LoadSound(name);

                if (clip != null)
                {
                    shots.Add(clip);
                }
            }

            asset.SetSounds(shots.ToArray(), LoadSound(emptySound), LoadSound("last_shell_out"), LoadSound("mag_shot"), LOW_AMMO_THRESHOLD);
            EditorUtility.SetDirty(asset);
            bool assigned = AssignToDefinition(asset, log);
            bool configured = ConfigurePrefab(log);
            AssetDatabase.SaveAssets();
            SetupAimAndSounds();
            Debug.Log($"{LOG}{ASSET_PATH}: {motions.Count} motions, missing {missing}, rpm {rpm}, magazine {magazine}, shots {shots.Count}, flame '{flamePath}', smoke '{smokePath}', hands {handsPosition}/{handsOrientation}, aim {aimPosition}/{aimRotation}, overlay bones {overlayBones.Length}, definition {assigned}, prefab {configured}.\n{log}");
            return assigned && configured ? asset : null;
        }

        private static int Add(List<FirstPersonWeaponMotion> motions, FirstPersonWeaponMotionKind kind, XRayMotionLibrary hands, string handsName, XRayMotionLibrary primary, XRayMotionLibrary fallback, string weaponName, string cameraEffect, string sound, StringBuilder log)
        {
            string fullName = MOTION_PREFIX + handsName;

            if (hands.TryGetClip(fullName, out AnimationClip handsClip) == false)
            {
                log.AppendLine($"ERROR: hands motion '{fullName}' is missing.");
                return 1;
            }

            AnimationClip weaponClip = null;

            if (primary.TryGetClip(weaponName, out AnimationClip primaryClip))
            {
                weaponClip = primaryClip;
            }
            else if (fallback.TryGetClip(weaponName, out AnimationClip fallbackClip))
            {
                weaponClip = fallbackClip;
            }
            else
            {
                log.AppendLine($"Weapon motion '{weaponName}' is missing for '{fullName}'.");
            }

            XRayAnmAsset camera = string.IsNullOrEmpty(cameraEffect) ? null : AssetDatabase.LoadAssetAtPath<XRayAnmAsset>(CAMERA_FOLDER + "/" + cameraEffect + ".anm");

            if (string.IsNullOrEmpty(cameraEffect) == false && camera == null)
            {
                log.AppendLine($"Camera effect '{cameraEffect}' is missing.");
            }

            motions.Add(new FirstPersonWeaponMotion(kind, fullName, handsClip, weaponClip, camera, string.IsNullOrEmpty(sound) ? null : LoadSound(sound), 1.0f));
            return 0;
        }

        private static string[] FindLeftArm(XRayMotionLibrary hands, StringBuilder log)
        {
            var parts = new List<string>();

            foreach (XRayBonePart part in hands.BoneParts)
            {
                parts.Add($"{part.Name}({part.Bones.Count})");

                if (part.Name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 && part.Bones.Count > 0)
                {
                    log.AppendLine($"Overlay bone part '{part.Name}': {string.Join(", ", part.Bones)}.");
                    return ToArray(part.Bones);
                }
            }

            var bones = new List<string>();

            foreach (string bone in hands.BoneNames)
            {
                if (bone.StartsWith("l_", StringComparison.OrdinalIgnoreCase))
                {
                    bones.Add(bone);
                }
            }

            log.AppendLine($"No left bone part in [{string.Join(", ", parts)}]; overlay uses l_* bones: {string.Join(", ", bones)}.");
            return bones.ToArray();
        }

        private static bool AssignToDefinition(FirstPersonWeaponAsset asset, StringBuilder log)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(IwpWeaponSetup.DEFINITION_PATH);

            if (definition == null)
            {
                log.AppendLine($"ERROR: definition '{IwpWeaponSetup.DEFINITION_PATH}' is missing.");
                return false;
            }

            var serialized = new SerializedObject(definition);
            SerializedProperty components = serialized.FindProperty("_components");

            for (int i = 0; components != null && i < components.arraySize; i++)
            {
                SerializedProperty component = components.GetArrayElementAtIndex(i);

                if (component.managedReferenceValue is WeaponHudComponent)
                {
                    component.FindPropertyRelative("_firstPersonWeapon").objectReferenceValue = asset;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(definition);
                    return true;
                }
            }

            log.AppendLine($"ERROR: '{definition.name}' has no {nameof(WeaponHudComponent)}.");
            return false;
        }

        internal static void CreateMuzzle(GameObject root, string parentName, Vector3 localPosition, Quaternion localRotation)
        {
            Transform parent = root.transform;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == parentName)
                {
                    parent = child;
                    break;
                }
            }

            var muzzle = new GameObject(MUZZLE_POINT).transform;
            muzzle.SetParent(parent, false);
            muzzle.localPosition = localPosition;
            muzzle.localRotation = localRotation;
            FirstPersonWeaponView view = root.GetComponent<FirstPersonWeaponView>();

            if (view == null)
            {
                view = root.AddComponent<FirstPersonWeaponView>();
            }

            view.SetMuzzle(muzzle);
        }

        private static void ConfigureMuzzle(string path, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                FirstPersonWeaponView view = root.GetComponent<FirstPersonWeaponView>();

                if (view != null && view.Muzzle != null)
                {
                    log.AppendLine($"{path}: muzzle point '{view.Muzzle.name}' under '{view.Muzzle.parent.name}' is kept.");
                    return;
                }

                Transform bone = null;

                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == WeaponHudComponent.SILENCER_BONE)
                    {
                        bone = child;
                    }
                }

                Quaternion rotation = bone != null ? Quaternion.Inverse(bone.rotation) * root.transform.rotation : Quaternion.identity;
                CreateMuzzle(root, WeaponHudComponent.SILENCER_BONE, Vector3.zero, rotation);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                log.AppendLine($"{path}: muzzle point created under '{WeaponHudComponent.SILENCER_BONE}'.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool ConfigurePrefab(StringBuilder log)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(StalkerPlayerSetup.PREFAB_PATH);

            try
            {
                var player = root.GetComponent<StalkerPlayerController>();
                StalkerPlayerInput input = root.GetComponentInChildren<StalkerPlayerInput>(true);
                StalkerCameraController camera = root.GetComponentInChildren<StalkerCameraController>(true);

                if (player == null || input == null || camera == null || camera.CameraRoot == null)
                {
                    log.AppendLine("ERROR: FPS_Player.prefab must contain StalkerPlayerController, StalkerPlayerInput and StalkerCameraController with CameraRoot.");
                    return false;
                }

                StalkerCameraRecoil recoil = camera.CameraRoot.GetComponent<StalkerCameraRecoil>();

                if (recoil == null)
                {
                    recoil = camera.CameraRoot.gameObject.AddComponent<StalkerCameraRecoil>();
                }

                var cameraObject = new SerializedObject(camera);
                cameraObject.FindProperty("_recoil").objectReferenceValue = recoil;
                cameraObject.ApplyModifiedPropertiesWithoutUndo();
                AudioSource audio = GetAudio(root.transform, WEAPON_AUDIO);
                AudioSource shotAudio = GetAudio(root.transform, WEAPON_SHOT_AUDIO);
                FirstPersonWeaponController controller = root.GetComponent<FirstPersonWeaponController>();

                if (controller == null)
                {
                    controller = root.AddComponent<FirstPersonWeaponController>();
                }

                var controllerObject = new SerializedObject(controller);
                controllerObject.FindProperty("_player").objectReferenceValue = player;
                controllerObject.FindProperty("_input").objectReferenceValue = input;
                controllerObject.FindProperty("_audio").objectReferenceValue = audio;
                controllerObject.FindProperty("_shotAudio").objectReferenceValue = shotAudio;
                controllerObject.ApplyModifiedPropertiesWithoutUndo();
                ConfigureHud(root.transform, controller);
                PrefabUtility.SaveAsPrefabAsset(root, StalkerPlayerSetup.PREFAB_PATH);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureHud(Transform root, FirstPersonWeaponController controller)
        {
            Transform hud = root.Find(WEAPON_HUD);

            if (hud == null)
            {
                hud = new GameObject(WEAPON_HUD, typeof(RectTransform)).transform;
                hud.SetParent(root, false);
            }

            Canvas canvas = hud.GetComponent<Canvas>();

            if (canvas == null)
            {
                canvas = hud.gameObject.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = hud.GetComponent<CanvasScaler>();

            if (scaler == null)
            {
                scaler = hud.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var text = hud.Find(AMMO_TEXT) as RectTransform;

            if (text == null)
            {
                text = new GameObject(AMMO_TEXT, typeof(RectTransform)).GetComponent<RectTransform>();
                text.SetParent(hud, false);
            }

            text.anchorMin = new Vector2(1f, 0f);
            text.anchorMax = new Vector2(1f, 0f);
            text.pivot = new Vector2(1f, 0f);
            text.anchoredPosition = new Vector2(-48f, 36f);
            text.sizeDelta = new Vector2(320f, 48f);
            TextMeshProUGUI label = text.GetComponent<TextMeshProUGUI>();

            if (label == null)
            {
                label = text.gameObject.AddComponent<TextMeshProUGUI>();
            }

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GraInventoryWindowBuilder.FONT_PATH);

            if (font != null)
            {
                label.font = font;
            }

            label.fontSize = AMMO_FONT_SIZE;
            label.color = AMMO_COLOR;
            label.alignment = TextAlignmentOptions.BottomRight;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.text = string.Empty;
            text.gameObject.SetActive(false);
            FirstPersonWeaponAmmoView view = hud.GetComponent<FirstPersonWeaponAmmoView>();

            if (view == null)
            {
                view = hud.gameObject.AddComponent<FirstPersonWeaponAmmoView>();
            }

            var viewObject = new SerializedObject(view);
            viewObject.FindProperty("_controller").objectReferenceValue = controller;
            viewObject.FindProperty("_text").objectReferenceValue = label;
            viewObject.FindProperty("_root").objectReferenceValue = text.gameObject;
            viewObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static AudioSource GetAudio(Transform root, string name)
        {
            Transform target = root.Find(name);

            if (target == null)
            {
                target = new GameObject(name).transform;
                target.SetParent(root, false);
            }

            AudioSource audio = target.GetComponent<AudioSource>();

            if (audio == null)
            {
                audio = target.gameObject.AddComponent<AudioSource>();
            }

            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0.0f;
            audio.dopplerLevel = 0.0f;
            return audio;
        }

        private static FirstPersonWeaponAsset LoadOrCreate()
        {
            var asset = AssetDatabase.LoadAssetAtPath<FirstPersonWeaponAsset>(ASSET_PATH);

            if (asset != null)
            {
                return asset;
            }

            StalkerXRaySources.EnsureFolder(FIRST_PERSON_FOLDER);
            asset = ScriptableObject.CreateInstance<FirstPersonWeaponAsset>();
            AssetDatabase.CreateAsset(asset, ASSET_PATH);
            return asset;
        }

        private static void AddCopy(List<string> sources, List<string> targets, string source, string target)
        {
            if (targets.Contains(target) == false)
            {
                sources.Add(source);
                targets.Add(target);
            }
        }

        private static string ResolveSound(string graValue, string fallback)
        {
            string value = FirstValue(graValue);

            if (string.IsNullOrEmpty(value) == false)
            {
                string relative = value.Replace('\\', '/');
                string prefix = "weapons/";

                if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    relative = relative.Substring(prefix.Length);
                }

                if (File.Exists(Path.Combine(IwpWeaponSetup.IWP_ROOT, IWP_SOUNDS + relative + ".ogg")))
                {
                    return relative;
                }

                string flat = Path.GetFileName(relative);

                if (File.Exists(Path.Combine(IwpWeaponSetup.IWP_ROOT, IWP_SOUNDS + flat + ".ogg")))
                {
                    return flat;
                }
            }

            return fallback;
        }

        private static string SoundPath(string name)
        {
            return SOUNDS_FOLDER + "/" + name + ".ogg";
        }

        private static AudioClip LoadSound(string name)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>(SoundPath(name));
        }

        private static XRayAnmAsset LoadBlend(string name)
        {
            return AssetDatabase.LoadAssetAtPath<XRayAnmAsset>(BLEND_FOLDER + "/" + name + ".anm");
        }

        private static int[] ParseFireModes(string value)
        {
            var modes = new List<int>();

            foreach (string part in (value ?? string.Empty).Split(','))
            {
                if (StalkerXRaySources.TryFloat(part.Trim(), out float mode))
                {
                    modes.Add(Mathf.RoundToInt(mode));
                }
            }

            return modes.Count > 0 ? modes.ToArray() : new[] { 1 };
        }

        private static string Get(XRayLtxDatabase system, string section, string key)
        {
            string value = XRayLtxDatabase.Unquote(system.GetValue(section, key));
            return value != null ? value.Trim() : null;
        }

        private static string FirstValue(string value)
        {
            return string.IsNullOrEmpty(value) ? value : value.Split(',')[0].Trim();
        }

        private static float GetFloat(XRayLtxDatabase system, string section, string key, float fallback)
        {
            string value = FirstValue(Get(system, section, key));
            return value != null && StalkerXRaySources.TryFloat(value.TrimEnd('f'), out float result) ? result : fallback;
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var result = new string[values.Count];

            for (int i = 0; i < values.Count; i++)
            {
                result[i] = values[i];
            }

            return result;
        }
    }
}
