using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RaidIntoDarkness.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;
using UnityEngine.UI;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.GraphicsOptions
{
    [DisallowMultipleComponent]
    public sealed class GraphicsMenu : MonoBehaviour
    {
        private const string MILITARY_SCENE = "Assets/_Source/Levels/Anomaly/test_5665bccf/y04_pole/Scenes/y04_pole.unity";
        private const string DEMO_SCENE = "Assets/TEST_SCENE.unity";
        private const string HDRP_UNAVAILABLE = "Требуется HDRP";
        private static readonly string[] PRESETS = { "Низкие", "Средние", "Высокие", "Максимальные", "Ультра" };
        private static readonly string[] QUALITY = { "Низкое", "Среднее", "Высокое" };
        private static readonly string[] LOD = { "Низкая", "Средняя", "Высокая" };
        private static readonly string[] AO = { "Выкл.", "Низкое", "Среднее", "Высокое" };
        private static readonly string[] REFLECTIONS = { "Выкл.", "Низкое", "Среднее", "Высокое", "Ультра" };
        private static readonly string[] SHADOW_RESOLUTION = { "Низкое", "Среднее", "Высокое", "Ультра" };
        private static readonly string[] CASCADES = { "1", "2", "3", "4" };
        private static readonly string[] LOCAL_SHADOW_BUDGET = { "Выкл.", "4", "8", "16", "Без ограничения" };
        private static readonly string[] STREAMING_BUDGET = { "256 МБ", "512 МБ", "1 ГБ", "2 ГБ", "4 ГБ" };
        private static readonly string[] UPSCALERS = { "Native", "TAAU", "AMD FSR 2", "NVIDIA DLSS" };
        private static readonly string[] UPSCALER_QUALITY = { "Качество", "Баланс", "Производительность" };
        private static readonly string[] UPSCALER_QUALITY_WITH_ULTRA = { "Качество", "Баланс", "Производительность", "Ультра" };
        private static readonly string[] ANISOTROPY = { "Выкл.", "2x", "4x", "8x", "16x" };
        private static readonly string[] FPS = { "30", "60", "90", "120", "144", "165", "240", "Без ограничения" };
        private static readonly int[] ANISOTROPY_VALUES = VideoPresets.AnisotropyLevels;
        private static readonly int[] FPS_VALUES = VideoPresets.FrameLimits;
        private static readonly float[] CALIBRATION_LEVELS = { 0.0f, 0.012f, 0.025f, 0.05f, 0.12f, 0.25f, 0.50f, 0.80f, 1.0f };

        [SerializeField] private GameObject _mainMenu;
        [SerializeField] private CanvasGroup _options;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private Button _open;
        [SerializeField] private Button _back;
        [SerializeField] private Button _graphics;
        [SerializeField] private Button _playMilitary;
        [FormerlySerializedAs("_play")]
        [SerializeField] private Button _playDemo;
        [SerializeField] private Button _quit;
        [SerializeField] private bool _externalCancelInput;
        [SerializeField] private Button _apply;
        [SerializeField] private Button _cancel;
        [SerializeField] private Button _defaults;
        [SerializeField] private HorizontalSelector[] _selectors;
        [SerializeField] private Slider[] _sliders;
        [SerializeField] private TMP_Text[] _readouts;
        [SerializeField] private Toggle[] _toggles;
        [SerializeField] private TMP_Text _customLabel;
        [SerializeField] private TMP_Text _fpsHint;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private Image[] _calibration;
        [SerializeField] private GameObject _dialog;
        [SerializeField] private TMP_Text _dialogTitle;
        [SerializeField] private TMP_Text _dialogBody;
        [SerializeField] private Button _dialogPrimary;
        [SerializeField] private Button _dialogSecondary;
        [SerializeField] private TMP_Text _primaryLabel;
        [SerializeField] private TMP_Text _secondaryLabel;

        private readonly List<FullScreenMode> _modeOptions = new();
        private readonly List<VideoAntialiasing> _aaOptions = new();
        private readonly List<int> _upscalingOptions = new();
        private readonly List<string> _labels = new();
        private VideoSettingsRuntime _runtime;
        private VideoSettingsSession _session;
        private IReadOnlyList<VideoDisplay> _sizeOptions;
        private IReadOnlyList<VideoDisplay> _rateOptions;
        private GameObject _lastSelection;
        private GameObject _preDialogSelection;
        private InputAction _cancelAction;
        private MenuDialog _menuDialog;

        private enum MenuDialog
        {
            None,
            Reset,
            Exit
        }

        private enum Selector
        {
            Preset,
            Mode,
            Resolution,
            RefreshRate,
            FrameLimit,
            Antialiasing,
            AntialiasingQuality,
            TextureQuality,
            Anisotropy,
            Lod,
            ShadowQuality,
            AmbientOcclusion,
            Fog,
            ContactShadowQuality,
            Reflections,
            Rain,
            Anomalies,
            LocalShadowBudget,
            Ssgi,
            Ssr,
            ShadowCascades,
            Decals,
            StreamingBudget,
            UpscalingMethod,
            UpscalingQuality
        }

        private enum Range
        {
            Brightness,
            RenderScale,
            SunShadows,
            LocalShadows,
            GrassDensity,
            GrassDistance,
            GrassHeight,
            ObjectDistance,
            Sharpening
        }

        private enum Switch
        {
            VSync,
            GrassShadows,
            MotionBlur,
            Bloom,
            LensFlare,
            ContactShadows
        }

        private delegate void EditValue(ref VideoSettings settings, int value);

        private void Awake()
        {
            _open.onClick.AddListener(Open);
            _back.onClick.AddListener(RequestClose);
            _graphics.onClick.AddListener(FocusGraphics);
            if (_playMilitary != null)
            {
                _playMilitary.onClick.AddListener(PlayMilitary);
            }
            if (_playDemo != null)
            {
                _playDemo.onClick.AddListener(PlayDemo);
            }
            if (_quit != null)
            {
                _quit.onClick.AddListener(Application.Quit);
            }
            _apply.onClick.AddListener(() => _session.Apply(Time.realtimeSinceStartupAsDouble));
            _cancel.onClick.AddListener(() => _session.Cancel(Time.realtimeSinceStartupAsDouble));
            _defaults.onClick.AddListener(OpenReset);
            _dialogPrimary.onClick.AddListener(ConfirmPrimary);
            _dialogSecondary.onClick.AddListener(ConfirmSecondary);

            _selectors[(int)Selector.Preset].IndexChanged += value => _session.SelectPreset(value);
            Bind(Selector.Mode, SelectMode);
            Bind(Selector.Resolution, SelectResolution);
            Bind(Selector.RefreshRate, (ref VideoSettings settings, int value) => settings.display = _rateOptions[value]);
            Bind(Selector.FrameLimit, (ref VideoSettings settings, int value) => settings.fpsLimit = FPS_VALUES[value]);
            Bind(Selector.Antialiasing, SelectAntialiasing);
            Bind(Selector.AntialiasingQuality, (ref VideoSettings settings, int value) => settings.quality.antialiasingQuality = value);
            Bind(Selector.TextureQuality, (ref VideoSettings settings, int value) => settings.quality.textureQuality = value);
            Bind(Selector.Anisotropy, (ref VideoSettings settings, int value) => settings.quality.anisotropy = ANISOTROPY_VALUES[value]);
            Bind(Selector.Lod, (ref VideoSettings settings, int value) => settings.quality.lodQuality = value);
            Bind(Selector.ShadowQuality, (ref VideoSettings settings, int value) => settings.quality.shadowQuality = value);
            Bind(Selector.AmbientOcclusion, (ref VideoSettings settings, int value) => settings.quality.ambientOcclusion = value);
            Bind(Selector.Fog, (ref VideoSettings settings, int value) => settings.quality.fogQuality = value);
            Bind(Selector.ContactShadowQuality, (ref VideoSettings settings, int value) => settings.quality.contactShadowQuality = value);
            Bind(Selector.Reflections, (ref VideoSettings settings, int value) => settings.quality.reflections = value);
            Bind(Selector.Rain, (ref VideoSettings settings, int value) => settings.quality.rainQuality = value);
            Bind(Selector.Anomalies, (ref VideoSettings settings, int value) => settings.quality.anomalyQuality = value);
            Bind(Selector.LocalShadowBudget, (ref VideoSettings settings, int value) => settings.quality.localShadowBudget = value);
            Bind(Selector.Ssgi, (ref VideoSettings settings, int value) => settings.quality.ssgi = value);
            Bind(Selector.Ssr, (ref VideoSettings settings, int value) => settings.quality.ssr = value);
            Bind(Selector.ShadowCascades, (ref VideoSettings settings, int value) => settings.quality.shadowCascades = value + 1);
            Bind(Selector.Decals, (ref VideoSettings settings, int value) => settings.quality.decals = value);
            Bind(Selector.StreamingBudget, (ref VideoSettings settings, int value) => settings.quality.streamingBudget = value);
            Bind(Selector.UpscalingMethod, SelectUpscaling);
            Bind(Selector.UpscalingQuality, (ref VideoSettings settings, int value) => settings.quality.upscalingQuality = value);

            Bind(Range.Brightness, 50, 150, (ref VideoSettings settings, int value) => settings.brightness = value);
            Bind(Range.RenderScale, 50, 100, (ref VideoSettings settings, int value) => settings.quality.renderScale = value);
            Bind(Range.SunShadows, 75, 500, (ref VideoSettings settings, int value) => settings.quality.sunShadowDistance = value);
            Bind(Range.LocalShadows, 5, 75, (ref VideoSettings settings, int value) => settings.quality.localShadowDistance = value);
            Bind(Range.GrassDensity, 25, VideoPresets.MAX_GRASS_DENSITY, (ref VideoSettings settings, int value) => settings.quality.grassSpacing = _runtime.Presets.GrassSpacing(value));
            Bind(Range.GrassDistance, 0, VideoPresets.MAX_GRASS_DISTANCE, (ref VideoSettings settings, int value) => settings.quality.grassDistance = value);
            Bind(Range.GrassHeight, Mathf.RoundToInt(XRayGrassValues.MIN_HEIGHT * 100f), Mathf.RoundToInt(XRayGrassValues.MAX_HEIGHT * 100f), (ref VideoSettings settings, int value) => settings.grassHeight = value / 100f);
            Bind(Range.ObjectDistance, VideoPresets.MIN_OBJECT_DISTANCE, VideoPresets.MAX_OBJECT_DISTANCE, (ref VideoSettings settings, int value) => settings.quality.objectDrawDistance = value);
            Bind(Range.Sharpening, 0, 100, (ref VideoSettings settings, int value) => settings.quality.sharpening = value);

            Bind(Switch.VSync, (ref VideoSettings settings, int value) => settings.vSync = value != 0);
            Bind(Switch.GrassShadows, (ref VideoSettings settings, int value) => settings.quality.grassShadows = value != 0);
            Bind(Switch.MotionBlur, (ref VideoSettings settings, int value) => settings.quality.motionBlur = value != 0);
            Bind(Switch.Bloom, (ref VideoSettings settings, int value) => settings.quality.bloom = value != 0);
            Bind(Switch.LensFlare, (ref VideoSettings settings, int value) => settings.quality.lensFlare = value != 0);
            Bind(Switch.ContactShadows, (ref VideoSettings settings, int value) => settings.quality.contactShadows = value != 0);
        }

        private void Start()
        {
            _open.interactable = VideoSettingsRuntime.Instance != null;
            if (_playMilitary != null)
            {
                _playMilitary.interactable &= SceneLoader.CanLoad(MILITARY_SCENE);
            }
            if (_playDemo != null)
            {
                _playDemo.interactable &= SceneLoader.CanLoad(DEMO_SCENE);
            }
            if (_open.IsActive())
            {
                Select(_open.gameObject);
            }
        }

        private void Update()
        {
            if (_session == null || _dialog.activeSelf || EventSystem.current == null)
            {
                return;
            }
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected == _lastSelection)
            {
                return;
            }
            _lastSelection = selected;
            if (selected == null || !selected.transform.IsChildOf(_scroll.content))
            {
                return;
            }
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_scroll.viewport, (RectTransform)selected.transform);
            Rect viewport = _scroll.viewport.rect;
            float offset = bounds.max.y > viewport.yMax ? viewport.yMax - bounds.max.y : Mathf.Max(0.0f, viewport.yMin - bounds.min.y);
            if (!Mathf.Approximately(offset, 0.0f))
            {
                _scroll.StopMovement();
                _scroll.content.anchoredPosition += Vector2.up * offset;
                _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition);
            }
        }

        private void OnDisable()
        {
            LeaveSession();
        }

        private void Open()
        {
            _runtime = VideoSettingsRuntime.Instance;
            if (_runtime == null || _session != null || SceneLoader.IsLoading)
            {
                return;
            }
            _session = _runtime.Session;
            _runtime.Display.Refresh();
            _session.SyncDisplay();
            _session.Changed += Refresh;
            if (!_externalCancelInput)
            {
                var module = EventSystem.current?.GetComponent<InputSystemUIInputModule>();
                _cancelAction = module?.cancel?.action;
                if (_cancelAction != null)
                {
                    _cancelAction.performed += OnCancelInput;
                }
            }
            _mainMenu.SetActive(false);
            _options.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1.0f;
            Refresh();
            if (!_dialog.activeSelf)
            {
                Select(_graphics.gameObject);
            }
        }

        private void RequestClose()
        {
            if (_session == null || _session.State != VideoApplyState.Idle || _menuDialog != MenuDialog.None)
            {
                return;
            }
            if (!_session.Dirty)
            {
                Close();
                return;
            }
            _menuDialog = MenuDialog.Exit;
            Refresh();
        }

        private void OnCancelInput(InputAction.CallbackContext context)
        {
            HandleCancel();
        }

        public bool HandleCancel()
        {
            if (_session == null)
            {
                return false;
            }
            if (_menuDialog != MenuDialog.None || _session.State == VideoApplyState.Confirming || _session.State == VideoApplyState.Switching)
            {
                ConfirmSecondary();
            }
            else
            {
                RequestClose();
            }
            return true;
        }

        public void CloseForOwner()
        {
            if (_session != null)
            {
                Close();
            }
        }

        private void Close()
        {
            LeaveSession();
            _dialog.SetActive(false);
            _options.gameObject.SetActive(false);
            _mainMenu.SetActive(true);
            Select(_open.gameObject);
        }

        private void LeaveSession()
        {
            if (_session == null)
            {
                return;
            }
            if (_cancelAction != null)
            {
                _cancelAction.performed -= OnCancelInput;
                _cancelAction = null;
            }
            _session.Changed -= Refresh;
            _session.Cancel(Time.realtimeSinceStartupAsDouble);
            _session = null;
            _menuDialog = MenuDialog.None;
            _preDialogSelection = null;
        }

        private void FocusGraphics()
        {
            _scroll.verticalNormalizedPosition = 1.0f;
            CustomArrowButton arrow = _selectors[(int)Selector.Preset].GetComponentInChildren<CustomArrowButton>();
            if (arrow != null)
            {
                Select(arrow.gameObject);
            }
        }

        private void OpenReset()
        {
            _menuDialog = MenuDialog.Reset;
            Refresh();
        }

        private void ConfirmPrimary()
        {
            switch (_menuDialog)
            {
                case MenuDialog.Reset:
                    _menuDialog = MenuDialog.None;
                    _session.Reset(VideoCategory.All);
                    Refresh();
                    break;
                case MenuDialog.Exit:
                    Close();
                    break;
                default:
                    _session.Keep(Time.realtimeSinceStartupAsDouble);
                    break;
            }
        }

        private void ConfirmSecondary()
        {
            if (_menuDialog != MenuDialog.None)
            {
                _menuDialog = MenuDialog.None;
                Refresh();
                return;
            }
            _session.Cancel(Time.realtimeSinceStartupAsDouble);
        }

        private void Bind(Selector selector, EditValue action)
        {
            int index = (int)selector;
            if (index < _selectors.Length && _selectors[index] != null)
            {
                _selectors[index].IndexChanged += value => Edit(action, value);
            }
        }

        private void Bind(Range range, int minimum, int maximum, EditValue action)
        {
            int index = (int)range;
            if (index >= _sliders.Length || _sliders[index] == null)
            {
                return;
            }
            Slider slider = _sliders[index];
            slider.wholeNumbers = true;
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.onValueChanged.AddListener(value => Edit(action, Mathf.RoundToInt(value)));
        }

        private void Bind(Switch toggle, EditValue action)
        {
            _toggles[(int)toggle].onValueChanged.AddListener(value => Edit(action, value ? 1 : 0));
        }

        private void Edit(EditValue action, int value)
        {
            if (_session == null || _session.State != VideoApplyState.Idle)
            {
                return;
            }
            VideoSettings draft = _session.Draft;
            action(ref draft, value);
            _session.Edit(draft);
        }

        private void SelectMode(ref VideoSettings settings, int index)
        {
            VideoDisplay display = settings.display;
            display.mode = _modeOptions[index];
            settings.display = _runtime.Display.Normalize(display);
        }

        private void SelectResolution(ref VideoSettings settings, int index)
        {
            VideoDisplay selected = _sizeOptions[index];
            VideoDisplay old = settings.display;
            settings.display = _runtime.Display.Normalize(new VideoDisplay(old.mode, selected.width, selected.height, old.Refresh));
        }

        private void SelectAntialiasing(ref VideoSettings settings, int index)
        {
            settings.quality.antialiasing = _aaOptions[index];
            settings.quality.antialiasingQuality = VideoPresets.HasAntialiasingQuality(settings.quality.antialiasing) ? 1 : 0;
        }

        private void SelectUpscaling(ref VideoSettings settings, int index)
        {
            int method = _upscalingOptions[index];
            settings.quality.upscalingMethod = method;
            if (settings.quality.upscalingQuality == 3 && !_runtime.SupportsUltraPerformance(method))
            {
                settings.quality.upscalingQuality = 2;
            }
        }

        private void Refresh()
        {
            RefreshDialog();
            if (_session.State != VideoApplyState.Idle)
            {
                return;
            }
            VideoSettings draft = _session.Draft;
            VideoQuality quality = draft.quality;
            _apply.interactable = _session.Dirty;
            _cancel.interactable = _session.Dirty;
            int upscaler = _runtime.ResolveUpscalingMethod(quality.upscalingMethod);
            _status.text = _session.Error ?? (upscaler == quality.upscalingMethod ? "" : UPSCALERS[quality.upscalingMethod] + " недоступен · " + UPSCALERS[upscaler]);
            _customLabel.gameObject.SetActive(_runtime.Presets.IsCustom(draft));
            Set(Selector.Preset, PRESETS, draft.preset);
            RefreshDisplay(draft);
            Set(Selector.FrameLimit, FPS, Array.IndexOf(FPS_VALUES, draft.fpsLimit));
            _fpsHint.text = draft.vSync ? "FPS · ПРИОРИТЕТ VSYNC" : "ОГРАНИЧЕНИЕ FPS";
            Set(Switch.VSync, draft.vSync);
            RefreshAntialiasing(quality, upscaler);
            Set(Range.Brightness, draft.brightness, "%", _runtime.SupportsRendering ? null : HDRP_UNAVAILABLE);
            Set(Range.RenderScale, quality.renderScale, "%", upscaler != 0 ? "Задаёт апскейлер" : _runtime.SupportsRenderScale ? null : "Масштаб HDRP недоступен");
            Set(Selector.TextureQuality, PRESETS, quality.textureQuality);
            Set(Selector.Anisotropy, ANISOTROPY, Array.IndexOf(ANISOTROPY_VALUES, quality.anisotropy));
            Set(Selector.Lod, LOD, quality.lodQuality);
            SetSupported(Selector.ShadowQuality, SHADOW_RESOLUTION, quality.shadowQuality, _runtime.SupportsRendering, HDRP_UNAVAILABLE);
            Set(Range.SunShadows, quality.sunShadowDistance, " м", _runtime.SupportsRendering ? null : HDRP_UNAVAILABLE);
            Set(Range.LocalShadows, quality.localShadowDistance, " м", _runtime.SupportsLocalShadows ? null : "Тени Point/Spot недоступны");
            SetSupported(Selector.AmbientOcclusion, AO, quality.ambientOcclusion, _runtime.SupportsAmbientOcclusion, "SSAO недоступно");
            bool contactShadowsSupported = _runtime.SupportsContactShadows;
            Set(Switch.ContactShadows, contactShadowsSupported && quality.contactShadows, contactShadowsSupported);
            SetSupported(Selector.ContactShadowQuality, QUALITY, quality.contactShadowQuality, contactShadowsSupported && quality.contactShadows, contactShadowsSupported ? "Контактные тени выкл." : "Недоступно в HDRP");
            Set(Range.GrassDensity, _runtime.Presets.GrassPercent(quality.grassSpacing), "%", _runtime.SupportsGrass ? null : "Настройки травы недоступны");
            Set(Range.GrassDistance, quality.grassDistance, " м", _runtime.SupportsGrass ? null : "Настройки травы недоступны");
            Set(Range.GrassHeight, Mathf.RoundToInt(draft.grassHeight * 100f), "%", _runtime.SupportsGrass ? null : "Настройки травы недоступны");
            Set(Switch.GrassShadows, quality.grassShadows, _runtime.SupportsGrass);
            SetSupported(Selector.Fog, QUALITY, quality.fogQuality, _runtime.SupportsFog, "Объёмный туман недоступен");
            Set(Switch.MotionBlur, quality.motionBlur, _runtime.SupportsMotion);
            Set(Switch.Bloom, quality.bloom, _runtime.SupportsRendering);
            Set(Switch.LensFlare, quality.lensFlare, _runtime.SupportsRendering);
            RefreshAdvancedQuality(quality, upscaler);
            for (int i = 0; i < _calibration.Length; i++)
            {
                float level = Mathf.Clamp01(CALIBRATION_LEVELS[i] * draft.brightness * 0.01f);
                if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
                {
                    level = Mathf.LinearToGammaSpace(level);
                }
                _calibration[i].color = new Color(level, level, level);
            }
        }

        private void RefreshAdvancedQuality(VideoQuality quality, int upscaler)
        {
            SetSupported(Selector.Reflections, REFLECTIONS, quality.reflections, _runtime.SupportsReflections, "Отражения недоступны");
            Set(Selector.Rain, QUALITY, quality.rainQuality);
            Set(Selector.Anomalies, QUALITY, quality.anomalyQuality);
            SetSupported(Selector.LocalShadowBudget, LOCAL_SHADOW_BUDGET, quality.localShadowBudget, _runtime.SupportsRendering, HDRP_UNAVAILABLE);
            SetSupported(Selector.Ssgi, AO, quality.ssgi, _runtime.SupportsSsgi, "SSGI недоступно");
            SetSupported(Selector.Ssr, AO, quality.ssr, _runtime.SupportsSsr, "SSR недоступно");
            SetSupported(Selector.ShadowCascades, CASCADES, quality.shadowCascades - 1, _runtime.SupportsRendering, HDRP_UNAVAILABLE);
            SetSupported(Selector.Decals, AO, quality.decals, _runtime.SupportsDecals, "Декали недоступны");
            SetSupported(Selector.StreamingBudget, STREAMING_BUDGET, quality.streamingBudget, _runtime.SupportsTextureStreaming, "Стриминг недоступен");
            Set(Range.ObjectDistance, quality.objectDrawDistance, " м", null, quality.objectDrawDistance == VideoPresets.MAX_OBJECT_DISTANCE ? "Без ограничения" : null);
            Set(Range.Sharpening, quality.sharpening, "%", _runtime.SupportsSharpening ? null : HDRP_UNAVAILABLE);
            _upscalingOptions.Clear();
            _labels.Clear();
            for (int method = 0; method < UPSCALERS.Length; method++)
            {
                if (_runtime.SupportsUpscaling(method))
                {
                    _upscalingOptions.Add(method);
                    _labels.Add(UPSCALERS[method]);
                }
            }
            Set(Selector.UpscalingMethod, _labels, Mathf.Max(0, _upscalingOptions.IndexOf(upscaler)));
            string[] qualityOptions = _runtime.SupportsUltraPerformance(upscaler) ? UPSCALER_QUALITY_WITH_ULTRA : UPSCALER_QUALITY;
            SetSupported(Selector.UpscalingQuality, qualityOptions, Mathf.Min(quality.upscalingQuality, qualityOptions.Length - 1), _runtime.SupportsUpscalingQuality(upscaler), "Ручной масштаб");
        }

        private void RefreshDisplay(VideoSettings draft)
        {
            VideoDisplay display = draft.display;
            VideoDisplayOptions options = _runtime.Display;
            if (!options.Supported)
            {
                Set(Selector.Mode, new[] { "Доступно в Player" }, 0);
                Set(Selector.Resolution, new[] { "Доступно в Player" }, 0);
                Set(Selector.RefreshRate, new[] { "Доступно в Player" }, 0);
                return;
            }
            _modeOptions.Clear();
            _labels.Clear();
            if (options.ExclusiveSupported)
            {
                _modeOptions.Add(FullScreenMode.ExclusiveFullScreen);
                _labels.Add("Полноэкранный");
            }
            _modeOptions.Add(FullScreenMode.FullScreenWindow);
            _labels.Add("Без рамки");
            _modeOptions.Add(FullScreenMode.Windowed);
            _labels.Add("Оконный");
            Set(Selector.Mode, _labels, _modeOptions.IndexOf(display.mode));

            _sizeOptions = options.Sizes(display.mode);
            _labels.Clear();
            int selectedSize = 0;
            for (int i = 0; i < _sizeOptions.Count; i++)
            {
                VideoDisplay size = _sizeOptions[i];
                _labels.Add(SizeLabel(size));
                if (size.width == display.width && size.height == display.height)
                {
                    selectedSize = i;
                }
            }
            Set(Selector.Resolution, _labels, selectedSize);

            _rateOptions = options.Rates(display);
            _labels.Clear();
            int selectedRate = 0;
            if (_rateOptions.Count == 0)
            {
                _labels.Add(options.CurrentRefresh.value.ToString("0.##") + " Гц · рабочий стол");
            }
            for (int i = 0; i < _rateOptions.Count; i++)
            {
                VideoDisplay rate = _rateOptions[i];
                _labels.Add(rate.Refresh.value.ToString("0.##") + " Гц");
                if (rate.Matches(display))
                {
                    selectedRate = i;
                }
            }
            Set(Selector.RefreshRate, _labels, selectedRate);
        }

        private void RefreshAntialiasing(VideoQuality quality, int upscaler)
        {
            if (!_runtime.SupportsRendering)
            {
                Set(Selector.Antialiasing, new[] { HDRP_UNAVAILABLE }, 0);
                Set(Selector.AntialiasingQuality, new[] { HDRP_UNAVAILABLE }, 0);
                return;
            }
            if (upscaler != 0)
            {
                SetSupported(Selector.Antialiasing, QUALITY, 0, false, "Задаёт апскейлер");
                SetSupported(Selector.AntialiasingQuality, QUALITY, 0, false, "Задаёт апскейлер");
                return;
            }
            _aaOptions.Clear();
            _aaOptions.Add(VideoAntialiasing.Off);
            _aaOptions.Add(VideoAntialiasing.Fxaa);
            _aaOptions.Add(VideoAntialiasing.Smaa);
            _labels.Clear();
            _labels.Add("Выкл.");
            _labels.Add("FXAA");
            _labels.Add("SMAA");
            if (_runtime.SupportsMotion)
            {
                _aaOptions.Add(VideoAntialiasing.Taa);
                _labels.Add("TAA");
            }
            int selected = _aaOptions.IndexOf(quality.antialiasing);
            Set(Selector.Antialiasing, _labels, Mathf.Max(0, selected));
            string reason = quality.antialiasing == VideoAntialiasing.Fxaa ? "FXAA без уровней" : "Сглаживание выключено";
            bool supported = selected >= 0 && VideoPresets.HasAntialiasingQuality(quality.antialiasing);
            SetSupported(Selector.AntialiasingQuality, QUALITY, quality.antialiasingQuality, supported, reason);
        }

        private void RefreshDialog()
        {
            bool confirming = _session.State == VideoApplyState.Confirming;
            bool menuDialog = _menuDialog != MenuDialog.None;
            bool open = menuDialog || _session.State != VideoApplyState.Idle;
            bool wasOpen = _dialog.activeSelf;
            if (open && !wasOpen)
            {
                _preDialogSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            }
            _options.interactable = !open;
            _options.blocksRaycasts = !open;
            _dialog.SetActive(open);
            if (!open)
            {
                if (wasOpen)
                {
                    GameObject selection = _preDialogSelection;
                    if (selection == null || !selection.activeInHierarchy || selection.TryGetComponent(out Selectable control) && !control.IsInteractable())
                    {
                        selection = _graphics.gameObject;
                    }
                    _preDialogSelection = null;
                    Select(selection);
                }
                return;
            }
            _dialogPrimary.gameObject.SetActive(menuDialog || confirming);
            _dialogSecondary.gameObject.SetActive(menuDialog || confirming);
            if (_menuDialog == MenuDialog.Reset)
            {
                _dialogTitle.text = "Сброс настроек видео";
                _dialogBody.text = "Вернуть все настройки видео к значениям по умолчанию?";
                _primaryLabel.text = "Сбросить";
                _secondaryLabel.text = "Отмена";
            }
            else if (_menuDialog == MenuDialog.Exit)
            {
                _dialogTitle.text = "Несохранённые изменения";
                _dialogBody.text = "Выйти из настроек без сохранения изменений?";
                _primaryLabel.text = "Выйти";
                _secondaryLabel.text = "Остаться";
            }
            else if (confirming)
            {
                _dialogTitle.text = "Сохранить режим экрана?";
                _dialogBody.text = "Предыдущий режим вернётся через " + _session.Seconds + " с.";
                _primaryLabel.text = "Сохранить";
                _secondaryLabel.text = "Вернуть";
            }
            else
            {
                _dialogTitle.text = _session.State == VideoApplyState.Restoring ? "Возврат режима экрана" : "Переключение режима экрана";
                _dialogBody.text = "";
            }
            if ((menuDialog || confirming) && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != _dialogPrimary.gameObject && EventSystem.current.currentSelectedGameObject != _dialogSecondary.gameObject)
            {
                Select(_dialogSecondary.gameObject);
            }
        }

        private void Set(Selector selector, IReadOnlyList<string> values, int index)
        {
            int slot = (int)selector;
            if (slot < _selectors.Length && _selectors[slot] != null)
            {
                _selectors[slot].SetOptions(values, index);
            }
        }

        private void SetSupported(Selector selector, string[] values, int index, bool supported, string reason)
        {
            Set(selector, supported ? values : new[] { reason }, supported ? index : 0);
        }

        private void Set(Range range, int value, string unit, string reason, string readout = null)
        {
            int slot = (int)range;
            if (slot >= _sliders.Length || slot >= _readouts.Length || _sliders[slot] == null || _readouts[slot] == null)
            {
                return;
            }
            Slider slider = _sliders[slot];
            slider.interactable = reason == null;
            slider.SetValueWithoutNotify(value);
            _readouts[slot].text = reason ?? readout ?? value.ToString() + unit;
        }

        private void Set(Switch toggle, bool value, bool supported = true)
        {
            Toggle control = _toggles[(int)toggle];
            control.interactable = supported;
            control.SetIsOnWithoutNotify(value);
        }

        private static string SizeLabel(VideoDisplay display)
        {
            return display.width + " x " + display.height;
        }

        private static void Select(GameObject target)
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(target);
            }
        }

        private void PlayMilitary()
        {
            LoadTestSceneAsync(MILITARY_SCENE).Forget();
        }

        private void PlayDemo()
        {
            LoadTestSceneAsync(DEMO_SCENE).Forget();
        }

        private async UniTask LoadTestSceneAsync(string scenePath)
        {
            if (!isActiveAndEnabled || _session != null || SceneLoader.IsLoading)
            {
                return;
            }
            if (!_mainMenu.TryGetComponent(out CanvasGroup controls))
            {
                controls = _mainMenu.AddComponent<CanvasGroup>();
            }
            bool wasInteractable = controls.interactable;
            controls.interactable = false;
            try
            {
                await SceneLoader.LoadAsync(scenePath, destroyCancellationToken);
            }
            finally
            {
                if (controls != null)
                {
                    controls.interactable = wasInteractable;
                }
            }
        }
    }
}
