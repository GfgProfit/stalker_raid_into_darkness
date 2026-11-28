using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class InventoryInteractionLifetimeTests : OutdoorsSceneInputTestFixture
    {
        private const string BACKPACK = "Backpack";
        private const string STASH = "Stash";
        private const string ROTATABLE_ITEM = "wpn_sil_nato";
        private const string AMMO = "ammo_5.56x45_ss190";
        private const BindingFlags FIELDS = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator DragCleansUpOnCancelSourceRebindRemovalAndClose()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindAnyObjectByType<InventoryWindowController>();
            controller.Open(true);
            InventoryWindow window = controller.Window;
            InventoryModel model = window.Model;
            ItemDefinition definition = FindDefinition(controller.Runtime().Database, ROTATABLE_ITEM);
            Assert.That(definition.AllowRotation, Is.True);
            ItemInstance first = model.CreateItem(definition);
            ItemInstance second = model.CreateItem(definition);
            Assert.That(model.TryAddAuto(first, BACKPACK, mergeStacks: false).Success, Is.True);
            Assert.That(model.TryAddAuto(second, BACKPACK, mergeStacks: false).Success, Is.True);
            yield return null;

            InventoryGridViewBase bag = window.FindGrid(BACKPACK);
            InventoryGridViewBase stash = window.FindGrid(STASH);
            InventoryItemView firstView = bag.GetItemView(first);
            InventoryItemView secondView = bag.GetItemView(second);
            CanvasGroup firstCanvas = firstView.GetComponent<CanvasGroup>();
            CanvasGroup secondCanvas = secondView.GetComponent<CanvasGroup>();
            bool sourceRaycasts = firstCanvas.blocksRaycasts;
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = 4, position = ScreenPoint(firstView.RectTransform) };

            window.HandleItemPointerEnter(firstView, pointer);
            Assert.That(window.HoveredItem, Is.SameAs(first));
            firstView.OnBeginDrag(pointer);
            Assert.That(window.Drag.Item, Is.SameAs(first));
            Assert.That(window.HoveredItem, Is.Null);
            Assert.That(window.Tooltip.IsVisible, Is.False);
            Assert.That(firstCanvas.alpha, Is.LessThan(1f));
            Assert.That(firstCanvas.blocksRaycasts, Is.EqualTo(sourceRaycasts));

            InventoryItemDragGhost ghost = window.Drag.Ghost;
            bool originalRotation = window.Drag.Rotated;
            window.RotateDrag();
            Assert.That(window.Drag.Rotated, Is.EqualTo(!originalRotation));
            pointer.position = ScreenPoint(stash.RectTransform);
            firstView.OnDrag(pointer);
            Assert.That(window.Drag.TargetGrid, Is.SameAs(stash));
            Assert.That(Placement(stash), Is.Not.Null);

            window.CancelDrag();
            AssertReleased(window, ghost);
            Assert.That(firstCanvas.alpha, Is.EqualTo(1f));
            Assert.That(firstCanvas.blocksRaycasts, Is.EqualTo(sourceRaycasts));
            Assert.That(window.HoveredItem, Is.Null);

            Assert.That(window.BeginDrag(second, bag, second.Amount, ScreenPoint(secondView.RectTransform), null), Is.True);
            ghost = window.Drag.Ghost;
            firstView.OnEndDrag(pointer);
            Assert.That(window.Drag.Item, Is.SameAs(second));

            secondView.Bind(first, bag);
            AssertReleased(window, ghost);
            Assert.That(secondCanvas.alpha, Is.EqualTo(1f));
            secondView.OnBeginDrag(pointer);
            Assert.That(window.Drag, Is.Null);
            secondView.Bind(second, bag);

            Assert.That(window.BeginDrag(first, bag, first.Amount, ScreenPoint(firstView.RectTransform), null), Is.True);
            ghost = window.Drag.Ghost;
            Assert.That(model.TryRemove(first.InstanceId).Success, Is.True);
            AssertReleased(window, ghost);

            Assert.That(window.BeginDrag(second, bag, second.Amount, ScreenPoint(secondView.RectTransform), null), Is.True);
            ghost = window.Drag.Ghost;
            controller.Close();
            AssertReleased(window, ghost);
            Assert.That(secondCanvas.alpha, Is.EqualTo(1f));
            Assert.That(model.ContainsItem(second), Is.True);
            yield return null;
            Assert.That(ghost == null, Is.True);

            controller.Open();
            yield return null;
            Assert.That(window.HoveredItem, Is.Null);
            Assert.That(window.Tooltip.IsVisible, Is.False);
            Assert.That(window.BeginDrag(second, bag, second.Amount, ScreenPoint(bag.GetItemView(second).RectTransform), null), Is.True);
            ghost = window.Drag.Ghost;
            controller.Configure(window, null, controller.Player, null, null);
            AssertReleased(window, ghost);
            Assert.That(window.Model, Is.Null);
            Assert.That(model.ContainsItem(second), Is.True);
        }

        [UnityTest]
        public IEnumerator TooltipDelayContentAndExternalModeRespectModalDragAndReopen()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.runInBackground = UnityEngine.Application.runInBackground;
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;

            var controller = Object.FindAnyObjectByType<InventoryWindowController>();
            controller.Open();
            InventoryWindow window = controller.Window;
            InventoryModel model = window.Model;
            ItemDefinition definition = FindDefinition(controller.Runtime().Database, AMMO);
            ItemInstance item = model.CreateItem(definition, 4);
            Assert.That(model.TryAddAuto(item, BACKPACK, mergeStacks: false).Success, Is.True);
            window.ItemDescriptionProvider = current => $"Amount {current.Amount}";
            yield return null;

            InventoryGridViewBase bag = window.FindGrid(BACKPACK);
            InventoryItemView view = bag.GetItemView(item);
            Vector2 position = ScreenPoint(view.RectTransform);
            Move(mouse.position, position, queueEventOnly: true);
            yield return null;
            var pointer = new PointerEventData(EventSystem.current) { position = position };
            window.HandleItemPointerEnter(view, pointer);
            Assert.That(window.HoveredItem, Is.SameAs(item));
            Assert.That(window.Tooltip.IsVisible, Is.False);
            yield return new WaitForSecondsRealtime(0.45f);
            Assert.That(window.Tooltip.IsVisible, Is.True);
            Assert.That(window.Tooltip.Title, Is.EqualTo(definition.DisplayName));
            Assert.That(window.Tooltip.Description, Is.EqualTo("Amount 4"));

            Assert.That(model.TryRemove(item.InstanceId, 1).Success, Is.True);
            Assert.That(window.Tooltip.Description, Is.EqualTo("Amount 3"));
            window.ItemDescriptionProvider = _ => "Refreshed";
            window.RefreshTooltip();
            Assert.That(window.Tooltip.Description, Is.EqualTo("Refreshed"));

            window.OpenSplitDialog(item);
            Assert.That(window.SplitDialog.IsVisible, Is.True);
            Assert.That(window.HoveredItem, Is.Null);
            Assert.That(window.Tooltip.IsVisible, Is.False);
            window.HandleItemPointerEnter(view, pointer);
            Assert.That(window.HoveredItem, Is.Null);
            Assert.That(window.ShowTooltip("Blocked", string.Empty, position, null), Is.False);
            Assert.That(window.CloseTopWidget(), Is.True);

            Assert.That(window.ShowTooltip("Status", "External", position, null), Is.True);
            Assert.That(window.HoveredItem, Is.Null);
            Assert.That(window.Tooltip.Title, Is.EqualTo("Status"));
            window.MoveExternalTooltip(position + Vector2.one, null);
            Assert.That(window.Tooltip.IsVisible, Is.True);

            Assert.That(window.BeginDrag(item, bag, 1, position, null), Is.True);
            Assert.That(window.Tooltip.IsVisible, Is.False);
            Assert.That(window.ShowTooltip("Blocked", string.Empty, position, null), Is.False);
            window.HandleItemPointerEnter(view, pointer);
            Assert.That(window.HoveredItem, Is.Null);
            window.CancelDrag();
            Assert.That(window.HoveredItem, Is.Null);

            Assert.That(window.ShowTooltip("Status", "External", position, null), Is.True);
            window.HideTooltip();
            Assert.That(window.Tooltip.IsVisible, Is.False);
            controller.Close();
            window.HandleItemPointerEnter(view, pointer);
            Assert.That(window.HoveredItem, Is.Null);
            Move(mouse.position, Vector2.zero, queueEventOnly: true);
            yield return null;
            controller.Open();
            yield return null;
            Assert.That(window.HoveredItem, Is.Null);
            Assert.That(window.Tooltip.IsVisible, Is.False);
        }

        private static Vector2 ScreenPoint(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));

        private static object Placement(InventoryGridViewBase grid) => typeof(InventoryGridViewBase).GetField("_placementArea", FIELDS).GetValue(grid);

        private static void AssertReleased(InventoryWindow window, InventoryItemDragGhost ghost)
        {
            Assert.That(window.IsDragging, Is.False);
            Assert.That(window.Drag, Is.Null);
            Assert.That(ghost == null || ghost.gameObject.activeSelf == false, Is.True);
            var compatible = typeof(InventoryGridViewBase).GetField("_compatibleAreas", FIELDS);
            foreach (InventoryGridViewBase grid in window.Grids)
            {
                Assert.That(Placement(grid), Is.Null);
                Assert.That((List<RectInt>)compatible.GetValue(grid), Is.Empty);
            }
        }
    }
}
