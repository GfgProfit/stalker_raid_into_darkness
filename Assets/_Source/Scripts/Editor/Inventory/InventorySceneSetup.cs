using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.Editor;
using RaidIntoDarkness.Inventory.IconGeneration;
using RaidIntoDarkness.Inventory.UI;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Weapons;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.Editor
{
    public static class InventorySceneSetup
    {
        public const string SCENE_PATH = "Assets/TEST_SCENE.unity";
        public const string WINDOW_NAME = "InventoryWindow";
        public const string STASH_OWNER = "World";
        public const string STASH_TITLE = "Тайник";
        public const string INPUT_PATH = "Assets/_Source/Settings/Input/StalkerControls.inputactions";
        public const string CANVAS_NAME = "InventoryCanvas";
        public const string INTEGRATION_NAME = "InventoryIntegration";
        public const string TMP_PACKAGE = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

        private const string MENU = "Tools/Inventory/Setup Test Scene Inventory";

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
            Scene scene = RequireTargetScene();
            ImportTmpEssentials();
            InventoryDemoContent content = InventoryDemoContentBuilder.Build();
            TMP_FontAsset font = GraInventoryWindowBuilder.ImportAssets();
            ApplyDefaultFont(font);
            InventoryTheme theme = GraInventoryWindowBuilder.BuildTheme(font);
            List<WorldItem> worldItems = FindRoot(scene, WorldItemPrefabBuilder.WORLD_ITEMS_ROOT) == null ? WorldItemPrefabBuilder.Build(content.Items.Values) : new List<WorldItem>();
            bool sceneSaved = SetupScene(scene, font, theme, worldItems);
            Debug.Log($"[Inventory] Configured {content.Items.Count} definitions, placed {worldItems.Count} new world items, scene saved: {sceneSaved}.");

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

            UnityEditor.AssetPackage.Package.Import(package, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static InventoryWindow BuildWindow(Transform canvas, TMP_FontAsset font, InventoryTheme theme)
        {
            var root = new GameObject(WINDOW_NAME, typeof(RectTransform));
            root.layer = canvas.gameObject.layer;
            root.transform.SetParent(canvas, false);
            InventoryWindow window = GraInventoryWindowBuilder.Build(root, theme, font, "Тайник");
            Undo.RegisterCreatedObjectUndo(root, "Create inventory window");
            window.ConfigureContainers(InventoryDemoContentBuilder.BACKPACK, InventoryDemoContentBuilder.STASH, InventoryDemoContentBuilder.EQUIPMENT, InventoryDemoContentBuilder.OWNER, 45f);
            window.Input.Set(FindReference("Inventory", "RotateItem"), FindReference("Inventory", "QuickTransferModifier"));
            return window;
        }

        private static void BindSceneGrids(InventoryWindow window)
        {
            var grids = new List<InventoryGridViewBase>();
            Transform stashPanel = FindDescendant(window.transform, GraInventoryWindowBuilder.STASH_PANEL);
            Vector2 cellSize = new Vector2(GraInventoryWindowBuilder.CELL, GraInventoryWindowBuilder.CELL);

            foreach (InventoryGridView grid in window.GetComponentsInChildren<InventoryGridView>(true))
            {
                bool stash = stashPanel != null && grid.transform.IsChildOf(stashPanel);
                string containerId = stash ? InventoryDemoContentBuilder.STASH : InventoryDemoContentBuilder.BACKPACK;
                cellSize = grid.CellSize;

                if (grid.ContainerId != containerId)
                {
                    Undo.RecordObject(grid, "Configure inventory grid");
                    grid.Configure(containerId, stash ? STASH_OWNER : InventoryDemoContentBuilder.OWNER, stash ? STASH_TITLE : grid.DisplayName, grid.CellSize);
                }

                grids.Add(grid);
            }

            InventoryGridViewBase equipment = GraInventoryWindowBuilder.BindEquipmentPanel(window, cellSize);

            if (equipment != null && grids.Contains(equipment) == false)
            {
                grids.Insert(0, equipment);
            }

            var serialized = new SerializedObject(window);
            SerializedProperty list = serialized.FindProperty("_grids");
            list.arraySize = grids.Count;

            for (int i = 0; i < grids.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = grids[i];
            }

            SerializedProperty secondaryRoot = serialized.FindProperty("_secondaryRoot");

            if (secondaryRoot.objectReferenceValue == null && stashPanel != null)
            {
                secondaryRoot.objectReferenceValue = stashPanel;
            }

            serialized.ApplyModifiedProperties();
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
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
                SerializedProperty defaultFont = serialized.FindProperty("m_defaultFontAsset");

                if (defaultFont.objectReferenceValue != null)
                {
                    continue;
                }

                defaultFont.objectReferenceValue = font;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }

            AssetDatabase.SaveAssets();
        }

        internal static Scene RequireTargetScene()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (Application.isBatchMode && scene.path != SCENE_PATH && scene.isDirty == false)
            {
                scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SCENE_PATH || scene.isDirty)
            {
                throw new InvalidOperationException($"Open the saved scene '{SCENE_PATH}' in Edit Mode before configuring inventory content.");
            }

            return scene;
        }

        private static bool SetupScene(Scene scene, TMP_FontAsset font, InventoryTheme theme, IReadOnlyList<WorldItem> worldItems)
        {
            StalkerPlayerController player = FindInScene<StalkerPlayerController>(scene);

            if (player == null)
            {
                Debug.LogError($"[Inventory] Scene '{scene.path}' has no StalkerPlayerController.");
                return false;
            }

            GameObject canvasObject = FindRoot(scene, CANVAS_NAME);

            if (canvasObject == null)
            {
                canvasObject = new GameObject(CANVAS_NAME, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                Undo.RegisterCreatedObjectUndo(canvasObject, "Create inventory canvas");
            }

            Undo.RecordObject(canvasObject, "Configure inventory canvas object");
            canvasObject.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasObject.GetComponent<Canvas>();
            Undo.RecordObject(canvas, "Configure inventory canvas");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            Undo.RecordObject(scaler, "Configure inventory canvas scale");
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            InventoryWindow window = canvasObject.GetComponentInChildren<InventoryWindow>(true);

            if (window == null)
            {
                window = BuildWindow(canvasObject.transform, font, theme);
            }

            BindSceneGrids(window);

            SetupEventSystem(scene);
            GameObject integration = FindRoot(scene, INTEGRATION_NAME);

            if (integration == null)
            {
                integration = new GameObject(INTEGRATION_NAME);
                SceneManager.MoveGameObjectToScene(integration, scene);
                Undo.RegisterCreatedObjectUndo(integration, "Create inventory scene objects");
            }

            PlayerInventoryRuntime runtime = GetOrAdd<PlayerInventoryRuntime>(integration);
            var runtimeObject = new SerializedObject(runtime);
            runtimeObject.FindProperty("_database").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ItemDatabase>(InventoryDemoContentBuilder.DATABASE_PATH);
            runtimeObject.FindProperty("_seed").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InventorySeed>(InventoryDemoContentBuilder.SEED_PATH);
            runtimeObject.ApplyModifiedProperties();
            InventoryWindowController controller = GetOrAdd<InventoryWindowController>(integration);
            WorldItemInteractor interactor = GetOrAdd<WorldItemInteractor>(integration);
            ConfigureRuntime(window, runtime, player, interactor);
            Undo.RecordObject(controller, "Configure inventory window controller");
            controller.Configure(window, runtime, player, FindReference("Inventory", "ToggleInventory"), FindReference("Inventory", "CloseInventory"), player.GetComponentInChildren<RaidIntoDarkness.UI.SpawnMenuView>(true));
            controller.ConfigurePresentation(FindReference("Inventory", "ToggleStash"), FindHeader(window), window.GetComponentInChildren<InventoryStatusPanel>(true), "Сталкер", "Одиночки", 5000, GraInventoryWindowBuilder.LoadSprite(GraInventoryWindowBuilder.PORTRAIT_SPRITE), GraInventoryWindowBuilder.LoadSprite(GraInventoryWindowBuilder.RANK_SPRITE));
            controller.ConfigureSorting(FindReference("Inventory", "ToggleFavorite"), FindReference("Inventory", "ToggleJunk"), GraInventoryWindowBuilder.LoadSprite(GraInventoryWindowBuilder.FAVORITE_SPRITE), GraInventoryWindowBuilder.LoadSprite(GraInventoryWindowBuilder.JUNK_SPRITE));
            controller.ConfigureStats(FindReference("Inventory", "FullStatsModifier"));
            Undo.RecordObject(interactor, "Configure world item interactor");
            interactor.Configure(controller, FindReference("Inventory", "PickUp"));

            if (worldItems.Count > 0)
            {
                WorldItemPrefabBuilder.PlaceInScene(scene, worldItems, player.transform);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureRuntime(InventoryWindow window, PlayerInventoryRuntime runtime, StalkerPlayerController player, WorldItemInteractor interactor)
        {
            InventoryGridView bagView = window.Grids.OfType<InventoryGridView>().FirstOrDefault(grid => grid.ContainerId == window.PrimaryContainerId);
            InventoryGridView stashView = window.Grids.OfType<InventoryGridView>().FirstOrDefault(grid => grid.ContainerId == window.SecondaryContainerId);
            InventoryGridViewBase equipment = window.Grids.FirstOrDefault(grid => grid.ContainerId == window.EquipmentContainerId);
            InventorySlotLayout layout = equipment switch
            {
                InventoryEquipmentPanel panel => panel.Layout,
                InventorySlotGridView slotGrid => slotGrid.Layout,
                _ => null
            };

            if (bagView == null || stashView == null || layout == null)
            {
                throw new InvalidOperationException("The inventory window requires a backpack grid, stash grid and equipment layout.");
            }

            var backpack = (GridContainer)bagView.CreateContainer();
            var stash = (GridContainer)stashView.CreateContainer();
            SlotId[] equipmentSlots = equipment is InventoryEquipmentPanel equipmentPanel ? equipmentPanel.SlotViews.Select(slot => slot.SlotId).ToArray() : Array.Empty<SlotId>();
            var backpackSettings = new InventoryGridSettings(backpack.Id, backpack.OwnerId, backpack.DisplayName, backpack.Width, backpack.Height, 2);
            var stashSettings = new InventoryGridSettings(stash.Id, stash.OwnerId, stash.DisplayName, stash.Width, stash.Height, 0);
            Undo.RecordObject(runtime, "Configure inventory containers");
            runtime.Configure(backpackSettings, stashSettings, equipment.ContainerId, equipment.DisplayName, layout, equipmentSlots, player, player.GetComponentInChildren<FirstPersonWeaponController>(true), interactor);
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
                Undo.RegisterCreatedObjectUndo(eventObject, "Create inventory event system");
                eventSystem = eventObject.GetComponent<EventSystem>();
            }

            StandaloneInputModule legacy = eventSystem.GetComponent<StandaloneInputModule>();

            if (legacy != null)
            {
                Undo.DestroyObjectImmediate(legacy);
            }

            InputSystemUIInputModule module = GetOrAdd<InputSystemUIInputModule>(eventSystem.gameObject);
            Undo.RecordObject(module, "Configure inventory input module");
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
            return component != null ? component : Undo.AddComponent<T>(target);
        }
    }
}
