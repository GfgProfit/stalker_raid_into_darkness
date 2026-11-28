using RaidIntoDarkness.Inventory.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Inventory.Editor
{
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
