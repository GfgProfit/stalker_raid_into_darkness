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
        private string _catalogDiagnostic;
        private string _transitionDiagnostic;
        private string _geometryDiagnostic;
        private float _transitionDuration = 10.0f;

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
            using (new EditorGUI.DisabledScope(playing || controller.Catalog != null))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_profile"), new GUIContent("Стартовый профиль сцены"));
            }
            DrawPropertiesExcluding(serializedObject, "m_Script", "_profile", "_catalog", "_lensFlareEnabled", "_lensFlareIntensity", "_adaptLensFlareToWeather", "_expressiveDaylight", "_expressiveFlareIntensity", "_flareProfileOverrides");
            DrawFlareSettings();
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
                using (new EditorGUI.DisabledScope(_selectedProfile == null || _selectedProfile == controller.AuthoredProfile))
                {
                    if (GUILayout.Button("Назначить стартовым профилем сцены"))
                    {
                        serializedObject.FindProperty("_profile").objectReferenceValue = _selectedProfile;
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
                    if (XRayWeatherPreview.Start(controller) && _selectedProfile != null && _selectedProfile != controller.ActiveProfile) controller.SetProfile(_selectedProfile);
                    SceneView.RepaintAll();
                }
            }
            if (playing || controller.PreviewActive) DrawSimulation(controller);
        }

        private void DrawFlareSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lens flare", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_lensFlareEnabled"), new GUIContent("Включён"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_lensFlareIntensity"), new GUIContent("Общая сила flare"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_adaptLensFlareToWeather"), new GUIContent("Адаптация к погоде"));
            SerializedProperty expressive = serializedObject.FindProperty("_expressiveDaylight");
            EditorGUILayout.PropertyField(expressive, new GUIContent("Expressive Daylight"));
            using (new EditorGUI.DisabledScope(!expressive.boolValue))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_expressiveFlareIntensity"), new GUIContent("Сила дополнительных бликов"));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_flareProfileOverrides"), new GUIContent("Исключения по профилям", "Дневная и лунная видимость прямого источника в выбранном цикле. 0 гасит оптические блики, 1 пропускает полностью."), true);
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
            using (new EditorGUI.DisabledScope(playing))
            {
                var catalog = (XRayWeatherCatalog)EditorGUILayout.ObjectField("Каталог погоды", controller.Catalog, typeof(XRayWeatherCatalog), false);
                if (catalog != controller.Catalog) AssignCatalog(controller, catalog);
                if (controller.Catalog == null && GUILayout.Button("Подключить подготовленный каталог"))
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
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(controller.Paused ? "Продолжить" : "Пауза")) controller.SetPaused(!controller.Paused);
                if (GUILayout.Button("Сбросить симуляцию"))
                {
                    controller.ResetSimulation();
                    _transitionDiagnostic = null;
                }
            }
            using (new EditorGUI.DisabledScope(_selectedProfile == null))
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
            using (new EditorGUI.DisabledScope(_selectedProfile == null || controller.IsTransitioning || !IsNonNegativeFinite(_transitionDuration)))
            {
                if (GUILayout.Button("Начать переход")) _transitionDiagnostic = controller.TransitionTo(_selectedProfile, _transitionDuration) ? null : controller.Diagnostic;
            }
            if (controller.IsTransitioning)
            {
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), controller.TransitionProgress, controller.Paused ? "Переход на паузе" : "Переход выполняется");
            }
            if (!string.IsNullOrEmpty(_transitionDiagnostic)) EditorGUILayout.HelpBox(_transitionDiagnostic, MessageType.Warning);
        }

        private static bool IsNonNegativeFinite(float value)
        {
            return value >= 0.0f && !float.IsInfinity(value);
        }
    }
}
