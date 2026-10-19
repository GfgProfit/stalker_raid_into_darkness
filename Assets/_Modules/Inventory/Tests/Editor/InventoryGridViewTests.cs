using Inventory.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory.Tests
{
    public sealed class InventoryGridViewTests
    {
        private GameObject _canvasObject;
        private GameObject _cameraObject;
        private RectTransform _gridRect;

        [SetUp]
        public void SetUp()
        {
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

        private InventoryGridView CreateGrid()
        {
            var grid = _gridRect.gameObject.AddComponent<InventoryGridView>();
            grid.Configure("Backpack", "Player", "Backpack", new Vector2(64f, 64f));
            grid.Rebuild();
            return grid;
        }
    }
}
