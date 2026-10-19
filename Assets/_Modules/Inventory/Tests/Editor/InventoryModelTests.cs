using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Inventory.Tests
{
    public sealed class InventoryModelTests
    {
        private InventoryTestItems _items;
        private InventoryModel _model;
        private ItemDefinition _rifle;
        private ItemDefinition _ammo;
        private ItemDefinition _medkit;
        private ItemDefinition _square;
        private List<InventoryChange> _changes;

        [SetUp]
        public void SetUp()
        {
            _items = new InventoryTestItems();
            _rifle = _items.Create("rifle", new Vector2Int(4, 2), 1, 3.5f, "Weapon");
            _ammo = _items.Create("ammo", Vector2Int.one, 60, 0.01f, "Ammo");
            _medkit = _items.Create("medkit", Vector2Int.one, 5, 0.2f, "Medical");
            _square = _items.Create("square", new Vector2Int(2, 2), 1, 1f);
            _model = new InventoryModel(_items.Database);
            _model.AddContainer(new GridContainer("Backpack", 10, 8, "Player"));
            _model.AddContainer(new GridContainer("Stash", 6, 6, "World"));
            _changes = new List<InventoryChange>();
            _model.Changed += _changes.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _items.Dispose();
        }

        [Test]
        public void AddPlacesItemAndOccupiesCells()
        {
            ItemInstance rifle = _model.CreateItem(_rifle);
            InventoryResult result = _model.TryAdd(rifle, "Backpack", new Vector2Int(1, 1), false);

            Assert.That(result.Success, Is.True);
            InventoryContainer backpack = _model.GetContainer("Backpack");
            Assert.That(backpack.GetItemAt(new Vector2Int(1, 1)), Is.SameAs(rifle));
            Assert.That(backpack.GetItemAt(new Vector2Int(4, 2)), Is.SameAs(rifle));
            Assert.That(backpack.GetItemAt(new Vector2Int(5, 1)), Is.Null);
            Assert.That(rifle.Location.ContainerId, Is.EqualTo("Backpack"));
            Assert.That(_changes.Exists(change => change.Kind == InventoryChangeKind.ItemAdded && change.Item == rifle), Is.True);
        }

        [Test]
        public void AddRejectsOverlapOutOfBoundsAndDuplicates()
        {
            ItemInstance first = _model.CreateItem(_rifle);
            Assert.That(_model.TryAdd(first, "Backpack", Vector2Int.zero, false).Success, Is.True);

            Assert.That(_model.TryAdd(_model.CreateItem(_rifle), "Backpack", new Vector2Int(3, 1), false).Failure, Is.EqualTo(InventoryFailure.Occupied));
            Assert.That(_model.TryAdd(_model.CreateItem(_rifle), "Backpack", new Vector2Int(7, 0), false).Failure, Is.EqualTo(InventoryFailure.OutOfBounds));
            Assert.That(_model.TryAdd(first, "Stash", Vector2Int.zero, false).Failure, Is.EqualTo(InventoryFailure.AlreadyInInventory));
            Assert.That(_model.TryAdd(_model.CreateItem(_rifle), "Missing", Vector2Int.zero, false).Failure, Is.EqualTo(InventoryFailure.ContainerNotFound));
            Assert.That(_model.ItemCount, Is.EqualTo(1));
        }

        [Test]
        public void AutoPlaceFindsSpaceAndRotatesWhenNeeded()
        {
            var narrow = new GridContainer("Narrow", 2, 4);
            _model.AddContainer(narrow);
            ItemInstance rifle = _model.CreateItem(_rifle);

            InventoryResult result = _model.TryAddAuto(rifle, "Narrow");

            Assert.That(result.Success, Is.True);
            Assert.That(rifle.Location.Rotated, Is.True);
            Assert.That(narrow.GetItemArea(rifle), Is.EqualTo(new RectInt(0, 0, 2, 4)));
        }

        [Test]
        public void ArrangeStartsEachGroupOnNewRow()
        {
            ItemInstance ammo = _model.CreateItem(_ammo, 10);
            ItemInstance rifle = _model.CreateItem(_rifle);
            ItemInstance medkit = _model.CreateItem(_medkit, 2);
            _model.TryAdd(ammo, "Backpack", new Vector2Int(9, 7), false);
            _model.TryAdd(rifle, "Backpack", new Vector2Int(3, 3), false);
            _model.TryAdd(medkit, "Backpack", new Vector2Int(0, 6), false);
            _changes.Clear();

            InventoryResult result = _model.TryArrange("Backpack", new[] { rifle, medkit, ammo }, new[] { 1, 6, 9 });

            Assert.That(result.Success, Is.True);
            Assert.That(rifle.Location.Position, Is.EqualTo(Vector2Int.zero));
            Assert.That(medkit.Location.Position, Is.EqualTo(new Vector2Int(0, 2)));
            Assert.That(ammo.Location.Position, Is.EqualTo(new Vector2Int(0, 3)));
            Assert.That(_changes.FindAll(change => change.Kind == InventoryChangeKind.ItemMoved).Count, Is.EqualTo(3));
            Assert.That(_model.TryArrange("Backpack", new[] { rifle, medkit, ammo }, new[] { 1, 6, 9 }).Amount, Is.EqualTo(0));
            Assert.That(_model.TryArrange("Backpack", new[] { rifle, medkit }, new[] { 1, 6 }).Success, Is.False);
        }

        [Test]
        public void AutoPlaceMergesIntoExistingStacksFirst()
        {
            ItemInstance stack = _model.CreateItem(_ammo, 50);
            _model.TryAdd(stack, "Backpack", Vector2Int.zero, false);
            ItemInstance incoming = _model.CreateItem(_ammo, 30);

            InventoryResult result = _model.TryAddAuto(incoming, "Backpack");

            Assert.That(result.Success, Is.True);
            Assert.That(stack.Amount, Is.EqualTo(60));
            Assert.That(incoming.Amount, Is.EqualTo(20));
            Assert.That(_model.ContainsItem(incoming), Is.True);
            Assert.That(_model.ItemCount, Is.EqualTo(2));
        }

        [Test]
        public void FailedAutoPlaceLeavesModelUntouched()
        {
            var tiny = new GridContainer("Tiny", 1, 1);
            _model.AddContainer(tiny);
            ItemInstance stack = _model.CreateItem(_ammo, 55);
            _model.TryAdd(stack, "Tiny", Vector2Int.zero, false);
            _changes.Clear();
            ItemInstance incoming = _model.CreateItem(_ammo, 20);

            InventoryResult result = _model.TryAddAuto(incoming, "Tiny");

            Assert.That(result.Failure, Is.EqualTo(InventoryFailure.NoSpace));
            Assert.That(stack.Amount, Is.EqualTo(55));
            Assert.That(incoming.Amount, Is.EqualTo(20));
            Assert.That(_model.ContainsItem(incoming), Is.False);
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void MoveBetweenContainersAndRotate()
        {
            ItemInstance rifle = _model.CreateItem(_rifle);
            _model.TryAdd(rifle, "Backpack", Vector2Int.zero, false);

            Assert.That(_model.TryMove(rifle.InstanceId, "Stash", new Vector2Int(1, 1), false).Success, Is.True);
            Assert.That(_model.GetContainer("Backpack").GetItemAt(Vector2Int.zero), Is.Null);
            Assert.That(_model.GetContainer("Stash").GetItemAt(new Vector2Int(4, 2)), Is.SameAs(rifle));
            Assert.That(_changes.Exists(change => change.Kind == InventoryChangeKind.ItemMoved && change.PreviousContainerId == "Backpack" && change.ContainerId == "Stash"), Is.True);

            Assert.That(_model.TryRotate(rifle.InstanceId).Success, Is.True);
            Assert.That(rifle.Location.Rotated, Is.True);
            Assert.That(_model.GetContainer("Stash").GetItemArea(rifle), Is.EqualTo(new RectInt(1, 1, 2, 4)));
        }

        [Test]
        public void RotateFailsWithoutSpaceAndSquareItemsDoNotRotate()
        {
            ItemInstance rifle = _model.CreateItem(_rifle);
            _model.TryAdd(rifle, "Backpack", new Vector2Int(0, 6), false);
            ItemInstance square = _model.CreateItem(_square);
            _model.TryAdd(square, "Backpack", new Vector2Int(5, 5), false);

            Assert.That(_model.TryRotate(rifle.InstanceId).Success, Is.False);
            Assert.That(rifle.Location.Rotated, Is.False);
            Assert.That(_model.TryRotate(square.InstanceId).Failure, Is.EqualTo(InventoryFailure.RotationNotAllowed));
        }

        [Test]
        public void MoveOntoStackMergesAndRespectsMaxStack()
        {
            ItemInstance target = _model.CreateItem(_medkit, 4);
            ItemInstance source = _model.CreateItem(_medkit, 3);
            _model.TryAdd(target, "Backpack", Vector2Int.zero, false);
            _model.TryAdd(source, "Backpack", new Vector2Int(2, 0), false);

            InventoryResult result = _model.TryMove(source.InstanceId, "Backpack", Vector2Int.zero, false);

            Assert.That(result.Success, Is.True);
            Assert.That(target.Amount, Is.EqualTo(5));
            Assert.That(source.Amount, Is.EqualTo(2));
            Assert.That(_model.ContainsItem(source), Is.True);
            Assert.That(_model.TryMerge(source.InstanceId, target.InstanceId).Failure, Is.EqualTo(InventoryFailure.StackFull));
        }

        [Test]
        public void MergeRemovesEmptiedSource()
        {
            ItemInstance target = _model.CreateItem(_ammo, 10);
            ItemInstance source = _model.CreateItem(_ammo, 15);
            _model.TryAdd(target, "Backpack", Vector2Int.zero, false);
            _model.TryAdd(source, "Stash", Vector2Int.zero, false);

            Assert.That(_model.TryMerge(source.InstanceId, target.InstanceId).Success, Is.True);
            Assert.That(target.Amount, Is.EqualTo(25));
            Assert.That(_model.ContainsItem(source), Is.False);
            Assert.That(_model.GetContainer("Stash").GetItemAt(Vector2Int.zero), Is.Null);
        }

        [Test]
        public void ItemsWithDifferentStateDoNotStack()
        {
            ItemInstance target = _model.CreateItem(_ammo, 10);
            ItemInstance source = _model.CreateItem(_ammo, 10);
            source.SetState("condition", "0.5");
            _model.TryAdd(target, "Backpack", Vector2Int.zero, false);
            _model.TryAdd(source, "Backpack", Vector2Int.right, false);

            Assert.That(_model.TryMerge(source.InstanceId, target.InstanceId).Failure, Is.EqualTo(InventoryFailure.NotStackable));
        }

        [Test]
        public void SplitCreatesNewStackWithCopiedState()
        {
            ItemInstance stack = _model.CreateItem(_ammo, 40);
            stack.SetState("lot", "A");
            _model.TryAdd(stack, "Backpack", Vector2Int.zero, false);

            InventoryResult result = _model.TrySplit(stack.InstanceId, 15, "Stash", new Vector2Int(2, 2), false);

            Assert.That(result.Success, Is.True);
            Assert.That(stack.Amount, Is.EqualTo(25));
            Assert.That(result.Item, Is.Not.SameAs(stack));
            Assert.That(result.Item.Amount, Is.EqualTo(15));
            Assert.That(result.Item.GetState("lot"), Is.EqualTo("A"));
            Assert.That(result.Item.ContainerId, Is.EqualTo("Stash"));
            Assert.That(_model.TrySplit(stack.InstanceId, 25, "Stash", Vector2Int.zero, false).Failure, Is.EqualTo(InventoryFailure.InvalidAmount));
            Assert.That(_model.TrySplitAuto(stack.InstanceId, 5).Success, Is.True);
            Assert.That(stack.Amount, Is.EqualTo(20));
        }

        [Test]
        public void PartialTransferMovesOnlyRequestedAmount()
        {
            ItemInstance stack = _model.CreateItem(_ammo, 40);
            _model.TryAdd(stack, "Backpack", Vector2Int.zero, false);

            InventoryResult result = _model.TryTransfer(stack.InstanceId, "Stash", 10);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Amount, Is.EqualTo(10));
            Assert.That(stack.Amount, Is.EqualTo(30));
            Assert.That(result.Item.ContainerId, Is.EqualTo("Stash"));
            Assert.That(result.Item.Amount, Is.EqualTo(10));
        }

        [Test]
        public void PartialRemoveReturnsDetachedPiece()
        {
            ItemInstance stack = _model.CreateItem(_medkit, 4);
            _model.TryAdd(stack, "Backpack", Vector2Int.zero, false);

            InventoryResult partial = _model.TryRemove(stack.InstanceId, 1);
            Assert.That(partial.Success, Is.True);
            Assert.That(stack.Amount, Is.EqualTo(3));
            Assert.That(partial.Item.Location.IsPlaced, Is.False);
            Assert.That(_model.ContainsItem(partial.Item), Is.False);

            Assert.That(_model.TryRemove(stack.InstanceId).Success, Is.True);
            Assert.That(_model.ContainsItem(stack), Is.False);
            Assert.That(_model.GetContainer("Backpack").GetItemAt(Vector2Int.zero), Is.Null);
        }

        [Test]
        public void WeightLimitRejectsAddAndTransfer()
        {
            InventoryContainer stash = _model.GetContainer("Stash");
            stash.MaxWeight = 5f;
            ItemInstance rifle = _model.CreateItem(_rifle);
            ItemInstance second = _model.CreateItem(_rifle);
            _model.TryAdd(rifle, "Stash", Vector2Int.zero, false);
            _model.TryAdd(second, "Backpack", Vector2Int.zero, false);

            Assert.That(_model.TryTransfer(second.InstanceId, "Stash").Failure, Is.EqualTo(InventoryFailure.NoSpace));
            Assert.That(_model.TryAdd(_model.CreateItem(_rifle), "Stash", new Vector2Int(0, 3), false).Failure, Is.EqualTo(InventoryFailure.NoSpace));
            Assert.That(second.ContainerId, Is.EqualTo("Backpack"));
            Assert.That(_model.GetOwnerWeight("Player"), Is.EqualTo(3.5f).Within(0.001f));
        }

        [Test]
        public void PlacementValidatorCanRejectPlacement()
        {
            var validator = new RejectingValidator("Stash");
            _model.AddPlacementValidator(validator);
            ItemInstance rifle = _model.CreateItem(_rifle);

            Assert.That(_model.TryAdd(rifle, "Stash", Vector2Int.zero, false).Failure, Is.EqualTo(InventoryFailure.ValidatorRejected));
            Assert.That(_model.TryAdd(rifle, "Backpack", Vector2Int.zero, false).Success, Is.True);
            _model.RemovePlacementValidator(validator);
            Assert.That(_model.TryMove(rifle.InstanceId, "Stash", Vector2Int.zero, false).Success, Is.True);
        }

        [Test]
        public void EventsAreRaisedAfterOperationCompletes()
        {
            ItemInstance stack = _model.CreateItem(_ammo, 40);
            _model.TryAdd(stack, "Backpack", Vector2Int.zero, false);
            int observedItems = -1;
            _model.Changed += change =>
            {
                if (change.Kind == InventoryChangeKind.ItemAdded && observedItems < 0)
                {
                    observedItems = _model.ItemCount;
                }
            };

            _model.TrySplit(stack.InstanceId, 10, "Backpack", new Vector2Int(5, 5), false);

            Assert.That(observedItems, Is.EqualTo(2));
            Assert.That(stack.Amount, Is.EqualTo(30));
        }

        [Test]
        public void RemoveContainerDropsItsItems()
        {
            ItemInstance rifle = _model.CreateItem(_rifle);
            _model.TryAdd(rifle, "Stash", Vector2Int.zero, false);

            Assert.That(_model.RemoveContainer("Stash"), Is.True);
            Assert.That(_model.ContainsItem(rifle), Is.False);
            Assert.That(_model.GetContainer("Stash"), Is.Null);
        }

        private sealed class RejectingValidator : IInventoryPlacementValidator
        {
            private readonly string _containerId;

            public RejectingValidator(string containerId)
            {
                _containerId = containerId;
            }

            public bool CanPlace(ItemInstance item, InventoryContainer container, ItemPlacement placement) => container.Id != _containerId;
        }
    }
}
