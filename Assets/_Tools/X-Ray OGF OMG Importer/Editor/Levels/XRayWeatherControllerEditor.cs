using System;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    [CustomEditor(typeof(XRayWeatherController))]
    public sealed class XRayWeatherControllerEditor : UnityEditor.Editor
    {
        private XRayWeatherProfile _selectedProfile;
        private XRayWeatherFxProfile _selectedFx;
        private XRayWeatherFxCatalog _pickerFxCatalog;
        private string _catalogDiagnostic;
        private string _transitionDiagnostic;
        private string _geometryDiagnostic;
        private string _fxDiagnostic;
        private float _transitionDuration = 10.0f;
        private float _fxStartSeconds;

        public override bool RequiresConstantRepaint()
        {
            var controller = (XRayWeatherController)target;
            return controller != null && (controller.PreviewActive || Application.IsPlaying(controller.gameObject));
        }

        public override void OnInspectorGUI()
        {
            var controller = (XRayWeatherController)target;
            bool playing = Application.IsPlaying(controller.gameObject);
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            var flags = serializedObject.FindProperty("_levelOverrides");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("GameConfig", controller.Configuration, typeof(GameConfig), false);
            }
            using (new EditorGUI.DisabledScope(playing || controller.ActiveFx != null))
            {
                EditorGUILayout.PropertyField(flags, new GUIContent("Переопределения локации"));
                DrawOverride(flags, XRayWeatherLevelOverrides.TimeOfDay, "_timeOfDayHours", "Стартовое время", controller.ConfiguredTimeOfDay.ToString("0.###"));
                DrawOverride(flags, XRayWeatherLevelOverrides.TimeScale, "_timeScale", "Масштаб времени", controller.ConfiguredTimeScale.ToString("0.###"));
                DrawOverride(flags, XRayWeatherLevelOverrides.WeatherProfile, "_profile", "Стартовый профиль", controller.AuthoredProfile != null ? controller.AuthoredProfile.CycleName : "—");
                DrawOverride(flags, XRayWeatherLevelOverrides.Fog, "_fogEnabled", "Weather Fog", EnabledText(controller.FogEnabled), controller.FogOverride.HasValue);
                DrawOverride(flags, XRayWeatherLevelOverrides.SunShafts, "_sunShaftsEnabled", "Sun Shafts", EnabledText(controller.SunShaftsEnabled), controller.SunShaftsOverride.HasValue);
                DrawOverride(flags, XRayWeatherLevelOverrides.CelestialSettings, "_celestialSettings", "Профиль Celestial", controller.Configuration != null && controller.Configuration.CelestialSettings != null ? controller.Configuration.CelestialSettings.name : "—");
                DrawFlareSettings(controller, flags);
                DrawOverride(flags, XRayWeatherLevelOverrides.FxLightingAdaptation, "_fxLightingAdaptation", "Адаптация освещения WeatherFX", controller.FxLightingAdaptationGain.ToString("0.###"), controller.FxLightingAdaptationOverride.HasValue);
            }
            DrawPropertiesExcluding(serializedObject, "m_Script", "_levelOverrides", "_timeOfDayHours", "_timeScale", "_profile", "_catalog", "_fxCatalog", "_fogEnabled", "_sunShaftsEnabled", "_celestialSettings", "_lensFlareEnabled", "_lensFlareIntensity", "_adaptLensFlareToWeather", "_expressiveDaylight", "_expressiveFlareIntensity", "_flareProfileOverrides", "_fxLightingAdaptation");
            string timeSource = controller.TimeScaleOverride.HasValue ? "Runtime" : (controller.LevelOverrides & XRayWeatherLevelOverrides.TimeScale) != 0 ? "Level" : "Global";
            EditorGUILayout.LabelField("Эффективный масштаб времени", $"{controller.TimeScale:0.###} ({timeSource})");
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                controller.ApplySettings();
            }

            DrawFlareDiagnostics(controller);
            DrawCatalog(controller, playing);
            EditorGUILayout.Space();
            XRayWeatherProfile preferredProfile = playing || controller.PreviewActive ? controller.ActiveProfile : controller.AuthoredProfile;
            DrawProfilePicker(controller.Catalog, preferredProfile);
            if (!playing && controller.Catalog != null)
            {
                bool hasProfileOverride = (controller.LevelOverrides & XRayWeatherLevelOverrides.WeatherProfile) != 0;
                using (new EditorGUI.DisabledScope(_selectedProfile == null || hasProfileOverride && _selectedProfile == controller.AuthoredProfile || controller.ActiveFx != null))
                {
                    if (GUILayout.Button("Назначить стартовым профилем сцены"))
                    {
                        serializedObject.FindProperty("_profile").objectReferenceValue = _selectedProfile;
                        flags.intValue |= (int)XRayWeatherLevelOverrides.WeatherProfile;
                        serializedObject.ApplyModifiedProperties();
                        controller.ApplySettings();
                    }
                }
            }

            EditorGUILayout.Space();
            if (!string.IsNullOrEmpty(controller.Diagnostic)) EditorGUILayout.HelpBox(controller.Diagnostic, MessageType.Warning);
            if (!playing)
            {
                if (XRayWeatherPreview.Controller == controller)
                {
                    if (GUILayout.Button("Завершить предпросмотр погоды")) XRayWeatherPreview.Stop();
                }
                else if (GUILayout.Button("Предпросмотр погоды в Scene View"))
                {
                    _transitionDiagnostic = null;
                    _geometryDiagnostic = null;
                    _fxDiagnostic = null;
                    if (XRayWeatherPreview.Start(controller) && _selectedProfile != null && _selectedProfile != controller.ActiveProfile) controller.SetProfile(_selectedProfile);
                    SceneView.RepaintAll();
                }
            }
            if (playing || controller.PreviewActive) DrawSimulation(controller);
            DrawFxControls(controller, playing);
        }

        private void DrawFlareSettings(XRayWeatherController controller, SerializedProperty flags)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lens flare", EditorStyles.boldLabel);
            DrawOverride(flags, XRayWeatherLevelOverrides.LensFlare, "_lensFlareEnabled", "Включён", EnabledText(controller.LensFlareEnabled), controller.LensFlareOverride.HasValue);
            DrawOverride(flags, XRayWeatherLevelOverrides.LensFlareIntensity, "_lensFlareIntensity", "Общая сила flare", controller.LensFlareIntensity.ToString("0.###"), controller.LensFlareIntensityOverride.HasValue);
            DrawOverride(flags, XRayWeatherLevelOverrides.FlareAdaptation, "_adaptLensFlareToWeather", "Адаптация к погоде", EnabledText(controller.AdaptLensFlareToWeather), controller.FlareAdaptationOverride.HasValue);
            DrawOverride(flags, XRayWeatherLevelOverrides.ExpressiveDaylight, "_expressiveDaylight", "Expressive Daylight", EnabledText(controller.ExpressiveDaylight));
            DrawOverride(flags, XRayWeatherLevelOverrides.ExpressiveFlareIntensity, "_expressiveFlareIntensity", "Сила дополнительных бликов", controller.ExpressiveFlareIntensity.ToString("0.###"), controller.ExpressiveFlareIntensityOverride.HasValue);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_flareProfileOverrides"), new GUIContent("Исключения по профилям", "Дневная и лунная видимость прямого источника в выбранном цикле. 0 гасит оптические блики, 1 пропускает полностью."), true);
        }

        private void DrawOverride(SerializedProperty flags, XRayWeatherLevelOverrides flag, string property, string label, string effective, bool runtime = false)
        {
            if ((flags.intValue & (int)flag) != 0)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(property), new GUIContent(label));
            }
            else
            {
                EditorGUILayout.LabelField(label, effective + (runtime ? " (Runtime)" : " (Global)"));
            }
        }

        private static string EnabledText(bool enabled)
        {
            return enabled ? "Вкл" : "Выкл";
        }

        private void DrawFlareDiagnostics(XRayWeatherController controller)
        {
            XRayWeatherCelestialRenderer celestial = controller.Celestial;
            if (celestial == null) return;
            string requested = controller.Snapshot.SunDefinition;
            EditorGUILayout.LabelField("Определение из погоды", string.IsNullOrEmpty(requested) ? "Пустое" : requested);
            EditorGUILayout.LabelField("Определение renderer", celestial.FlareDefinition?.Name ?? "—");
            EditorGUILayout.LabelField("Яркость источника, лк", celestial.SourceBrightness.ToString("0.###"));
            EditorGUILayout.LabelField("Погодная видимость", celestial.WeatherVisibility.ToString("0.###"));
            EditorGUILayout.LabelField("Сила IgnoreColor", celestial.UntintedBrightness.ToString("0.###"));
            EditorGUILayout.LabelField("Временное затухание", celestial.FlareFade.ToString("0.###"));
            EditorGUILayout.LabelField("Коэффициент RGB до depth и fade", celestial.EffectiveIntensity.ToString("0.###"));
            float? visibility = celestial.GeometryVisibility;
            string camera = string.IsNullOrEmpty(celestial.GeometryCamera) ? "" : " · " + celestial.GeometryCamera;
            EditorGUILayout.LabelField(new GUIContent("Геометрия оптики", "Пять выборок HDRP depth вокруг источника. Для диска применяется попиксельный depth."), new GUIContent(visibility.HasValue ? visibility.Value.ToString("P0") + camera : "Нет замера"));
            if (GUILayout.Button("Измерить геометрическую видимость"))
            {
                _geometryDiagnostic = celestial.RequestGeometrySample() ? null : "Замер depth сейчас недоступен.";
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
            if (!string.IsNullOrEmpty(_geometryDiagnostic)) EditorGUILayout.HelpBox(_geometryDiagnostic, MessageType.Info);
        }

        private void DrawCatalog(XRayWeatherController controller, bool playing)
        {
            bool local = (controller.LevelOverrides & XRayWeatherLevelOverrides.WeatherCatalog) != 0;
            using (new EditorGUI.DisabledScope(!local || playing || controller.ActiveFx != null))
            {
                var catalog = (XRayWeatherCatalog)EditorGUILayout.ObjectField(local ? "Каталог погоды (Level)" : "Каталог погоды (Global)", controller.Catalog, typeof(XRayWeatherCatalog), false);
                if (catalog != controller.Catalog) AssignCatalog(controller, catalog);
                if (local && controller.Catalog == null && GUILayout.Button("Подключить подготовленный каталог"))
                {
                    try
                    {
                        catalog = XRayWeatherImporter.LoadPreparedCatalog();
                        if (catalog != null) AssignCatalog(controller, catalog);
                        else _catalogDiagnostic = "Подготовленный каталог погоды отсутствует.";
                    }
                    catch (Exception exception)
                    {
                        _catalogDiagnostic = exception.Message;
                    }
                }
            }
            if (!string.IsNullOrEmpty(_catalogDiagnostic)) EditorGUILayout.HelpBox(_catalogDiagnostic, MessageType.Warning);
        }

        private void AssignCatalog(XRayWeatherController controller, XRayWeatherCatalog catalog)
        {
            if (catalog != null && controller.AuthoredProfile != null && !catalog.CanAttachTo(controller.AuthoredProfile))
            {
                _catalogDiagnostic = "Каталог несовместим со стартовым профилем сцены.";
                return;
            }
            _catalogDiagnostic = null;
            _transitionDiagnostic = null;
            serializedObject.FindProperty("_catalog").objectReferenceValue = catalog;
            serializedObject.ApplyModifiedProperties();
            controller.ApplySettings();
        }

        private void DrawProfilePicker(XRayWeatherCatalog catalog, XRayWeatherProfile preferredProfile)
        {
            if (catalog == null)
            {
                EditorGUI.BeginChangeCheck();
                _selectedProfile = (XRayWeatherProfile)EditorGUILayout.ObjectField("Выбранный профиль", _selectedProfile, typeof(XRayWeatherProfile), false);
                if (EditorGUI.EndChangeCheck()) _transitionDiagnostic = null;
                return;
            }
            if (!catalog.Contains(_selectedProfile)) _selectedProfile = catalog.Contains(preferredProfile) ? preferredProfile : null;
            Rect button = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent("Выбранный цикл"));
            if (!EditorGUI.DropdownButton(button, new GUIContent(_selectedProfile != null ? _selectedProfile.CycleName : "Выбрать"), FocusType.Keyboard)) return;
            var menu = new GenericMenu();
            foreach (XRayWeatherProfile profile in catalog.Profiles)
            {
                if (profile == null) continue;
                menu.AddItem(new GUIContent(profile.CycleName), _selectedProfile == profile, () =>
                {
                    _selectedProfile = profile;
                    _transitionDiagnostic = null;
                    Repaint();
                });
            }
            menu.DropDown(button);
        }

        private void DrawSimulation(XRayWeatherController controller)
        {
            EditorGUILayout.LabelField(controller.IsTransitioning ? "Целевой цикл" : "Текущий цикл", controller.ActiveProfile == null ? "—" : controller.ActiveProfile.CycleName);
            EditorGUILayout.LabelField("Текущее время", TimeSpan.FromSeconds(controller.GameTimeSeconds).ToString(@"hh\:mm\:ss"));
            EditorGUI.BeginChangeCheck();
            float hour = EditorGUILayout.Slider("Время суток", (float)(controller.GameTimeSeconds / 3600.0), 0.0f, 24.0f);
            if (EditorGUI.EndChangeCheck())
            {
                controller.Seek(hour * 3600.0);
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
            float scale = EditorGUILayout.DelayedFloatField("Масштаб игрового времени", controller.TimeScale);
            if (scale != controller.TimeScale && IsNonNegativeFinite(scale)) controller.SetTimeScale(scale);
            if (controller.TimeScaleOverride.HasValue && GUILayout.Button("Сбросить переопределение времени сессии")) controller.ClearTimeScaleOverride();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(controller.Paused ? "Продолжить" : "Пауза")) controller.SetPaused(!controller.Paused);
                using (new EditorGUI.DisabledScope(controller.ActiveFx != null))
                {
                    if (GUILayout.Button("Сбросить симуляцию"))
                    {
                        controller.ResetSimulation();
                        _transitionDiagnostic = null;
                    }
                }
            }
            using (new EditorGUI.DisabledScope(_selectedProfile == null || controller.ActiveFx != null))
            {
                if (GUILayout.Button(controller.IsTransitioning ? "Прервать переход и применить цикл" : "Применить цикл сразу"))
                {
                    controller.SetProfile(_selectedProfile);
                    _transitionDiagnostic = null;
                    EditorApplication.QueuePlayerLoopUpdate();
                    SceneView.RepaintAll();
                }
            }
            _transitionDuration = EditorGUILayout.DelayedFloatField("Длительность перехода (с)", _transitionDuration);
            using (new EditorGUI.DisabledScope(_selectedProfile == null || controller.IsTransitioning || controller.ActiveFx != null || !IsNonNegativeFinite(_transitionDuration)))
            {
                if (GUILayout.Button("Начать переход")) _transitionDiagnostic = controller.TransitionTo(_selectedProfile, _transitionDuration) ? null : controller.Diagnostic;
            }
            if (controller.IsTransitioning)
            {
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), controller.TransitionProgress, controller.Paused ? "Переход на паузе" : "Переход выполняется");
            }
            if (!string.IsNullOrEmpty(_transitionDiagnostic)) EditorGUILayout.HelpBox(_transitionDiagnostic, MessageType.Warning);
        }

        private void DrawFxControls(XRayWeatherController controller, bool playing)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Погодные эффекты", EditorStyles.boldLabel);
            bool local = (controller.LevelOverrides & XRayWeatherLevelOverrides.WeatherFxCatalog) != 0;
            using (new EditorGUI.DisabledScope(!local || playing || controller.ActiveFx != null))
            {
                var catalog = (XRayWeatherFxCatalog)EditorGUILayout.ObjectField(local ? "Каталог FX (Level)" : "Каталог FX (Global)", controller.FxCatalog, typeof(XRayWeatherFxCatalog), false);
                if (catalog != controller.FxCatalog) AssignFxCatalog(controller, catalog);
                if (local && controller.FxCatalog == null && GUILayout.Button("Подключить подготовленный каталог FX"))
                {
                    try
                    {
                        catalog = XRayWeatherImporter.LoadPreparedFxCatalog();
                        if (catalog != null) AssignFxCatalog(controller, catalog);
                        else _fxDiagnostic = "Подготовленный каталог FX отсутствует.";
                    }
                    catch (Exception exception)
                    {
                        _fxDiagnostic = exception.Message;
                    }
                }
            }
            if (controller.FxCatalog != null)
            {
                DrawFxPicker(controller.FxCatalog, controller.ActiveFx);
                _fxStartSeconds = EditorGUILayout.DelayedFloatField("Позиция запуска (игровые с)", _fxStartSeconds);
                bool simulating = playing || controller.PreviewActive;
                if (!simulating) EditorGUILayout.HelpBox("Запустите предпросмотр погоды, чтобы применить FX.", MessageType.Info);
                if (controller.IsTransitioning) EditorGUILayout.HelpBox("При запуске FX обычный переход завершится на целевом цикле.", MessageType.Info);
                using (new EditorGUI.DisabledScope(!simulating || _selectedFx == null || !IsNonNegativeFinite(_fxStartSeconds)))
                {
                    if (GUILayout.Button("Запустить FX"))
                    {
                        _fxDiagnostic = controller.StartFx(_selectedFx, _fxStartSeconds) ? null : controller.Diagnostic;
                        EditorApplication.QueuePlayerLoopUpdate();
                        SceneView.RepaintAll();
                    }
                }
            }
            if (controller.ActiveFx != null)
            {
                bool sourceLightingOnly = controller.FxLightingAdaptationOverride == 0.0f;
                if (EditorGUILayout.Toggle("Только исходное освещение FX", sourceLightingOnly) != sourceLightingOnly) controller.SetFxLightingAdaptationOverride(sourceLightingOnly ? (float?)null : 0.0f);
                EditorGUILayout.LabelField("Активный FX", controller.ActiveFx.EffectName);
                EditorGUILayout.LabelField("Фаза FX", controller.Paused ? controller.FxPhase + " (пауза)" : controller.FxPhase.ToString());
                EditorGUILayout.LabelField("Время FX, игровые с", FormattableString.Invariant($"{controller.FxTimeSeconds:0.##} / {controller.FxDurationSeconds:0.##}"));
                EditorGUI.BeginChangeCheck();
                float seconds = EditorGUILayout.Slider("Позиция FX (игровые с)", (float)controller.FxTimeSeconds, 0.0f, (float)controller.FxDurationSeconds);
                if (EditorGUI.EndChangeCheck())
                {
                    _fxDiagnostic = controller.SeekFx(Math.Min(seconds, controller.FxDurationSeconds)) ? null : controller.Diagnostic;
                    EditorApplication.QueuePlayerLoopUpdate();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Остановить и вернуть погоду"))
                {
                    _fxDiagnostic = controller.StopFx() ? null : controller.Diagnostic;
                    EditorApplication.QueuePlayerLoopUpdate();
                    SceneView.RepaintAll();
                }
            }
            if (!string.IsNullOrEmpty(_fxDiagnostic)) EditorGUILayout.HelpBox(_fxDiagnostic, MessageType.Warning);
        }

        private void AssignFxCatalog(XRayWeatherController controller, XRayWeatherFxCatalog catalog)
        {
            if (catalog != null && !catalog.CanAttachTo(controller.AuthoredProfile))
            {
                _fxDiagnostic = "Каталог FX несовместим со стартовым профилем сцены.";
                return;
            }
            _fxDiagnostic = null;
            serializedObject.FindProperty("_fxCatalog").objectReferenceValue = catalog;
            serializedObject.ApplyModifiedProperties();
            controller.ApplySettings();
        }

        private void DrawFxPicker(XRayWeatherFxCatalog catalog, XRayWeatherFxProfile active)
        {
            if (_pickerFxCatalog != catalog)
            {
                _pickerFxCatalog = catalog;
                _selectedFx = active;
            }
            Rect button = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent("Выбранный FX"));
            if (!EditorGUI.DropdownButton(button, new GUIContent(_selectedFx != null ? _selectedFx.EffectName : "Выбрать"), FocusType.Keyboard)) return;
            var menu = new GenericMenu();
            foreach (XRayWeatherFxProfile effect in catalog.Effects)
            {
                if (effect == null) continue;
                menu.AddItem(new GUIContent(effect.EffectName), _selectedFx == effect, () =>
                {
                    _selectedFx = effect;
                    _fxDiagnostic = null;
                    Repaint();
                });
            }
            menu.DropDown(button);
        }

        private static bool IsNonNegativeFinite(float value)
        {
            return value >= 0.0f && !float.IsInfinity(value);
        }
    }
}
