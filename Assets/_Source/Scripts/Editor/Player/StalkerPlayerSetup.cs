using System;
using RaidIntoDarkness.FirstPerson;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Editor
{
    public static class StalkerPlayerSetup
    {
        public const string PREFAB_PATH = "Assets/_Source/FirstPerson/FPS_Player.prefab";
        public const string SCENE_PATH = "Assets/OutdoorsScene.unity";
        public const string INPUT_PATH = "Assets/_Source/Settings/Input/StalkerControls.inputactions";
        public const string TEST_AREA = "PlayerTestArea";

        public const string CAMERA_PIVOT = "CameraMotionPivot";
        public const string FOLEY_SOURCE = "FoleyAudio";

        private const string MENU = "Tools/X-Ray/Setup Stalker Player";
        private const string PLAYER_NAME = "FPS_Player";
        private const string SCENE_CAMERA = "Main Camera";
        private const string MAIN_CAMERA_TAG = "MainCamera";

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
                Debug.LogError("[Player] Setup failed: " + exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        public static bool Setup()
        {
            StalkerMovementProfile profile = AssetDatabase.LoadAssetAtPath<StalkerMovementProfile>(StalkerMovementProfileBuilder.PROFILE_PATH);
            if (profile == null)
            {
                profile = StalkerMovementProfileBuilder.Build(out string report);
                Debug.Log("[Player] " + report);
            }

            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(INPUT_PATH);
            if (profile == null || input == null)
            {
                Debug.LogError($"[Player] Missing profile or input actions ({StalkerMovementProfileBuilder.PROFILE_PATH}, {INPUT_PATH}).");
                return false;
            }

            var cameraEffects = AssetDatabase.LoadAssetAtPath<StalkerCameraEffectsProfile>(StalkerCameraEffectsBuilder.PROFILE_PATH);
            var audio = AssetDatabase.LoadAssetAtPath<StalkerSurfaceAudioLibrary>(StalkerSurfaceAudioBuilder.LIBRARY_PATH);
            var timing = AssetDatabase.LoadAssetAtPath<StalkerFootstepTimingLibrary>(StalkerFootstepTimingBuilder.TIMING_PATH);
            if (cameraEffects == null || audio == null || timing == null)
            {
                bool built = StalkerFoleyAssets.Build(out string report);
                Debug.Log("[Player] " + report);
                cameraEffects = AssetDatabase.LoadAssetAtPath<StalkerCameraEffectsProfile>(StalkerCameraEffectsBuilder.PROFILE_PATH);
                audio = AssetDatabase.LoadAssetAtPath<StalkerSurfaceAudioLibrary>(StalkerSurfaceAudioBuilder.LIBRARY_PATH);
                timing = AssetDatabase.LoadAssetAtPath<StalkerFootstepTimingLibrary>(StalkerFootstepTimingBuilder.TIMING_PATH);
                if (!built)
                {
                    Debug.LogWarning("[Player] Foley assets were not fully built; missing references stay empty.");
                }
            }

            SetupPrefab(profile, input, cameraEffects, audio, timing);
            return SetupScene(audio);
        }

        private static void SetupFoley(GameObject root, Transform cameraRoot, Camera camera, StalkerMovementProfile profile, StalkerCameraEffectsProfile cameraEffects, StalkerSurfaceAudioLibrary audio, StalkerFootstepTimingLibrary timing, StalkerPlayerController player)
        {
            Transform pivot = cameraRoot.Find(CAMERA_PIVOT);
            if (pivot == null)
            {
                pivot = new GameObject(CAMERA_PIVOT).transform;
                pivot.SetParent(cameraRoot, false);
            }

            pivot.localPosition = Vector3.zero;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;
            if (camera.transform.parent != pivot)
            {
                camera.transform.SetParent(pivot, false);
            }

            Transform foleyTransform = root.transform.Find(FOLEY_SOURCE);
            if (foleyTransform == null)
            {
                foleyTransform = new GameObject(FOLEY_SOURCE).transform;
                foleyTransform.SetParent(root.transform, false);
            }

            foleyTransform.localPosition = Vector3.zero;
            var source = GetOrAdd<AudioSource>(foleyTransform.gameObject);
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0.0f;
            source.dopplerLevel = 0.0f;
            source.priority = 64;
            var effects = GetOrAdd<StalkerCameraEffects>(root);
            var detector = GetOrAdd<StalkerSurfaceDetector>(root);
            var footsteps = GetOrAdd<StalkerFootstepSynchronizer>(root);
            var foley = GetOrAdd<StalkerPlayerFoley>(root);
            Set(effects, "_profile", cameraEffects);
            Set(effects, "_pivot", pivot);
            Set(detector, "_library", audio);
            var detectorObject = new SerializedObject(detector);
            detectorObject.FindProperty("_mask").intValue = profile.Unity.CollisionMask;
            detectorObject.ApplyModifiedPropertiesWithoutUndo();
            Set(footsteps, "_library", timing);
            Set(foley, "_library", audio);
            Set(foley, "_detector", detector);
            Set(foley, "_source", source);
            Set(player, "_cameraEffects", effects);
            Set(player, "_footsteps", footsteps);
            Set(player, "_foley", foley);
            Set(player, "_surfaceDetector", detector);
        }

        private static void SetupPrefab(StalkerMovementProfile profile, InputActionAsset input, StalkerCameraEffectsProfile cameraEffects, StalkerSurfaceAudioLibrary audio, StalkerFootstepTimingLibrary timing)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            try
            {
                Transform cameraRoot = root.transform.Find("CameraRoot");
                Transform bodyRoot = root.transform.Find("BodyRoot");
                Camera camera = cameraRoot != null ? cameraRoot.GetComponentInChildren<Camera>(true) : null;
                var rig = root.GetComponent<FirstPersonRig>();
                if (cameraRoot == null || bodyRoot == null || camera == null || rig == null)
                {
                    throw new InvalidOperationException("FPS_Player.prefab must contain FirstPersonRig, CameraRoot/Camera and BodyRoot.");
                }

                var controller = GetOrAdd<CharacterController>(root);
                var stance = GetOrAdd<StalkerStanceController>(root);
                var condition = GetOrAdd<StalkerActorCondition>(root);
                var motor = GetOrAdd<StalkerMovementMotor>(root);
                var playerInput = GetOrAdd<StalkerPlayerInput>(root);
                var view = GetOrAdd<StalkerCameraController>(root);
                var animationPlayer = GetOrAdd<StalkerBodyAnimationPlayer>(root);
                var body = GetOrAdd<StalkerVisibleBody>(root);
                var player = GetOrAdd<StalkerPlayerController>(root);
                var overlay = GetOrAdd<StalkerPlayerDebugOverlay>(root);
                stance.Initialize(profile);
                controller.skinWidth = profile.Unity.SkinWidth;
                controller.slopeLimit = profile.Unity.SlopeLimit;
                controller.minMoveDistance = 0.0f;
                Set(stance, "_profile", profile);
                Set(condition, "_profile", profile);
                Set(motor, "_profile", profile);
                Set(playerInput, "_actions", input);
                Set(view, "_cameraRoot", cameraRoot);
                Set(view, "_camera", camera);
                Set(view, "_hudRoot", cameraRoot.Find("HandsRoot"));
                Set(body, "_bodyRoot", bodyRoot);
                Set(body, "_player", animationPlayer);
                Set(player, "_profile", profile);
                Set(player, "_motor", motor);
                Set(player, "_input", playerInput);
                Set(player, "_camera", view);
                Set(player, "_visibleBody", body);
                Set(player, "_bodyPlayer", animationPlayer);
                Set(player, "_rig", rig);
                Set(overlay, "_controller", player);
                Set(overlay, "_input", playerInput);
                var rigObject = new SerializedObject(rig);
                rigObject.FindProperty("_driveBodyAnimation").boolValue = false;
                rigObject.FindProperty("_initialOutfit").stringValue = FirstPersonOutfitCatalog.NO_OUTFIT_SECTION;
                rigObject.ApplyModifiedPropertiesWithoutUndo();
                cameraRoot.localPosition = new Vector3(0.0f, stance.TargetCameraHeight, 0.0f);
                camera.tag = MAIN_CAMERA_TAG;
                camera.fieldOfView = profile.Camera.FieldOfView;
                camera.nearClipPlane = profile.Camera.NearClip;
                GetOrAdd<AudioListener>(camera.gameObject);
                SetupFoley(root, cameraRoot, camera, profile, cameraEffects, audio, timing, player);
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool SetupScene(StalkerSurfaceAudioLibrary audio)
        {
            Scene scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            GameObject sceneCamera = FindRoot(scene, SCENE_CAMERA);
            if (sceneCamera != null && sceneCamera.activeSelf)
            {
                sceneCamera.SetActive(false);
            }

            GameObject area = FindRoot(scene, TEST_AREA);
            if (area == null)
            {
                area = StalkerTestAreaBuilder.Build(!HasGround(scene, StalkerTestAreaBuilder.SpawnPosition));
                SceneManager.MoveGameObjectToScene(area, scene);
            }

            StalkerSurfaceTestAreaBuilder.Build(area.transform, audio);
            GameObject player = FindRoot(scene, PLAYER_NAME);
            if (player == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
                player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                player.transform.SetPositionAndRotation(StalkerTestAreaBuilder.SpawnPosition, Quaternion.identity);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return EditorSceneManager.SaveScene(scene);
        }

        private static bool HasGround(Scene scene, Vector3 spawn)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Collider collider in root.GetComponentsInChildren<Collider>())
                {
                    Bounds bounds = collider.bounds;
                    Renderer renderer = collider.GetComponent<Renderer>();
                    if (bounds.size == Vector3.zero && renderer != null)
                    {
                        bounds = renderer.bounds;
                    }

                    if (!collider.isTrigger && collider.enabled && spawn.x >= bounds.min.x && spawn.x <= bounds.max.x && spawn.z >= bounds.min.z && spawn.z <= bounds.max.z && Mathf.Abs(bounds.max.y - spawn.y) < 0.5f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            return null;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void Set(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
