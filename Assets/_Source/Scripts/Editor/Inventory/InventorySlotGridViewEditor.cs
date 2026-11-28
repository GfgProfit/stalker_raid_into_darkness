using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.Editor
{
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
}
