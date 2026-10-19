using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Inventory;
using Inventory.Editor;
using Inventory.IconGeneration;
using Inventory.UI;
using RaidIntoDarkness.Player;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class InventoryIntegrationSetup
    {
        public const string SCENE_PATH = "Assets/OutdoorsScene.unity";
        public const string PREFAB_FOLDER = "Assets/_Source/UI/Inventory";
        public const string PREFAB_PATH = PREFAB_FOLDER + "/InventoryWindow.prefab";
        public const string INPUT_PATH = "Assets/_Source/Settings/Input/StalkerControls.inputactions";
        public const string CANVAS_NAME = "InventoryCanvas";
        public const string INTEGRATION_NAME = "InventoryIntegration";
        public const string TMP_PACKAGE = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

        private const string MENU = "Tools/Inventory/Setup OutdoorsScene Inventory";

        [MenuItem(MENU)]
        public static void SetupFromMenu()
        {
            RunAsync(true).Forget(Debug.LogException);
        }

        public static void SetupBatch()
        {
            RunBatchAsync().Forget();
        }

        public static async UniTask<bool> RunAsync(bool bakeIcons)
        {
            ImportTmpEssentials();
            InventoryDemoContentBuilder.DemoContent content = InventoryDemoContentBuilder.Build();
            GameObject prefab = BuildWindowPrefab(content);
            bool sceneSaved = SetupScene(prefab);
            Debug.Log($"[Inventory] Demo content: {content.Items.Count} definitions, window prefab {PREFAB_PATH}, scene saved: {sceneSaved}.");

            if (bakeIcons)
            {
                int baked = await ItemIconBatchBake.BakeAllAsync(true, CancellationToken.None);
                Debug.Log($"[Inventory] Baked {baked} icon variant(s) into {ItemIconBatchBake.CatalogAssetPath}.");
            }

            return sceneSaved;
        }

        private static async UniTaskVoid RunBatchAsync()
        {
            bool success = false;

            try
            {
                success = await RunAsync(true);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Inventory] Setup failed: " + exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        private static void ImportTmpEssentials()
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0)
            {
                return;
            }

            string package = Path.GetFullPath(TMP_PACKAGE);

            if (File.Exists(package) == false)
            {
                Debug.LogWarning("[Inventory] TMP Essential Resources package was not found: " + package);
                return;
            }

            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static GameObject BuildWindowPrefab(InventoryDemoContentBuilder.DemoContent content)
        {
            InventoryDemoContentBuilder.EnsureFolder(PREFAB_FOLDER);
            TMP_FontAsset font = GraInventoryWindowBuilder.ImportAssets();
            ApplyDefaultFont(font);
            InventoryTheme theme = GraInventoryWindowBuilder.BuildTheme(font);
            var root = new GameObject("InventoryWindow", typeof(RectTransform));

            try
            {
                GraInventoryWindowBuilder.Result result = GraInventoryWindowBuilder.Build(root, theme, font, "Тайник");
                result.Window.ConfigureContainers(InventoryDemoContentBuilder.BACKPACK, InventoryDemoContentBuilder.STASH, InventoryDemoContentBuilder.EQUIPMENT, InventoryDemoContentBuilder.OWNER, 45f);
                result.Window.Input.Set(FindReference("Inventory", "RotateItem"), FindReference("Inventory", "QuickTransferModifier"));
                return PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ApplyDefaultFont(TMP_FontAsset font)
        {
            if (font == null)
            {
                return;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:TMP_Settings"))
            {
                var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(AssetDatabase.GUIDToAssetPath(guid));

                if (settings == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(settings);
                serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }

            AssetDatabase.SaveAssets();
        }

        private static bool SetupScene(GameObject windowPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            GameObject canvasObject = FindRoot(scene, CANVAS_NAME);

            if (canvasObject == null)
            {
                canvasObject = new GameObject(CANVAS_NAME, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
            }

            canvasObject.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            InventoryWindow window = canvasObject.GetComponentInChildren<InventoryWindow>(true);

            if (window == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(windowPrefab, canvasObject.transform);
                window = instance.GetComponent<InventoryWindow>();
            }

            SetupEventSystem(scene);
            GameObject integration = FindRoot(scene, INTEGRATION_NAME);

            if (integration == null)
            {
                integration = new GameObject(INTEGRATION_NAME);
                SceneManager.MoveGameObjectToScene(integration, scene);
            }

            PlayerInventoryRuntime runtime = GetOrAdd<PlayerInventoryRuntime>(integration);
            var runtimeObject = new SerializedObject(runtime);
            runtimeObject.FindProperty("_database").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ItemDatabase>(InventoryDemoContentBuilder.DATABASE_PATH);
            runtimeObject.FindProperty("_seed").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InventorySeed>(InventoryDemoContentBuilder.SEED_PATH);
            runtimeObject.ApplyModifiedPropertiesWithoutUndo();
            InventoryWindowController controller = GetOrAdd<InventoryWindowController>(integration);
            StalkerPlayerController player = FindInScene<StalkerPlayerController>(scene);

            if (player == null)
            {
                Debug.LogError("[Inventory] OutdoorsScene has no StalkerPlayerController.");
            }

            controller.Configure(window, runtime, player, FindReference("Inventory", "ToggleInventory"), FindReference("Inventory", "CloseInventory"));
            controller.ConfigurePresentation(FindReference("Inventory", "ToggleStash"), FindHeader(window), window.GetComponentInChildren<InventoryStatusPanel>(true), "Сталкер", "Одиночки", 5000, GraInventoryWindowBuilder.LoadSprite(GraInventoryWindowBuilder.PORTRAIT_SPRITE), GraInventoryWindowBuilder.LoadSprite(GraInventoryWindowBuilder.RANK_SPRITE));
            controller.ConfigureSorting(FindReference("Inventory", "ToggleFavorite"), FindReference("Inventory", "ToggleJunk"), GraInventoryWindowBuilder.LoadSprite(GraInventoryWindowBuilder.FAVORITE_SPRITE), GraInventoryWindowBuilder.LoadSprite(GraInventoryWindowBuilder.JUNK_SPRITE));
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            return EditorSceneManager.SaveScene(scene);
        }

        private static InventoryCharacterHeader FindHeader(InventoryWindow window)
        {
            foreach (InventoryCharacterHeader header in window.GetComponentsInChildren<InventoryCharacterHeader>(true))
            {
                if (header.transform.name == "PlayerPanel")
                {
                    return header;
                }
            }

            return null;
        }

        private static void SetupEventSystem(Scene scene)
        {
            EventSystem eventSystem = FindInScene<EventSystem>(scene);

            if (eventSystem == null)
            {
                var eventObject = new GameObject("EventSystem", typeof(EventSystem));
                SceneManager.MoveGameObjectToScene(eventObject, scene);
                eventSystem = eventObject.GetComponent<EventSystem>();
            }

            StandaloneInputModule legacy = eventSystem.GetComponent<StandaloneInputModule>();

            if (legacy != null)
            {
                UnityEngine.Object.DestroyImmediate(legacy);
            }

            InputSystemUIInputModule module = GetOrAdd<InputSystemUIInputModule>(eventSystem.gameObject);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(INPUT_PATH);
            module.actionsAsset = actions;
            module.point = FindReference("UI", "Point");
            module.leftClick = FindReference("UI", "Click");
            module.rightClick = FindReference("UI", "RightClick");
            module.middleClick = FindReference("UI", "MiddleClick");
            module.scrollWheel = FindReference("UI", "ScrollWheel");
            module.move = null;
            module.submit = null;
            module.cancel = null;
            module.trackedDevicePosition = null;
            module.trackedDeviceOrientation = null;
            EditorUtility.SetDirty(module);
        }

        public static InputActionReference FindReference(string mapName, string actionName)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(INPUT_PATH))
            {
                if (asset is InputActionReference reference && reference.action != null && reference.action.name == actionName && reference.action.actionMap != null && reference.action.actionMap.name == mapName)
                {
                    return reference;
                }
            }

            Debug.LogError($"[Inventory] Input action {mapName}/{actionName} was not found in {INPUT_PATH}.");
            return null;
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

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);

                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
