using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor.Levels
{
    [CustomPropertyDrawer(typeof(XRayLevelAnomalyEntry))]
    public sealed class XRayLevelAnomalyEntryDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * (property.isExpanded ? 4f : 1f) + EditorGUIUtility.standardVerticalSpacing * (property.isExpanded ? 3f : 0f);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty section = property.FindPropertyRelative("_section");
            SerializedProperty displayName = property.FindPropertyRelative("_displayName");
            SerializedProperty prefab = property.FindPropertyRelative("_prefab");
            string title = string.IsNullOrWhiteSpace(displayName.stringValue) ? section.stringValue : displayName.stringValue;
            string tooltip = title + " (" + section.stringValue + ")";
            Rect row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginProperty(position, label, property);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, new GUIContent(tooltip, title), true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.PropertyField(row, section, new GUIContent("Section", "Точное имя секции аномалии в all.spawn."));
                row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.PropertyField(row, displayName, new GUIContent("Название", "Русское название аномалии для подсказок и отчёта импорта."));
                row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.PropertyField(row, prefab, new GUIContent("Prefab", tooltip));
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }
    }

    [CustomEditor(typeof(XRayLevelAnomalyCatalog))]
    public sealed class XRayLevelAnomalyCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var catalog = (XRayLevelAnomalyCatalog)target;
            if (!catalog.Validate(out string error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            var missing = new HashSet<string>();
            foreach (XRayLevelAnomalyEntry entry in catalog.Entries)
            {
                if (entry != null && entry.Prefab == null)
                {
                    missing.Add(entry.DisplayName);
                }
            }

            if (missing.Count > 0)
            {
                EditorGUILayout.HelpBox("Нет префабов аномалий: " + string.Join(", ", missing) + ". Импортёр покажет пропущенные экземпляры для выбранной локации.", MessageType.Warning);
            }
        }
    }
}
