using Inventory.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory.Tests
{
    public sealed class InventoryGridViewTests
    {
        private GameObject _canvasObject;
        private GameObject _cameraObject;
        private RectTransform _gridRect;
        private InventoryTestItems _items;

        [SetUp]
        public void SetUp()
        {
            _items = new InventoryTestItems();
            _cameraObject = new GameObject("GridTestCamera", typeof(Camera));
            _cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            _canvasObject = new GameObject("GridTestCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _cameraObject.GetComponent<Camera>();
            _canvasObject.transform.localScale = Vector3.one * 0.005f;
            var imageObject = new GameObject("Grid", typeof(RectTransform), typeof(Image));
            _gridRect = (RectTransform)imageObject.transform;
            _gridRect.SetParent(_canvasObject.transform, false);
            _gridRect.anchorMin = new Vector2(0.5f, 0.5f);
            _gridRect.anchorMax = new Vector2(0.5f, 0.5f);
            _gridRect.sizeDelta = new Vector2(100f, 100f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_canvasObject);
            Object.DestroyImmediate(_cameraObject);
            _items.Dispose();
        }

        [Test]
        public void AutoSizeResizesRectToCells()
        {
            InventoryGridView grid = CreateGrid();
            grid.SetSizeMode(InventoryGridSizeMode.AutoSize);
            grid.SetGridSize(10, 8);

            Assert.That(grid.Columns, Is.EqualTo(10));
            Assert.That(grid.Rows, Is.EqualTo(8));
            Assert.That(_gridRect.rect.size, Is.EqualTo(new Vector2(640f, 512f)));
            Assert.That(grid.LayoutWarning, Is.Empty);
            Assert.That(grid.GridGraphic, Is.Not.Null);
        }

        [Test]
        public void SizeFromRectDerivesCellsAndWarnsOnRemainder()
        {
            InventoryGridView grid = CreateGrid();
            _gridRect.sizeDelta = new Vector2(384f, 256f);
            grid.SetSizeMode(InventoryGridSizeMode.SizeFromRect);

            Assert.That(grid.Columns, Is.EqualTo(6));
            Assert.That(grid.Rows, Is.EqualTo(4));
            Assert.That(grid.LayoutWarning, Is.Empty);

            _gridRect.sizeDelta = new Vector2(400f, 256f);
            grid.Rebuild();

            Assert.That(grid.Columns, Is.EqualTo(6));
            Assert.That(grid.LayoutWarning, Is.Not.Empty);
            Assert.That(_gridRect.sizeDelta, Is.EqualTo(new Vector2(400f, 256f)));
            Assert.That(((GridContainer)grid.CreateContainer()).Width, Is.EqualTo(6));
        }

        [Test]
        public void RebuildDoesNotDuplicateGeneratedChildrenAndKeepsUserChildren()
        {
            var userChild = new GameObject("UserDecoration", typeof(RectTransform));
            userChild.transform.SetParent(_gridRect, false);
            InventoryGridView grid = CreateGrid();
            var duplicate = new GameObject("__InventoryGrid", typeof(RectTransform));
            duplicate.transform.SetParent(_gridRect, false);
            duplicate.AddComponent<InventoryGeneratedElement>().Role = InventoryGeneratedRole.Grid;

            for (int i = 0; i < 3; i++)
            {
                grid.Rebuild();
            }

            Assert.That(_gridRect.GetComponentsInChildren<InventoryGeneratedElement>(true).Length, Is.EqualTo(4));
            Assert.That(userChild != null, Is.True);
            Assert.That(userChild.transform.parent, Is.SameAs(_gridRect));
            Assert.That(grid.GridGraphic.gameObject.hideFlags & HideFlags.DontSaveInEditor, Is.EqualTo(HideFlags.DontSaveInEditor));
        }

        [Test]
        public void ScreenCoordinatesMapToCells()
        {
            InventoryGridView grid = CreateGrid();
            grid.SetGridSize(10, 8);
            Camera camera = _cameraObject.GetComponent<Camera>();
            Rect area = grid.GetAreaRect(new RectInt(3, 2, 1, 1));
            Vector3 world = _gridRect.TransformPoint(grid.TopLeftToLocal(area.center));
            Vector2 screen = camera.WorldToScreenPoint(world);

            Assert.That(grid.TryGetCellAtScreen(screen, camera, out Vector2Int cell), Is.True);
            Assert.That(cell, Is.EqualTo(new Vector2Int(3, 2)));
            Assert.That(grid.GetPlacementOrigin(area.center, new Vector2Int(3, 1)), Is.EqualTo(new Vector2Int(2, 2)));
            Assert.That(grid.TryGetCellAtLocal(new Vector2(-1f, 10f), out _), Is.False);
        }

        [Test]
        public void SlotGridBuildsSlottedContainerFromInlineMask()
        {
            var view = _gridRect.gameObject.AddComponent<InventorySlotGridView>();
            view.Configure("Equipment", "Player", "Equipment", new Vector2(50f, 50f));
            view.SetInlineLayout("AA.B\nAA.C", System.Array.Empty<SlotLayoutBinding>());
            view.Rebuild();

            var container = (SlottedContainer)view.CreateContainer();

            Assert.That(container.Slots.Count, Is.EqualTo(3));
            Assert.That(view.Columns, Is.EqualTo(4));
            Assert.That(view.Rows, Is.EqualTo(2));
            Assert.That(view.LayoutWarning, Is.Empty);
            Assert.That(view.TryGetCellAtLocal(view.GetAreaRect(new RectInt(3, 1, 1, 1)).center, out Vector2Int cell), Is.True);
            Assert.That(cell, Is.EqualTo(new Vector2Int(3, 1)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TooltipStatsKeepLongLabelsSeparateFromValues(bool hasBar)
        {
            CreateWindow(out InventoryTooltip tooltip, out InventoryModel model);
            ItemInstance item = model.CreateItem(_items.Create("item", Vector2Int.one));
            var stats = new[]
            {
                new InventoryItemStat("Protection against a very long localized characteristic name", "100%", comparison: "(+25%)", fill: hasBar ? 0.75f : -1f),
                new InventoryItemStat("Weight", "+10 kg")
            };
            tooltip.Show(item, new Vector2(400f, 400f), _cameraObject.GetComponent<Camera>(), stats);
            Canvas.ForceUpdateCanvases();
            Transform root = tooltip.transform.Find("Layout/Stats");
            var firstRow = (RectTransform)root.GetChild(0);
            var secondRow = (RectTransform)root.GetChild(1);
            TextMeshProUGUI label = firstRow.Find("Label").GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI value = firstRow.Find("Value").GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI comparison = firstRow.Find("Comparison").GetComponent<TextMeshProUGUI>();
            label.ForceMeshUpdate();
            value.ForceMeshUpdate();
            comparison.ForceMeshUpdate();

            Assert.That(label.textInfo.lineCount, Is.GreaterThan(1));
            Assert.That(label.rectTransform.offsetMax.x, Is.LessThan(value.rectTransform.offsetMin.x));
            Assert.That(value.rectTransform.offsetMax.x, Is.LessThan(comparison.rectTransform.offsetMin.x));
            Assert.That(comparison.rectTransform.offsetMax.x, Is.LessThanOrEqualTo(((RectTransform)root).rect.width + 0.1f));
            Assert.That(firstRow.rect.height, Is.GreaterThanOrEqualTo(label.preferredHeight - 0.1f));
            Assert.That(secondRow.anchoredPosition.y, Is.EqualTo(-firstRow.rect.height).Within(0.1f));
            Assert.That(label.isTextTruncated, Is.False);
            Assert.That(value.isTextTruncated, Is.False);
            Assert.That(comparison.isTextTruncated, Is.False);
        }

        [Test]
        public void ItemDisableCallbackClearsHoverAndHidesTooltip()
        {
            InventoryWindow window = CreateWindow(out InventoryTooltip tooltip, out InventoryModel model);
            ItemInstance item = model.CreateItem(_items.Create("item", Vector2Int.one));
            model.TryAdd(item, "Backpack", Vector2Int.zero, false);
            InventoryItemView view = window.Grids[0].ItemViews[item.InstanceId];
            window.HandleItemPointerEnter(view, new PointerEventData(EventSystem.current));
            tooltip.Show(item, new Vector2(400f, 400f), _cameraObject.GetComponent<Camera>());
            typeof(InventoryItemView).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, null);

            Assert.That(window.HoveredItem, Is.Null);
            Assert.That(tooltip.IsVisible, Is.False);
        }

        [Test]
        public void EnteringAnotherItemHidesPreviousTooltipAndIgnoresOldExit()
        {
            InventoryWindow window = CreateWindow(out InventoryTooltip tooltip, out InventoryModel model);
            ItemDefinition definition = _items.Create("item", Vector2Int.one);
            ItemInstance first = model.CreateItem(definition);
            ItemInstance second = model.CreateItem(definition);
            model.TryAdd(first, "Backpack", Vector2Int.zero, false);
            model.TryAdd(second, "Backpack", Vector2Int.right, false);
            InventoryItemView firstView = window.Grids[0].ItemViews[first.InstanceId];
            InventoryItemView secondView = window.Grids[0].ItemViews[second.InstanceId];
            var pointer = new PointerEventData(EventSystem.current);
            window.HandleItemPointerEnter(firstView, pointer);
            tooltip.Show(first, new Vector2(400f, 400f), _cameraObject.GetComponent<Camera>());
            window.HandleItemPointerEnter(secondView, pointer);
            window.HandleItemPointerExit(firstView, pointer);

            Assert.That(window.HoveredItem, Is.SameAs(second));
            Assert.That(tooltip.IsVisible, Is.False);
        }

        private InventoryWindow CreateWindow(out InventoryTooltip tooltip, out InventoryModel model)
        {
            InventoryGridView grid = CreateGrid();
            var tooltipObject = new GameObject("Tooltip", typeof(RectTransform));
            tooltipObject.transform.SetParent(_canvasObject.transform, false);
            tooltip = tooltipObject.AddComponent<InventoryTooltip>();
            tooltip.Hide();
            InventoryWindow window = _canvasObject.AddComponent<InventoryWindow>();
            window.Configure((RectTransform)_canvasObject.transform, null, tooltip, null, null, null, null, null, new[] { grid });
            model = new InventoryModel(_items.Database);
            window.Bind(model, null);
            return window;
        }

        private InventoryGridView CreateGrid()
        {
            var grid = _gridRect.gameObject.AddComponent<InventoryGridView>();
            grid.Configure("Backpack", "Player", "Backpack", new Vector2(64f, 64f));
            grid.Rebuild();
            return grid;
        }
    }
}
