using Inventory.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Inventory.Tests
{
    public sealed class InventoryItemConditionViewTests
    {
        private Scene _preview;
        private GameObject _root;
        private InventoryTestItems _items;
        private InventoryWindow _window;
        private InventoryGridView _grid;
        private InventoryModel _model;
        private InventoryTheme _theme;

        [SetUp]
        public void SetUp()
        {
            _preview = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("ConditionViewTestRoot", typeof(RectTransform));
            _root.SetActive(false);
            SceneManager.MoveGameObjectToScene(_root, _preview);
            _root.AddComponent<Canvas>();
            _items = new InventoryTestItems();
            _theme = _items.Track(ScriptableObject.CreateInstance<InventoryTheme>());
            using (var serialized = new SerializedObject(_theme))
            {
                serialized.FindProperty("_itemInfoBackground").colorValue = new Color(0f, 0f, 0f, 0.7f);
                serialized.FindProperty("_itemTextShadow").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var gridObject = new GameObject("Grid", typeof(RectTransform), typeof(Image));
            gridObject.transform.SetParent(_root.transform, false);
            _grid = gridObject.AddComponent<InventoryGridView>();
            _grid.Configure("Backpack", "Player", "Backpack", new Vector2(64f, 64f));
            _grid.SetGridSize(2, 1);
            _grid.Rebuild();
            _window = _root.AddComponent<InventoryWindow>();
            _window.Configure((RectTransform)_root.transform, null, null, null, null, null, null, null, new[] { _grid });
            _window.ConfigurePresentation(_theme, null, string.Empty, string.Empty, Color.white);
            _model = new InventoryModel(_items.Database);
            _window.Bind(_model, null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            _items?.Dispose();
            if (_preview.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(_preview);
            }
        }

        [TestCase("1", "100%")]
        [TestCase("0.999", "99.9%")]
        [TestCase("0.91", "91%")]
        [TestCase("0.12345", "12.3%")]
        [TestCase("0", "0%")]
        public void ConditionOverlayShowsFullAndFractionalValuesWithInfoBackground(string condition, string expected)
        {
            ItemInstance item = _model.CreateItem(_items.Create("condition-item", Vector2Int.one));
            item.SetState("condition", condition);
            Assert.That(_model.TryAdd(item, "Backpack", Vector2Int.zero, false).Success, Is.True);
            InventoryItemView view = _grid.ItemViews[item.InstanceId];
            TMP_Text label = view.transform.Find("Overlay/Condition").GetComponent<TMP_Text>();
            Image background = view.transform.Find("Overlay/ConditionBackground").GetComponent<Image>();
            _root.SetActive(true);
            label.ForceMeshUpdate(true);
            view.Layout(_grid.GetAreaRect(new RectInt(0, 0, 1, 1)));

            Assert.That(_root.activeSelf, Is.True);
            Assert.That(label.gameObject.activeSelf, Is.True);
            Assert.That(label.enabled, Is.True);
            Assert.That(label.text, Is.EqualTo(expected));
            Assert.That(background.enabled, Is.True);
            Assert.That(background.color, Is.EqualTo(_theme.ItemInfoBackground));
            Assert.That(background.raycastTarget, Is.False);
            Assert.That(background.transform.GetSiblingIndex(), Is.LessThan(label.transform.GetSiblingIndex()));
            Assert.That(background.rectTransform.rect.width, Is.GreaterThan(0f));
            Assert.That(background.rectTransform.rect.height, Is.GreaterThan(0f));
            Assert.That(view.transform.Find("Overlay/ConditionShadow").gameObject.activeSelf, Is.False);
            Assert.That(item.GetState("condition"), Is.EqualTo(condition));
            view.BindGhost(item, _grid, _grid.CellSize, false, 1);
            Assert.That(label.enabled, Is.False);
            Assert.That(background.enabled, Is.False);
        }

        [Test]
        public void ConditionProviderShowsFullDurabilityWithoutCreatingStateOnUnrelatedItems()
        {
            _window.ItemConditionProvider = item => item.Definition.IsCategory("Equipment") ? 1f : null;
            ItemInstance equipment = _model.CreateItem(_items.Create("full-equipment", Vector2Int.one, category: "Equipment"));
            ItemInstance food = _model.CreateItem(_items.Create("food", Vector2Int.one));
            Assert.That(_model.TryAdd(equipment, "Backpack", Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(food, "Backpack", Vector2Int.right, false).Success, Is.True);
            TMP_Text equipmentLabel = _grid.ItemViews[equipment.InstanceId].transform.Find("Overlay/Condition").GetComponent<TMP_Text>();
            TMP_Text foodLabel = _grid.ItemViews[food.InstanceId].transform.Find("Overlay/Condition").GetComponent<TMP_Text>();

            Assert.That(_root.activeSelf, Is.False);
            Assert.That(equipmentLabel.gameObject.activeSelf, Is.True);
            Assert.That(equipmentLabel.enabled, Is.True);
            Assert.That(equipmentLabel.text, Is.EqualTo("100%"));
            Assert.That(foodLabel.enabled, Is.False);
            Assert.That(equipment.State.ContainsKey("condition"), Is.False);
            Assert.That(food.State.ContainsKey("condition"), Is.False);
        }
    }
}
