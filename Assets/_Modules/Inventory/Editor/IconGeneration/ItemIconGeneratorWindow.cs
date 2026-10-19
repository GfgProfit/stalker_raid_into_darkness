using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Inventory.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace Inventory.Editor
{
    public sealed class ItemIconGeneratorWindow : EditorWindow
    {
        private const int LARGE_BAKE_CONFIRMATION_THRESHOLD = 2000;
        private const float DEFAULT_VARIANT_COUNT_HEIGHT = 82f;
        private const float MIN_VARIANT_COUNT_HEIGHT = 32f;
        private const float MAX_VARIANT_COUNT_HEIGHT = 260f;
        private const float VARIANT_COUNT_RESIZE_HANDLE_HEIGHT = 6f;

        private static GUIStyle _centeredLabelStyle;

        private ItemIconGeneratorSettings _settings;
        private SerializedObject _settingsObject;
        private ItemDefinition _previewItem;
        private SerializedObject _previewItemObject;
        private Texture2D _previewTexture;
        private Vector2 _settingsScroll;
        private Vector2 _previewScroll;
        private bool _autoRefresh = true;
        private bool _incrementalBake = true;
        private bool _showItemSettings = true;
        private bool _previewRenderScheduled;
        private CancellationTokenSource _previewRenderCancellation;
        private CancellationTokenSource _bakeCancellation;
        private ItemIconVariantAnalysis _allVariantAnalysis;
        private ItemIconVariantAnalysis _previewVariantAnalysis;
        private string[] _previewVariantLabels = Array.Empty<string>();
        private int _selectedVariantIndex;
        private string[] _categories = Array.Empty<string>();
        private int _selectedCategoryMask = -1;
        private bool _isBaking;
        private string _bakeStatus = "Analysis not refreshed";
        private Vector2 _variantCountScroll;
        private float _variantCountHeight = DEFAULT_VARIANT_COUNT_HEIGHT;
        private bool _isResizingVariantCount;

        public bool IsBaking => _isBaking;
        public Texture2D PreviewTexture => _previewTexture;
        public string BakeStatus => _bakeStatus;

        private static GUIStyle CenteredLabelStyle => _centeredLabelStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };

        [MenuItem("Tools/Inventory/Item Icon Generator")]
        public static ItemIconGeneratorWindow Open()
        {
            var window = GetWindow<ItemIconGeneratorWindow>();
            window.titleContent = new GUIContent("Item Icon Generator");
            window.minSize = new Vector2(820f, 520f);
            window.Show();
            return window;
        }

        public void SetPreviewItem(ItemDefinition definition)
        {
            _previewItem = definition;
            _previewItemObject = definition == null ? null : new SerializedObject(definition);
            RefreshPreviewVariantAnalysis();
            RequestPreviewRender();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            _settings = ItemIconGeneratedAssetWriter.LoadOrCreateSettings();
            _settingsObject = _settings == null ? null : new SerializedObject(_settings);
            RefreshVariantAnalysis();
            RequestPreviewRender();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            CancelPreviewRender();
            CancelBake();
            EditorUtility.ClearProgressBar();
            DestroyPreviewTexture();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_settings == null)
            {
                EditorGUILayout.HelpBox("Item icon generator settings were not found.", MessageType.Warning);

                if (GUILayout.Button("Create Default Settings"))
                {
                    _settings = ItemIconGeneratedAssetWriter.LoadOrCreateSettings();
                    _settingsObject = new SerializedObject(_settings);
                }

                return;
            }

            EditorGUILayout.Space(4f);
            DrawBakePanel();
            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSettingsPanel();
                DrawPreviewPanel();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                var item = (ItemDefinition)EditorGUILayout.ObjectField(_previewItem, typeof(ItemDefinition), false, GUILayout.MinWidth(220f));

                if (EditorGUI.EndChangeCheck())
                {
                    SetPreviewItem(item);
                }

                GUILayout.FlexibleSpace();
                _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto Refresh", EditorStyles.toolbarButton, GUILayout.Width(100f));

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    RequestPreviewRender();
                }

                if (GUILayout.Button("Clear Cache", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                {
                    ItemIconCache.Clear();
                    RequestPreviewRender();
                }

                if (GUILayout.Button("Analyze", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    RefreshVariantAnalysis();
                }

                if (GUILayout.Button("Select SO", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                {
                    Selection.activeObject = _settings;
                    EditorGUIUtility.PingObject(_settings);
                }
            }
        }

        private void DrawSettingsPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.54f)))
            {
                _settingsScroll = EditorGUILayout.BeginScrollView(_settingsScroll);

                if (_previewItemObject != null)
                {
                    _showItemSettings = EditorGUILayout.Foldout(_showItemSettings, $"Item Icon Settings: {_previewItem.DisplayName}", true, EditorStyles.foldoutHeader);

                    if (_showItemSettings)
                    {
                        EditorGUI.BeginChangeCheck();
                        _previewItemObject.Update();
                        EditorGUILayout.PropertyField(_previewItemObject.FindProperty("_size"));
                        EditorGUILayout.PropertyField(_previewItemObject.FindProperty("_visualPrefab"));
                        EditorGUILayout.PropertyField(_previewItemObject.FindProperty("_defaultIconVariant"), true);
                        EditorGUILayout.PropertyField(_previewItemObject.FindProperty("_icon"), true);
                        bool itemChanged = EditorGUI.EndChangeCheck();
                        _previewItemObject.ApplyModifiedProperties();

                        if (itemChanged)
                        {
                            EditorUtility.SetDirty(_previewItem);
                            RefreshPreviewVariantAnalysis();

                            if (_autoRefresh)
                            {
                                RequestPreviewRender();
                            }
                        }
                    }

                    EditorGUILayout.Space(6f);
                }

                EditorGUILayout.LabelField("Global Generator Settings", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Loaded SO", _settings, typeof(ItemIconGeneratorSettings), false);
                }

                EditorGUI.BeginChangeCheck();
                _settingsObject.Update();
                DrawSerializedSettings(_settingsObject);
                bool changed = EditorGUI.EndChangeCheck();
                _settingsObject.ApplyModifiedProperties();
                EditorGUILayout.EndScrollView();

                if (changed)
                {
                    EditorUtility.SetDirty(_settings);
                    AssetDatabase.SaveAssets();
                    ItemIconGeneratorSettings.ResetDefaultCache();
                    ItemIconCache.Clear();
                    RefreshVariantAnalysis();

                    if (_autoRefresh)
                    {
                        RequestPreviewRender();
                    }
                }
            }
        }

        private void DrawBakePanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Editor-baked Catalog", EditorStyles.boldLabel);

                if (_allVariantAnalysis != null)
                {
                    EditorGUILayout.LabelField($"Total: {_allVariantAnalysis.Variants.Count} | Default: {_allVariantAnalysis.DefaultVariantCount} | Slot: {_allVariantAnalysis.SlotVariantCount} | Estimated raw RGBA: {EditorUtility.FormatBytes(_allVariantAnalysis.EstimatedTextureBytes)}");

                    if (_allVariantAnalysis.ExcessiveVariantItems.Count > 0)
                    {
                        EditorGUILayout.HelpBox($"{_allVariantAnalysis.ExcessiveVariantItems.Count} item(s) exceed {ItemIconVariantEnumeration.LARGE_VARIANT_WARNING} variants. Review counts before baking.", MessageType.Warning);
                    }

                    _variantCountScroll = EditorGUILayout.BeginScrollView(_variantCountScroll, GUILayout.Height(_variantCountHeight));

                    foreach (KeyValuePair<ItemDefinition, int> pair in _allVariantAnalysis.VariantCountsByItem)
                    {
                        EditorGUILayout.LabelField($"{pair.Key.DisplayName} [{pair.Key.Category}]: {pair.Value}", EditorStyles.miniLabel);
                    }

                    EditorGUILayout.EndScrollView();
                    DrawVariantCountResizeHandle();
                }

                if (_previewVariantLabels.Length > 0)
                {
                    EditorGUI.BeginChangeCheck();
                    _selectedVariantIndex = EditorGUILayout.Popup("Selected Variant", Mathf.Clamp(_selectedVariantIndex, 0, _previewVariantLabels.Length - 1), _previewVariantLabels);

                    if (EditorGUI.EndChangeCheck())
                    {
                        RequestPreviewRender();
                    }
                }

                _incrementalBake = EditorGUILayout.ToggleLeft("Incremental (skip variants whose baked signature is up to date)", _incrementalBake);

                using (new EditorGUI.DisabledScope(_isBaking))
                using (new EditorGUILayout.HorizontalScope())
                {
                    _selectedCategoryMask = EditorGUILayout.MaskField("Categories", _selectedCategoryMask, _categories);

                    if (GUILayout.Button("Bake Categories", GUILayout.Width(140f)))
                    {
                        StartBakeCategories();
                    }
                }

                using (new EditorGUI.DisabledScope(_isBaking))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Bake All"))
                    {
                        StartBakeAll();
                    }

                    if (GUILayout.Button("Bake Selected Items"))
                    {
                        StartBakeSelectedItems();
                    }

                    using (new EditorGUI.DisabledScope(_previewVariantAnalysis == null || _previewVariantAnalysis.Variants.Count == 0))
                    {
                        if (GUILayout.Button("Bake Selected Variant"))
                        {
                            StartBakeSelectedVariant();
                        }
                    }

                    if (GUILayout.Button("Validate Catalog"))
                    {
                        ValidateCatalog();
                    }

                    if (GUILayout.Button("Clear Generated Icons"))
                    {
                        ClearGeneratedIcons();
                    }
                }

                if (_isBaking && GUILayout.Button("Cancel Bake"))
                {
                    CancelBake();
                }

                EditorGUILayout.LabelField(_bakeStatus, EditorStyles.miniLabel);
            }
        }

        public void StartBakeAll()
        {
            RefreshVariantAnalysis();

            if (_allVariantAnalysis == null || _allVariantAnalysis.Variants.Count == 0)
            {
                EditorUtility.DisplayDialog("Item Icon Bake", "No valid icon variants were found.", "OK");
                return;
            }

            if (RequiresLargeBakeConfirmation(_allVariantAnalysis) && EditorUtility.DisplayDialog("Large Item Icon Bake", $"Bake {_allVariantAnalysis.Variants.Count} icon variants? Estimated raw RGBA size is {EditorUtility.FormatBytes(_allVariantAnalysis.EstimatedTextureBytes)}.", "Bake", "Cancel") == false)
            {
                return;
            }

            RunBakeAsync(_allVariantAnalysis.Variants, true).Forget(Debug.LogException);
        }

        private void StartBakeSelectedItems()
        {
            var selected = new List<ItemDefinition>();

            foreach (UnityEngine.Object selectedObject in Selection.objects)
            {
                if (selectedObject is ItemDefinition definition && selected.Contains(definition) == false)
                {
                    selected.Add(definition);
                }
            }

            if (selected.Count == 0 && _previewItem != null)
            {
                selected.Add(_previewItem);
            }

            ItemIconVariantAnalysis analysis = ItemIconVariantEnumerator.Enumerate(selected, _settings);

            if (analysis.Variants.Count == 0)
            {
                EditorUtility.DisplayDialog("Item Icon Bake", "Select at least one valid ItemDefinition with a visual prefab.", "OK");
                return;
            }

            if (RequiresLargeBakeConfirmation(analysis) && EditorUtility.DisplayDialog("Large Item Icon Bake", $"Bake {analysis.Variants.Count} selected variants?", "Bake", "Cancel") == false)
            {
                return;
            }

            RunBakeAsync(analysis.Variants, false).Forget(Debug.LogException);
        }

        private void StartBakeCategories()
        {
            var selected = new List<ItemDefinition>();

            foreach (ItemDefinition definition in ItemDefinitionIdValidator.LoadAllDefinitions())
            {
                int index = Array.IndexOf(_categories, definition.Category);

                if (index >= 0 && (_selectedCategoryMask == -1 || (_selectedCategoryMask & (1 << index)) != 0))
                {
                    selected.Add(definition);
                }
            }

            ItemIconVariantAnalysis analysis = ItemIconVariantEnumerator.Enumerate(selected, _settings);

            if (analysis.Variants.Count == 0)
            {
                EditorUtility.DisplayDialog("Item Icon Bake", "No valid icon variants were found for the selected categories.", "OK");
                return;
            }

            RunBakeAsync(analysis.Variants, false).Forget(Debug.LogException);
        }

        private void StartBakeSelectedVariant()
        {
            int index = Mathf.Clamp(_selectedVariantIndex, 0, _previewVariantAnalysis.Variants.Count - 1);
            RunBakeAsync(new[] { _previewVariantAnalysis.Variants[index] }, false).Forget(Debug.LogException);
        }

        internal async UniTask<int> RunBakeAsync(IReadOnlyList<ItemIconBakeVariant> variants, bool replaceCatalog)
        {
            ItemDefinitionIdValidationResult idValidation = ItemDefinitionIdValidator.Validate(ItemDefinitionIdValidator.LoadAllDefinitions());

            if (idValidation.IsValid == false)
            {
                _bakeStatus = $"DefinitionId validation failed with {idValidation.Errors.Count} issue(s). Run Validate Definition IDs first.";
                Debug.LogError(_bakeStatus + "\n" + string.Join("\n", idValidation.Errors));
                return 0;
            }

            CancelBake();
            _bakeCancellation = new CancellationTokenSource();
            CancellationTokenSource cancellation = _bakeCancellation;
            _isBaking = true;
            string resultStatus = null;
            int baked = 0;

            try
            {
                baked = await ItemIconBakeCoordinator.BakeAsync(variants, _settings, replaceCatalog, _incrementalBake, UpdateBakeProgress, cancellation.Token);
                resultStatus = $"Rendered {baked} of {variants.Count} variant(s).";
            }
            catch (OperationCanceledException)
            {
                resultStatus = "Bake canceled. Completed variants were retained in the catalog.";
            }
            catch (Exception exception)
            {
                resultStatus = $"Bake failed: {exception.Message}";
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isBaking = false;

                if (ReferenceEquals(_bakeCancellation, cancellation))
                {
                    _bakeCancellation = null;
                }

                cancellation.Dispose();
                RefreshVariantAnalysis();
                _bakeStatus = resultStatus ?? _bakeStatus;
                Repaint();
            }

            return baked;
        }

        private void UpdateBakeProgress(int completed, int total, ItemIconBakeVariant variant)
        {
            if (total <= 0)
            {
                return;
            }

            string label = variant == null ? "Saving catalog" : variant.DisplayName;
            _bakeStatus = $"Baking {Mathf.Min(completed + 1, total)}/{total}: {label}";

            if (Application.isBatchMode == false && EditorUtility.DisplayCancelableProgressBar("Baking Item Icons", label, Mathf.Clamp01((float)completed / total)))
            {
                _bakeCancellation?.Cancel();
            }
        }

        private void ValidateCatalog()
        {
            ItemIconCatalogValidationResult result = ItemIconCatalogValidator.Validate(true);
            _bakeStatus = result.IsValid ? $"Catalog is valid: {result.EntryCount} entries." : $"Catalog validation: {result.Errors.Count} issue(s). See Console.";

            if (result.IsValid)
            {
                Debug.Log(_bakeStatus);
            }
            else
            {
                Debug.LogWarning($"{_bakeStatus}\n{string.Join("\n", result.Errors)}");
            }
        }

        private void ClearGeneratedIcons()
        {
            if (EditorUtility.DisplayDialog("Clear Generated Item Icons", "Delete all generated icon assets and clear the baked catalog?", "Clear", "Cancel") == false)
            {
                return;
            }

            ItemIconGeneratedAssetWriter.ClearGeneratedAssets();
            _bakeStatus = "Generated icons and catalog entries were cleared.";
            RefreshVariantAnalysis();
        }

        private void RefreshVariantAnalysis()
        {
            if (_settings == null)
            {
                _allVariantAnalysis = null;
                _previewVariantAnalysis = null;
                _previewVariantLabels = Array.Empty<string>();
                return;
            }

            List<ItemDefinition> definitions = ItemDefinitionIdValidator.LoadAllDefinitions();
            ItemDefinitionIdValidationResult validation = ItemDefinitionIdValidator.Validate(definitions);
            var categories = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ItemDefinition definition in definitions)
            {
                categories.Add(definition.Category);
            }

            _categories = new List<string>(categories).ToArray();

            if (validation.IsValid == false)
            {
                _allVariantAnalysis = null;
                _bakeStatus = $"DefinitionId validation failed: {validation.Errors.Count} issue(s).";
            }
            else
            {
                _allVariantAnalysis = ItemIconVariantEnumerator.Enumerate(definitions, _settings);
                _bakeStatus = $"Analyzed {_allVariantAnalysis.Variants.Count} bake variant(s).";
            }

            RefreshPreviewVariantAnalysis();
        }

        private void RefreshPreviewVariantAnalysis()
        {
            _previewVariantAnalysis = _settings == null ? null : ItemIconVariantEnumerator.Enumerate(_previewItem, _settings);
            int count = _previewVariantAnalysis?.Variants.Count ?? 0;
            _previewVariantLabels = new string[count];

            for (int i = 0; i < count; i++)
            {
                _previewVariantLabels[i] = _previewVariantAnalysis.Variants[i].DisplayName;
            }

            _selectedVariantIndex = Mathf.Clamp(_selectedVariantIndex, 0, Mathf.Max(0, count - 1));
        }

        private static bool RequiresLargeBakeConfirmation(ItemIconVariantAnalysis analysis) => Application.isBatchMode == false && (analysis.Variants.Count >= LARGE_BAKE_CONFIRMATION_THRESHOLD || analysis.ExcessiveVariantItems.Count > 0);

        private void CancelBake() => _bakeCancellation?.Cancel();

        private void DrawPreviewPanel()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                Rect previewRect = GUILayoutUtility.GetRect(256f, 256f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(previewRect, new Color(0.14f, 0.14f, 0.14f, 1f));

                if (_previewTexture == null)
                {
                    GUI.Label(previewRect, GetPreviewPlaceholderText(), CenteredLabelStyle);
                }
                else
                {
                    EditorGUI.DrawTextureTransparent(GetCenteredImageRect(previewRect, _previewTexture), _previewTexture, ScaleMode.ScaleToFit);
                    EditorGUILayout.LabelField($"{_previewTexture.width} x {_previewTexture.height}", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawVariantCountResizeHandle()
        {
            Rect handleRect = GUILayoutUtility.GetRect(1f, VARIANT_COUNT_RESIZE_HANDLE_HEIGHT, GUILayout.ExpandWidth(true));
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeVertical);
            EditorGUI.DrawRect(new Rect(handleRect.x, handleRect.center.y, handleRect.width, 1f), new Color(0.32f, 0.32f, 0.32f, 1f));
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && handleRect.Contains(currentEvent.mousePosition))
            {
                _isResizingVariantCount = true;
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag && _isResizingVariantCount)
            {
                _variantCountHeight = Mathf.Clamp(_variantCountHeight + currentEvent.delta.y, MIN_VARIANT_COUNT_HEIGHT, MAX_VARIANT_COUNT_HEIGHT);
                currentEvent.Use();
                Repaint();
            }
            else if (currentEvent.type == EventType.MouseUp && _isResizingVariantCount)
            {
                _isResizingVariantCount = false;
                currentEvent.Use();
            }
        }

        private static void DrawSerializedSettings(SerializedObject serializedSettings)
        {
            SerializedProperty property = serializedSettings.GetIterator();
            bool enterChildren = true;

            while (property.NextVisible(enterChildren))
            {
                using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
                {
                    EditorGUILayout.PropertyField(property, true);
                }

                enterChildren = false;
            }
        }

        public async UniTask RenderPreviewAsync()
        {
            DestroyPreviewTexture();

            if (CanRenderPreview() == false)
            {
                Repaint();
                return;
            }

            var cancellation = new CancellationTokenSource();
            _previewRenderCancellation = cancellation;

            try
            {
                ItemIconBakeVariant variant = _previewVariantAnalysis != null && _previewVariantAnalysis.Variants.Count > 0 ? _previewVariantAnalysis.Variants[Mathf.Clamp(_selectedVariantIndex, 0, _previewVariantAnalysis.Variants.Count - 1)] : null;
                Texture2D previewTexture = variant == null ? await ItemIconRenderer.RenderPreviewTextureAsync(_previewItem, settings: _settings, cancellationToken: cancellation.Token) : await ItemIconRenderer.RenderPreviewTextureAsync(_previewItem, variant.VariantIds, variant.Width, variant.Height, variant.ProfileType, _settings, cancellation.Token);

                if (cancellation.IsCancellationRequested || this == null)
                {
                    DestroyImmediate(previewTexture);
                    return;
                }

                _previewTexture = previewTexture;

                if (_previewTexture != null)
                {
                    _previewTexture.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _previewTexture = null;
            }
            finally
            {
                if (ReferenceEquals(_previewRenderCancellation, cancellation))
                {
                    _previewRenderCancellation = null;
                }

                cancellation.Dispose();
            }

            if (this != null)
            {
                Repaint();
            }
        }

        private void RequestPreviewRender()
        {
            CancelPreviewRender();

            if (_previewRenderScheduled)
            {
                return;
            }

            _previewRenderScheduled = true;
            EditorApplication.delayCall += RenderPreviewIfScheduled;
        }

        private void RenderPreviewIfScheduled()
        {
            _previewRenderScheduled = false;

            if (this != null)
            {
                RenderPreviewAsync().Forget(Debug.LogException);
            }
        }

        private bool CanRenderPreview() => _settings != null && _previewItem != null && EditorApplication.isPlayingOrWillChangePlaymode == false && EditorApplication.isCompiling == false && EditorApplication.isUpdating == false;

        private void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                CancelPreviewRender();
                DestroyPreviewTexture();
                Repaint();
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                RequestPreviewRender();
            }
        }

        private string GetPreviewPlaceholderText()
        {
            if (_previewItem == null)
            {
                return "Assign ItemDefinition for preview";
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return "Preview is disabled in Play Mode";
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return "Preview is waiting for editor update";
            }

            return "No generated preview";
        }

        private void DestroyPreviewTexture()
        {
            if (_previewTexture == null)
            {
                return;
            }

            DestroyImmediate(_previewTexture);
            _previewTexture = null;
        }

        private void CancelPreviewRender()
        {
            if (_previewRenderCancellation == null)
            {
                return;
            }

            _previewRenderCancellation.Cancel();
            _previewRenderCancellation = null;
        }

        private static Rect GetCenteredImageRect(Rect containerRect, Texture2D texture)
        {
            float scale = Mathf.Min(containerRect.width / texture.width, containerRect.height / texture.height);
            float width = texture.width * scale;
            float height = texture.height * scale;
            return new Rect(containerRect.x + (containerRect.width - width) * 0.5f, containerRect.y + (containerRect.height - height) * 0.5f, width, height);
        }
    }
}
