using Inventory.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory.Editor
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

    [CustomEditor(typeof(InventorySlotGridView))]
    [CanEditMultipleObjects]
    public sealed class InventorySlotGridViewEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            bool hasLayout = serializedObject.FindProperty("_layout").objectReferenceValue != null;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (property.propertyPath == "m_Script")
                {
                    continue;
                }

                using (new EditorGUI.DisabledScope(hasLayout && (property.propertyPath == "_inlineMask" || property.propertyPath == "_inlineBindings")))
                {
                    EditorGUILayout.PropertyField(property, true);
                }
            }

            bool changed = serializedObject.ApplyModifiedProperties();

            foreach (Object target in targets)
            {
                var grid = (InventorySlotGridView)target;

                if (changed)
                {
                    Undo.RecordObject(grid.RectTransform, "Rebuild Slot Grid");
                    grid.Rebuild();
                }

                InventoryGridEditorUtility.DrawStatus(grid);
                EditorGUILayout.LabelField($"Slots: {grid.Slots.Count}", EditorStyles.miniLabel);

                foreach (SlotDefinition slot in grid.Slots)
                {
                    EditorGUILayout.LabelField($"  {slot.Id}  {slot.Mode}  {slot.Width}x{slot.Height} at {slot.X},{slot.Y}  {string.Join(",", slot.Rule.AcceptedCategories)}", EditorStyles.miniLabel);
                }
            }

            if (GUILayout.Button("Rebuild Slots"))
            {
                foreach (Object target in targets)
                {
                    ((InventorySlotGridView)target).Rebuild();
                }
            }
        }
    }

    internal static class InventoryGridEditorUtility
    {
        public static void DrawStatus(InventoryGridViewBase grid)
        {
            Vector2 size = grid.ContentSize;
            EditorGUILayout.HelpBox($"{grid.name}: {grid.Columns}x{grid.Rows} cells, {size.x:0.#}x{size.y:0.#} px", MessageType.None);

            if (string.IsNullOrEmpty(grid.LayoutWarning) == false)
            {
                EditorGUILayout.HelpBox(grid.LayoutWarning, MessageType.Warning);
            }

            if (grid.GetComponent<Image>() == null && grid.GetComponent<Graphic>() == null)
            {
                EditorGUILayout.HelpBox("Add an Image (Source Image = None) to receive pointer events and draw the grid background.", MessageType.Info);
            }
        }

        [MenuItem("GameObject/UI/Inventory/Inventory Grid", false, 2100)]
        private static void CreateGrid(MenuCommand command) => Create<InventoryGridView>(command, "Inventory Grid");

        [MenuItem("GameObject/UI/Inventory/Inventory Slot Grid", false, 2101)]
        private static void CreateSlotGrid(MenuCommand command) => Create<InventorySlotGridView>(command, "Inventory Slot Grid");

        private static void Create<T>(MenuCommand command, string name) where T : InventoryGridViewBase
        {
            var parent = command.context as GameObject;
            var gridObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(T));
            GameObjectUtility.SetParentAndAlign(gridObject, parent);
            Undo.RegisterCreatedObjectUndo(gridObject, "Create " + name);
            Selection.activeGameObject = gridObject;
        }
    }
}
