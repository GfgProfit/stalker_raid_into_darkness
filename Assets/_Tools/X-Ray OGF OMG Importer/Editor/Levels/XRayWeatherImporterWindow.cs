using System;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayWeatherImporterWindow : EditorWindow
    {
        [SerializeField] private string _cycleName = "w_clear1";

        private XRayWeatherProfile _imported;
        private string _diagnostic;

        [MenuItem("Tools/X-Ray/Weather Profile Import")]
        public static void Open()
        {
            GetWindow<XRayWeatherImporterWindow>("RVM Weather Import");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Импорт погодного цикла РВМ", EditorStyles.boldLabel);
            _cycleName = EditorGUILayout.TextField("Исходный цикл", _cycleName);
            EditorGUILayout.HelpBox("Источник — активный профиль MO2 из действующей конфигурации. Импорт создаёт самостоятельный профиль; renderer неба и текстуры не импортируются.", MessageType.Info);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_cycleName)))
            {
                if (GUILayout.Button("Импортировать в новый Weather Profile")) Import();
            }
            if (!string.IsNullOrEmpty(_diagnostic)) EditorGUILayout.HelpBox(_diagnostic, _imported == null ? MessageType.Warning : MessageType.Info);
            if (_imported != null) EditorGUILayout.ObjectField("Созданный профиль", _imported, typeof(XRayWeatherProfile), false);
        }

        private void Import()
        {
            string path = EditorUtility.SaveFilePanelInProject("Import RVM Weather Profile", _cycleName + "_Weather", "asset", "Choose a new local weather profile asset.", "Assets/_Source/Levels");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                _imported = XRayWeatherImporter.ImportCurrent(_cycleName, path);
                _diagnostic = _imported.CycleName + ": " + _imported.KeyframeCount + " временных ключей. Source identity: " + _imported.SourceIdentity;
                EditorGUIUtility.PingObject(_imported);
            }
            catch (Exception exception)
            {
                _imported = null;
                _diagnostic = exception.Message;
                Debug.LogError("[X-Ray Weather] " + exception.Message);
            }
        }
    }
}
