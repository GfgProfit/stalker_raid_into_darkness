using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.Application;
using System.Collections;
using NUnit.Framework;
using RaidIntoDarkness.Inventory.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class InventoryAutoTransferPreviewTests : OutdoorsSceneInputTestFixture
    {
        [UnityTest]
        public IEnumerator StationaryAutoTransferRechecksWeightAndSpaceBeforeDrop()
        {
            SceneManager.LoadScene("TEST_SCENE", LoadSceneMode.Single);
            yield return null;

            InventoryWindowController controller = null;

            for (int i = 0; i < 60; i++)
            {
                controller = Object.FindAnyObjectByType<InventoryWindowController>();

                if (controller != null && controller.PresentationSource != null && controller.Window != null && controller.Runtime().Model.GetContainer(controller.Window.PrimaryContainerId) != null)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(controller, Is.Not.Null);
            controller.Open(true);
            InventoryWindow window = controller.Window;
            InventoryModel model = controller.Runtime().Model;
            InventoryGridViewBase sourceGrid = window.FindGrid(window.PrimaryContainerId);
            InventoryGridViewBase targetGrid = window.FindGrid(window.SecondaryContainerId);
            InventoryContainer source = model.GetContainer(window.PrimaryContainerId);
            Assert.That(model.RemoveContainer(window.SecondaryContainerId), Is.True);
            var target = model.AddContainer(new GridContainer(window.SecondaryContainerId, 3, 1, "World"));
            var ammo = ScriptableObject.CreateInstance<ItemDefinition>();
            var blocker = ScriptableObject.CreateInstance<ItemDefinition>();

            try
            {
                ammo.Configure("preview-ammo", "Ammo", Vector2Int.one, 0.1f, 60, false);
                blocker.Configure("preview-blocker", "Misc", Vector2Int.one, 0f, 1, false);
                ItemInstance incoming = model.CreateItem(ammo, 5);
                ItemInstance destination = model.CreateItem(ammo, 59);
                ItemInstance fillingItem = model.CreateItem(blocker);
                Assert.That(model.TryAddAuto(incoming, source.Id, mergeStacks: false).Success, Is.True);
                Assert.That(model.TryAdd(model.CreateItem(blocker), target.Id, Vector2Int.zero, false).Success, Is.True);
                Assert.That(model.TryAdd(destination, target.Id, Vector2Int.right, false).Success, Is.True);
                window.Bind(model, window.IconProvider);
                yield return null;
                Canvas.ForceUpdateCanvases();

                Assert.That(window.IsSecondaryOpen, Is.True);
                Assert.That(EventSystem.current, Is.Not.Null);
                Assert.That(targetGrid.Container, Is.SameAs(target));
                Canvas canvas = targetGrid.GetComponentInParent<Canvas>();
                Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                Rect firstCell = targetGrid.GetAreaRect(new RectInt(0, 0, 1, 1));
                Vector3 point = targetGrid.RectTransform.TransformPoint(targetGrid.TopLeftToLocal(firstCell.center));
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, point);
                Assert.That(targetGrid.TryGetCellAtScreen(screen, camera, out Vector2Int cell), Is.True);
                Assert.That(cell, Is.EqualTo(Vector2Int.zero));

                Assert.That(window.BeginDrag(incoming, sourceGrid, incoming.Amount, screen, camera), Is.True);
                Assert.That(window.Drag.TargetGrid, Is.SameAs(targetGrid));
                Assert.That(window.Drag.AutoPlace, Is.True);
                Assert.That(window.Drag.Kind, Is.EqualTo(InventoryDropKind.Merge));

                target.MaxWeight = target.TotalWeight;
                Assert.That(window.Drag.Kind, Is.EqualTo(InventoryDropKind.Invalid));
                Assert.That(window.Drag.Failure, Is.EqualTo(InventoryFailure.NoSpace));
                target.MaxWeight = -1f;
                Assert.That(window.Drag.Kind, Is.EqualTo(InventoryDropKind.Merge));

                Assert.That(model.TryAdd(fillingItem, target.Id, Vector2Int.right * 2, false).Success, Is.True);
                Assert.That(window.Drag.Kind, Is.EqualTo(InventoryDropKind.Invalid));
                Assert.That(window.Drag.Failure, Is.EqualTo(InventoryFailure.NoSpace));
                Assert.That(window.Drag.LastScreenPosition, Is.EqualTo(screen));
                Assert.That(window.CompleteDrag().Failure, Is.EqualTo(InventoryFailure.NoSpace));
                Assert.That(incoming.ContainerId, Is.EqualTo(source.Id));
                Assert.That(destination.Amount, Is.EqualTo(59));

                Assert.That(model.TryRemove(fillingItem.InstanceId).Success, Is.True);
                Assert.That(window.BeginDrag(incoming, sourceGrid, incoming.Amount, screen, camera), Is.True);
                Assert.That(window.Drag.Kind, Is.EqualTo(InventoryDropKind.Merge));
                Assert.That(window.CompleteDrag().Success, Is.True);
                Assert.That(destination.Amount, Is.EqualTo(60));
                Assert.That(target.GetItemAt(Vector2Int.right * 2), Is.SameAs(incoming));
                Assert.That(incoming.Amount, Is.EqualTo(4));
            }
            finally
            {
                controller.Close();
                Object.Destroy(ammo);
                Object.Destroy(blocker);
            }
        }
    }
}
