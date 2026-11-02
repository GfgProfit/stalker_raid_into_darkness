using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    [CustomEditor(typeof(XRayWeatherController))]
    public sealed class XRayWeatherControllerEditor : UnityEditor.Editor
    {
        private XRayWeatherProfile _transitionTarget;
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
            if (!string.IsNullOrEmpty(controller.Diagnostic)) EditorGUILayout.HelpBox(controller.Diagnostic, MessageType.Warning);
            bool playing = Application.IsPlaying(controller.gameObject);
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
    }
}
