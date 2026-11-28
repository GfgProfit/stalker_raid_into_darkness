using RaidIntoDarkness.Inventory.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.Editor
{
    [CustomEditor(typeof(InventoryGridView))]
    [CanEditMultipleObjects]
    public sealed class InventoryGridViewEditor : UnityEditor.Editor
    {
        private SerializedProperty _containerId;
        private SerializedProperty _ownerId;
        private SerializedProperty _displayName;
        private SerializedProperty _sizeMode;
        private SerializedProperty _columns;
        private SerializedProperty _rows;
        private SerializedProperty _cellWidth;
        private SerializedProperty _cellHeight;
        private SerializedProperty _applyBackgroundToImage;
        private SerializedProperty _style;

        private void OnEnable()
        {
            _containerId = serializedObject.FindProperty("_containerId");
            _ownerId = serializedObject.FindProperty("_ownerId");
            _displayName = serializedObject.FindProperty("_displayName");
            _sizeMode = serializedObject.FindProperty("_sizeMode");
            _columns = serializedObject.FindProperty("_columns");
            _rows = serializedObject.FindProperty("_rows");
            _cellWidth = serializedObject.FindProperty("_cellWidth");
            _cellHeight = serializedObject.FindProperty("_cellHeight");
            _applyBackgroundToImage = serializedObject.FindProperty("_applyBackgroundToImage");
            _style = serializedObject.FindProperty("_style");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Container", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_containerId);
            EditorGUILayout.PropertyField(_ownerId);
            EditorGUILayout.PropertyField(_displayName);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_sizeMode);
            bool fromRect = _sizeMode.enumValueIndex == (int)InventoryGridSizeMode.SizeFromRect;

            using (new EditorGUI.DisabledScope(fromRect))
            {
                EditorGUILayout.PropertyField(_columns);
                EditorGUILayout.PropertyField(_rows);
            }

            EditorGUILayout.PropertyField(_cellWidth);
            EditorGUILayout.PropertyField(_cellHeight);
            EditorGUILayout.PropertyField(_applyBackgroundToImage);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_style, true);
            bool changed = serializedObject.ApplyModifiedProperties();

            foreach (Object target in targets)
            {
                var grid = (InventoryGridView)target;

                if (changed)
                {
                    Undo.RecordObject(grid.RectTransform, "Resize Inventory Grid");
                    grid.Rebuild();
                    EditorUtility.SetDirty(grid.RectTransform);
                }

                InventoryGridEditorUtility.DrawStatus(grid);
            }

            if (GUILayout.Button("Rebuild Grid"))
            {
                foreach (Object target in targets)
                {
                    var grid = (InventoryGridView)target;
                    Undo.RecordObject(grid.RectTransform, "Rebuild Inventory Grid");
                    grid.Rebuild();
                }
            }
        }
    }
}
