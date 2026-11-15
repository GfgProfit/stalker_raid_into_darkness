using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Rvm.Benchmark.Editor
{
    public sealed class BenchmarkWindow : EditorWindow
    {
        [SerializeField] private BenchmarkScenario _scenario;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform[] _positions = new Transform[4];
        [SerializeField] private string _baseline;
        [SerializeField] private string _candidate;
        [SerializeField] private double _tolerance;

        private Vector2 _scroll;
        private string _error;

        [MenuItem("Tools/RVM/Benchmark")]
        public static void ShowWindow()
        {
            GetWindow<BenchmarkWindow>("RVM Benchmark");
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Бенчмарк локации", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Frame pacing", "VSync=" + QualitySettings.vSyncCount + "; targetFrameRate=" + Application.targetFrameRate + "; refresh=" + Screen.currentResolution.refreshRateRatio.value.ToString("F2") + " Hz; interval=" + UnityEngine.Rendering.OnDemandRendering.renderFrameInterval);
            if (QualitySettings.vSyncCount != 0 || Application.targetFrameRate > 0 || UnityEngine.Rendering.OnDemandRendering.renderFrameInterval != 1)
            {
                EditorGUILayout.HelpBox("Обнаружен VSync или программный лимит кадров. Benchmark записывает фактические значения и не меняет их во время замера.", MessageType.Warning);
            }
            var selected = (BenchmarkScenario)EditorGUILayout.ObjectField("Scenario", _scenario, typeof(BenchmarkScenario), false);
            if (selected != _scenario)
            {
                _scenario = selected;
                TryAutoReferences();
            }
            if (GUILayout.Button("Создать и подключить сценарий текущей сцены"))
            {
                Execute(() => { _scenario = BenchmarkEditorSession.PrepareCurrentScene(); TryAutoReferences(); });
            }
            if (_scenario != null)
            {
                var serialized = new SerializedObject(_scenario);
                serialized.Update();
                EditorGUILayout.PropertyField(serialized.FindProperty("_positionSeconds"), new GUIContent("Секунд на позицию"));
                EditorGUILayout.PropertyField(serialized.FindProperty("_warmupSeconds"), new GUIContent("Прогрев, секунд"));
                EditorGUILayout.PropertyField(serialized.FindProperty("_stabilizationSeconds"), new GUIContent("Transition, секунд"));
                EditorGUILayout.PropertyField(serialized.FindProperty("_mode"), new GUIContent("Режим"));
                if ((BenchmarkMode)serialized.FindProperty("_mode").enumValueIndex == BenchmarkMode.Diagnostic)
                {
                    EditorGUILayout.LabelField("Diagnostic", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serialized.FindProperty("_diagnosticCpu"), new GUIContent("CPU markers"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("_diagnosticGpu"), new GUIContent("GPU observations"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("_diagnosticRendering"), new GUIContent("Rendering snapshots"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("_diagnosticMemory"), new GUIContent("Memory counters"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("_snapshotSeconds"), new GUIContent("Snapshot interval, seconds"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("_profilerCapture"), new GUIContent("Binary CPU capture"));
                    if (serialized.FindProperty("_profilerCapture").boolValue)
                    {
                        EditorGUILayout.PropertyField(serialized.FindProperty("_captureSeconds"), new GUIContent("Capture budget, seconds (<=4)"));
                    }
                    EditorGUILayout.HelpBox("Inclusive CPU markers нельзя складывать. GPU observations запаздывают и не привязаны к исходному POS. Процедура GPU capture и анализ CPU Timeline описаны в diagnostic-report.md каждого запуска.", MessageType.Info);
                }
                else
                {
                    serialized.FindProperty("_profilerCapture").boolValue = false;
                }
                EditorGUILayout.LabelField("Условия запуска", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serialized.FindProperty("_fixedWeatherTime"), new GUIContent("Фиксировать погоду / часы"));
                if (serialized.FindProperty("_fixedWeatherTime").boolValue)
                {
                    EditorGUILayout.PropertyField(serialized.FindProperty("_fixedTimeHours"), new GUIContent("Час [0, 24)"));
                    EditorGUILayout.PropertyField(serialized.FindProperty("_expectedWeatherProfile"), new GUIContent("Ожидаемый профиль"));
                    EditorGUILayout.HelpBox("Используется уже выбранный стабильный профиль. Переходы/FX запрещены; часы ставятся на паузу и восстанавливаются после остановки. Аномалии, NPC и физика продолжают работу.", MessageType.Info);
                }
                serialized.ApplyModifiedProperties();
                EditorGUILayout.LabelField("Измеряемое время", BenchmarkOutput.Number(_scenario.PositionSeconds * 4) + " секунд; основной протокол — 4 × 5");
                _camera = (Camera)EditorGUILayout.ObjectField("Рабочая камера", _camera, typeof(Camera), true);
                for (int i = 0; i < 4; i++)
                {
                    _positions[i] = (Transform)EditorGUILayout.ObjectField("POS " + (i + 1), _positions[i], typeof(Transform), true);
                    if (_positions[i] != null)
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.Vector3Field("Position", _positions[i].position);
                            EditorGUILayout.Vector3Field("Rotation", _positions[i].eulerAngles);
                        }
                    }
                }
                if (GUILayout.Button("Подключить выбранные ссылки (с Undo)"))
                {
                    Execute(() => BenchmarkEditorSession.Connect(_scenario, _camera, _positions, false));
                }
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
                {
                    if (GUILayout.Button("Запустить один прогон"))
                    {
                        Execute(() => BenchmarkEditorSession.Launch(_scenario, 1));
                    }
                    if (GUILayout.Button("Запустить серию из трёх прогонов"))
                    {
                        Execute(() => BenchmarkEditorSession.Launch(_scenario, 3));
                    }
                }
            }
            EditorGUILayout.Space();
            if (GUILayout.Button("Открыть результаты"))
            {
                Directory.CreateDirectory(BenchmarkOutput.DEFAULT_ROOT);
                EditorUtility.RevealInFinder(BenchmarkOutput.DEFAULT_ROOT);
            }
            if (GUILayout.Button("Открыть последний отчёт") && File.Exists(BenchmarkEditorSession.LastReport))
            {
                Application.OpenURL(new Uri(BenchmarkEditorSession.LastReport).AbsoluteUri);
            }
            if (GUILayout.Button("Открыть Diagnostic Report / capture workflow"))
            {
                string path = string.IsNullOrEmpty(BenchmarkEditorSession.LastReport) ? "" : Path.Combine(Path.GetDirectoryName(BenchmarkEditorSession.LastReport), "diagnostic", "diagnostic-report.md");
                if (File.Exists(path))
                {
                    Application.OpenURL(new Uri(path).AbsoluteUri);
                }
                else
                {
                    _error = "Нет Diagnostic Report: выполните прогон в режиме Diagnostic.";
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Сравнение сохранённых прогонов", EditorStyles.boldLabel);
            _baseline = EditorGUILayout.TextField("Baseline", _baseline);
            if (GUILayout.Button("Выбрать Baseline report / summary"))
            {
                SelectReport(ref _baseline);
            }
            _candidate = EditorGUILayout.TextField("Candidate", _candidate);
            if (GUILayout.Button("Выбрать Candidate report / summary"))
            {
                SelectReport(ref _candidate);
            }
            _tolerance = EditorGUILayout.DoubleField("Практический порог, %", _tolerance);
            EditorGUILayout.HelpBox("Порог задаёт пользователь. Направление изменения не доказывает статистическую значимость. Различия условий блокируют корректное A/B-сравнение.", MessageType.Info);
            if (GUILayout.Button("Сравнить и сохранить отчёт"))
            {
                Execute(() => { string path = BenchmarkComparison.SaveComparison(_baseline, _candidate, _tolerance); Application.OpenURL(new Uri(path).AbsoluteUri); });
            }
            if (!string.IsNullOrEmpty(_error))
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            }
            EditorGUILayout.EndScrollView();
        }

        private void TryAutoReferences()
        {
            if (_scenario != null)
            {
                Execute(() => BenchmarkEditorSession.AutoReferences(_scenario, out _camera, out _positions));
            }
        }

        private void Execute(Action action)
        {
            try
            {
                _error = null;
                action();
            }
            catch (Exception exception)
            {
                _error = exception.Message;
            }
        }

        private static void SelectReport(ref string target)
        {
            string path = EditorUtility.OpenFilePanel("Benchmark report", BenchmarkOutput.DEFAULT_ROOT, "");
            if (!string.IsNullOrEmpty(path))
            {
                target = path;
            }
        }
    }
}
