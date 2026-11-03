using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    [CustomEditor(typeof(XRayWeatherController))]
    public sealed class XRayWeatherControllerEditor : UnityEditor.Editor
    {
        private XRayWeatherProfile _transitionTarget;
        private XRayWeatherProfile _clearProfile;
        private XRayWeatherProfile _rainProfile;
        private XRayWeatherProfile _quickSource;
        private float _transitionDuration = 10.0f;

        public override bool RequiresConstantRepaint()
        {
            var controller = (XRayWeatherController)target;
            return controller != null && (controller.PreviewActive || Application.IsPlaying(controller.gameObject));
        }

        public override void OnInspectorGUI()
        {
            var controller = (XRayWeatherController)target;
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                controller.ApplySettings();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Текущее время", System.TimeSpan.FromSeconds(controller.GameTimeSeconds).ToString(@"hh\:mm\:ss"));
            bool playing = Application.IsPlaying(controller.gameObject);
            if (playing || controller.PreviewActive) EditorGUILayout.LabelField("Текущий цикл", controller.ActiveProfile == null ? "—" : controller.ActiveProfile.CycleName);
            if (!string.IsNullOrEmpty(controller.Diagnostic)) EditorGUILayout.HelpBox(controller.Diagnostic, MessageType.Warning);
            if (!playing)
            {
                if (XRayWeatherPreview.Controller == controller)
                {
                    if (GUILayout.Button("Завершить предпросмотр погоды")) XRayWeatherPreview.Stop();
                }
                else if (GUILayout.Button("Предпросмотр погоды в Scene View"))
                {
                    XRayWeatherPreview.Start(controller);
                }
            }

            using (new EditorGUI.DisabledScope(!playing && !controller.PreviewActive))
            {
                if (_quickSource != controller.ActiveProfile)
                {
                    _quickSource = controller.ActiveProfile;
                    _clearProfile = QuickProfile(_quickSource, "w_clear1");
                    _rainProfile = QuickProfile(_quickSource, "w_rain1");
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(_clearProfile == null))
                    {
                        if (GUILayout.Button("Ясно (w_clear1)")) controller.SetProfile(_clearProfile);
                    }
                    using (new EditorGUI.DisabledScope(_rainProfile == null))
                    {
                        if (GUILayout.Button("Дождь (w_rain1)")) controller.SetProfile(_rainProfile);
                    }
                }
                EditorGUI.BeginChangeCheck();
                float hour = EditorGUILayout.Slider("Время суток", (float)(controller.GameTimeSeconds / 3600.0), 0.0f, 24.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    controller.Seek(hour * 3600.0);
                    EditorApplication.QueuePlayerLoopUpdate();
                    SceneView.RepaintAll();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(controller.Paused ? "Продолжить" : "Пауза")) controller.SetPaused(!controller.Paused);
                    if (GUILayout.Button("Сбросить симуляцию")) controller.ResetSimulation();
                }
                _transitionTarget = (XRayWeatherProfile)EditorGUILayout.ObjectField("Переход к профилю", _transitionTarget, typeof(XRayWeatherProfile), false);
                _transitionDuration = Mathf.Max(0.0f, EditorGUILayout.FloatField("Длительность перехода (с)", _transitionDuration));
                using (new EditorGUI.DisabledScope(_transitionTarget == null))
                {
                    if (GUILayout.Button("Начать переход")) controller.TransitionTo(_transitionTarget, _transitionDuration);
                }
            }
        }

        private void OnDisable()
        {
            if (target != null && XRayWeatherPreview.Controller == target) XRayWeatherPreview.Stop();
        }

        private static XRayWeatherProfile QuickProfile(XRayWeatherProfile source, string cycle)
        {
            if (source == null) return null;
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(source))?.Replace('\\', '/');
            XRayWeatherProfile profile = string.IsNullOrEmpty(folder) ? null : AssetDatabase.LoadAssetAtPath<XRayWeatherProfile>(folder + "/" + cycle + ".asset");
            return profile != null && profile.CycleName == cycle && string.Equals(profile.SourceIdentity, source.SourceIdentity, StringComparison.Ordinal) && XRayWeatherImporter.HasSerializedResources(profile) ? profile : null;
        }
    }
}
