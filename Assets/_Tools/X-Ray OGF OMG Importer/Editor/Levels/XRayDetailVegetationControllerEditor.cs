using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    [CustomEditor(typeof(XRayDetailVegetationController))]
    public sealed class XRayDetailVegetationControllerEditor : UnityEditor.Editor
    {
        private string _pendingProfile;
        private string _sourceStatus;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty density = serializedObject.FindProperty("_density");
            SerializedProperty radius = serializedObject.FindProperty("_radius");
            SerializedProperty height = serializedObject.FindProperty("_height");
            SerializedProperty shadows = serializedObject.FindProperty("_shadows");
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Плотность травы");
                EditorGUI.BeginChangeCheck();
                float slider = GUILayout.HorizontalSlider(1.04f - density.floatValue, 0.04f, 1.0f);
                if (EditorGUI.EndChangeCheck()) density.floatValue = Mathf.Clamp(0.04f + Mathf.Round((1.0f - slider) / 0.02f) * 0.02f, 0.04f, 1.0f);
                density.floatValue = EditorGUILayout.FloatField(density.floatValue, GUILayout.Width(65.0f));
            }

            radius.intValue = EditorGUILayout.IntSlider("Дистанция травы", radius.intValue, 50, 250);
            height.floatValue = EditorGUILayout.Slider("Высота травы", height.floatValue, 0.5f, 2.0f);
            EditorGUILayout.PropertyField(shadows, new GUIContent("Тени от травы"));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(targets, "Change X-Ray grass settings");
                serializedObject.ApplyModifiedProperties();
                foreach (Object item in targets) ((XRayDetailVegetationController)item).ApplyUserValues();
            }

            if (GUILayout.Button("Обновить настройки из РВМ")) XRayRvmGrassSource.TryRead(out _pendingProfile, out _sourceStatus);
            if (!string.IsNullOrEmpty(_sourceStatus)) EditorGUILayout.HelpBox(_sourceStatus, _pendingProfile == null ? MessageType.Warning : MessageType.Info);
            if (_pendingProfile != null)
            {
                XRayGrassValues old = ((XRayDetailVegetationController)target).RvmValues;
                XRayGrassValues next = XRayGrassDefaults.Load(_pendingProfile).ForLevel(null);
                EditorGUILayout.LabelField("Плотность РВМ", old.Density + " → " + next.Density);
                EditorGUILayout.LabelField("Дистанция РВМ", old.Radius + " → " + next.Radius);
                EditorGUILayout.LabelField("Высота РВМ", old.Height + " → " + next.Height);
                EditorGUILayout.LabelField("Тени РВМ", old.Shadows + " → " + next.Shadows);
                int mask = ((XRayDetailVegetationController)target).OverrideMask;
                string overrides = ((mask & 1) != 0 ? "плотность, " : "") + ((mask & 2) != 0 ? "дистанция, " : "") + ((mask & 4) != 0 ? "высота, " : "") + ((mask & 8) != 0 ? "тени, " : "");
                EditorGUILayout.LabelField("Пользовательские настройки", mask == 0 ? "нет" : overrides.TrimEnd(',', ' '));
                if (GUILayout.Button("Применить профиль РВМ, сохранив overrides"))
                {
                    bool verified = XRayRvmGrassSource.TryRead(out string current, out _sourceStatus);
                    if (verified && current == _pendingProfile)
                    {
                        Undo.RecordObjects(targets, "Refresh X-Ray RVM grass settings");
                        foreach (Object item in targets)
                        {
                            ((XRayDetailVegetationController)item).RefreshFromRvm(current);
                            EditorUtility.SetDirty(item);
                        }
                        _pendingProfile = null;
                    }
                    else
                    {
                        _pendingProfile = current;
                        if (verified) _sourceStatus = "Исходные настройки изменились. Проверьте новые значения.";
                    }
                }
            }

            if (GUILayout.Button("Сбросить к настройкам РВМ"))
            {
                Undo.RecordObjects(targets, "Reset X-Ray grass settings");
                foreach (Object item in targets)
                {
                    ((XRayDetailVegetationController)item).ResetToRvm();
                    EditorUtility.SetDirty(item);
                }
            }
        }
    }

    [InitializeOnLoad]
    internal static class XRayDetailVegetationScenePreview
    {
        private const double UPDATE_INTERVAL = 1.0 / 30.0;

        private static bool _requested;
        private static double _nextUpdate;

        static XRayDetailVegetationScenePreview()
        {
            XRayDetailVegetationController.PreviewRequested += Request;
            EditorApplication.update += Update;
        }

        private static void Request()
        {
            _requested = true;
        }

        private static void Update()
        {
            if (!_requested || Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || SceneView.sceneViews.Count == 0 || EditorApplication.timeSinceStartup < _nextUpdate) return;
            _requested = false;
            _nextUpdate = EditorApplication.timeSinceStartup + UPDATE_INTERVAL;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
    }
}
