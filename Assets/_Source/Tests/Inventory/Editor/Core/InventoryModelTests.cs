using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Core.Tests
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

        [TestCase(59, 1, 0, true)]
        [TestCase(58, 3, 0, false)]
        [TestCase(58, 3, 1, true)]
        [TestCase(60, 1, 1, true)]
        public void ReconfigurationOnlyMergesWhenEveryReturnedItemFits(int existingAmount, int returnAmount, int extraCells, bool fits)
        {
            var destination = new GridContainer("Return", 1 + extraCells, 1);
            _model.AddContainer(destination);
            ItemInstance stack = _model.CreateItem(_ammo, existingAmount);
            ItemInstance rifle = _model.CreateItem(_rifle);
            Assert.That(_model.TryAdd(stack, destination.Id, Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(rifle, "Backpack", Vector2Int.zero, false).Success, Is.True);
            var updated = new ItemInstance(_rifle, rifle.Amount, rifle.InstanceId);
            updated.SetState("test", "changed");
            ItemInstance returned = _model.CreateItem(_ammo, returnAmount);
            _changes.Clear();

            InventoryResult preview = _model.TryReconfigureItem(rifle, updated, rifle.Location.Position, returned: returned, returnContainerId: destination.Id, apply: false, mergeReturned: true);
            Assert.That(preview.Success, Is.EqualTo(fits));
            Assert.That(stack.Amount, Is.EqualTo(existingAmount));
            Assert.That(returned.Amount, Is.EqualTo(returnAmount));
            Assert.That(rifle.GetState("test"), Is.Null);
            Assert.That(_changes, Is.Empty);

            InventoryResult result = _model.TryReconfigureItem(rifle, updated, rifle.Location.Position, returned: returned, returnContainerId: destination.Id, mergeReturned: true);
            Assert.That(result.Success, Is.EqualTo(fits));
            Assert.That(stack.Amount, Is.EqualTo(fits ? Mathf.Min(existingAmount + returnAmount, stack.MaxStack) : existingAmount));
            Assert.That(rifle.GetState("test"), Is.EqualTo(fits ? "changed" : null));
            Assert.That(_model.ContainsItem(returned), Is.EqualTo(fits && extraCells > 0));
            if (_model.ContainsItem(returned))
            {
                Assert.That(returned.Amount, Is.EqualTo(existingAmount + returnAmount - stack.MaxStack));
            }

            Assert.That(_changes.Exists(change => change.Kind == InventoryChangeKind.AmountChanged && change.Item == stack), Is.EqualTo(fits && existingAmount < stack.MaxStack));
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

        [TestCase(0, 2, false)]
        [TestCase(0, 3, true)]
        [TestCase(59, 0, true)]
        [TestCase(60, 0, false)]
        public void ReconfigurationReturnsAddonAndAmmoTogether(int existingAmount, int extraCells, bool fits)
        {
            int width = existingAmount == 0 ? extraCells : 3 + extraCells;
            var destination = new GridContainer("Return", width, 2);
            _model.AddContainer(destination);
            ItemInstance stack = existingAmount > 0 ? _model.CreateItem(_ammo, existingAmount) : null;
            ItemInstance blockedCell = existingAmount > 0 ? _model.CreateItem(_medkit) : null;
            if (stack != null)
            {
                Assert.That(_model.TryAdd(stack, destination.Id, new Vector2Int(2, 0), false).Success, Is.True);
                Assert.That(_model.TryAdd(blockedCell, destination.Id, new Vector2Int(2, 1), false).Success, Is.True);
            }

            ItemInstance rifle = _model.CreateItem(_rifle);
            Assert.That(_model.TryAdd(rifle, "Backpack", Vector2Int.zero, false).Success, Is.True);
            rifle.SetState("round", "loaded");
            var updated = new ItemInstance(_rifle, rifle.Amount, rifle.InstanceId);
            ItemInstance addon = _model.CreateItem(_square);
            ItemInstance round = _model.CreateItem(_ammo);
            _changes.Clear();

            InventoryResult preview = _model.TryReconfigureItem(rifle, updated, rifle.Location.Position, returned: addon, returnContainerId: destination.Id, apply: false, returnedStack: round);
            Assert.That(preview.Success, Is.EqualTo(fits));
            Assert.That(rifle.GetState("round"), Is.EqualTo("loaded"));
            Assert.That(addon.Location.IsPlaced, Is.False);
            Assert.That(round.Location.IsPlaced, Is.False);
            Assert.That(_changes, Is.Empty);

            InventoryResult result = _model.TryReconfigureItem(rifle, updated, rifle.Location.Position, returned: addon, returnContainerId: destination.Id, returnedStack: round);
            Assert.That(result.Success, Is.EqualTo(fits));
            Assert.That(rifle.GetState("round"), Is.EqualTo(fits ? null : "loaded"));
            Assert.That(_model.ContainsItem(addon), Is.EqualTo(fits));
            Assert.That(_model.ContainsItem(round), Is.EqualTo(fits && stack == null));
            if (stack != null)
            {
                Assert.That(stack.Amount, Is.EqualTo(fits ? existingAmount + 1 : existingAmount));
            }

            if (!fits)
            {
                Assert.That(_changes, Is.Empty);
            }
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
        public void TransferPreviewReservesMergeAndRemainderWithoutChangingInventory()
        {
            InventoryContainer backpack = _model.GetContainer("Backpack");
            InventoryContainer stash = _model.GetContainer("Stash");
            ItemInstance incoming = _model.CreateItem(_ammo, 10);
            ItemInstance destination = _model.CreateItem(_ammo, 57);
            Assert.That(_model.TryAdd(incoming, backpack.Id, Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(destination, stash.Id, Vector2Int.zero, false).Success, Is.True);
            ItemLocation previous = incoming.Location;
            _changes.Clear();

            InventoryResult preview = _model.CanTransfer(incoming.InstanceId, stash.Id, out ItemPlacement placement, out ItemInstance firstMerge);

            Assert.That(preview.Success, Is.True);
            Assert.That(preview.Amount, Is.EqualTo(10));
            Assert.That(firstMerge, Is.SameAs(destination));
            Assert.That(placement.Position, Is.EqualTo(Vector2Int.right));
            Assert.That(incoming.Location, Is.EqualTo(previous));
            Assert.That(incoming.Amount, Is.EqualTo(10));
            Assert.That(destination.Amount, Is.EqualTo(57));
            Assert.That(backpack.Items, Is.EqualTo(new[] { incoming }));
            Assert.That(stash.Items, Is.EqualTo(new[] { destination }));
            Assert.That(stash.GetItemAt(placement.Position), Is.Null);
            Assert.That(_model.ItemCount, Is.EqualTo(2));
            Assert.That(_changes, Is.Empty);

            InventoryResult result = _model.TryTransfer(incoming.InstanceId, stash.Id);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Amount, Is.EqualTo(preview.Amount));
            Assert.That(result.Item, Is.SameAs(incoming));
            Assert.That(destination.Amount, Is.EqualTo(60));
            Assert.That(stash.GetItemAt(placement.Position), Is.SameAs(incoming));
            Assert.That(incoming.Amount, Is.EqualTo(7));
            Assert.That(backpack.Items, Is.Empty);
        }

        [Test]
        public void TransferPreviewRejectsAnUnplacedRemainderButDirectMergeCanMovePart()
        {
            InventoryContainer target = _model.AddContainer(new GridContainer("TinyTransferTarget", 1, 1));
            ItemInstance incoming = _model.CreateItem(_ammo, 5);
            ItemInstance destination = _model.CreateItem(_ammo, 59);
            Assert.That(_model.TryAdd(incoming, "Backpack", Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(destination, target.Id, Vector2Int.zero, false).Success, Is.True);
            _changes.Clear();

            InventoryResult preview = _model.CanTransfer(incoming.InstanceId, target.Id, out _, out _);
            InventoryResult transfer = _model.TryTransfer(incoming.InstanceId, target.Id);

            Assert.That(preview.Failure, Is.EqualTo(InventoryFailure.NoSpace));
            Assert.That(transfer.Failure, Is.EqualTo(preview.Failure));
            Assert.That(incoming.Amount, Is.EqualTo(5));
            Assert.That(incoming.ContainerId, Is.EqualTo("Backpack"));
            Assert.That(destination.Amount, Is.EqualTo(59));
            Assert.That(target.GetItemAt(Vector2Int.zero), Is.SameAs(destination));
            Assert.That(_changes, Is.Empty);

            InventoryResult merge = _model.TryMerge(incoming.InstanceId, destination.InstanceId);
            Assert.That(merge.Amount, Is.EqualTo(1));
            Assert.That(destination.Amount, Is.EqualTo(60));
            Assert.That(incoming.Amount, Is.EqualTo(4));
            Assert.That(incoming.ContainerId, Is.EqualTo("Backpack"));
        }

        [Test]
        public void TransferPreviewAndCommitUseTheCurrentWeightLimit()
        {
            InventoryContainer stash = _model.GetContainer("Stash");
            ItemInstance incoming = _model.CreateItem(_rifle);
            Assert.That(_model.TryAdd(incoming, "Backpack", Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.CanTransfer(incoming.InstanceId, stash.Id, out _, out _).Success, Is.True);

            stash.MaxWeight = 0f;
            _changes.Clear();
            InventoryResult preview = _model.CanTransfer(incoming.InstanceId, stash.Id, out _, out _);
            InventoryResult transfer = _model.TryTransfer(incoming.InstanceId, stash.Id);

            Assert.That(preview.Failure, Is.EqualTo(InventoryFailure.NoSpace));
            Assert.That(transfer.Failure, Is.EqualTo(preview.Failure));
            Assert.That(stash.Items, Is.Empty);
            Assert.That(incoming.ContainerId, Is.EqualTo("Backpack"));
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void PartialTransferPreviewOnlyPlansTheRequestedAmount()
        {
            InventoryContainer stash = _model.GetContainer("Stash");
            ItemInstance incoming = _model.CreateItem(_ammo, 40);
            ItemInstance destination = _model.CreateItem(_ammo, 59);
            Assert.That(_model.TryAdd(incoming, "Backpack", Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(destination, stash.Id, Vector2Int.zero, false).Success, Is.True);
            _changes.Clear();

            InventoryResult preview = _model.CanTransfer(incoming.InstanceId, stash.Id, out ItemPlacement placement, out ItemInstance firstMerge, amount: 10);

            Assert.That(preview.Amount, Is.EqualTo(10));
            Assert.That(firstMerge, Is.SameAs(destination));
            Assert.That(incoming.Amount, Is.EqualTo(40));
            Assert.That(destination.Amount, Is.EqualTo(59));
            Assert.That(stash.GetItemAt(placement.Position), Is.Null);
            Assert.That(_changes, Is.Empty);

            InventoryResult transfer = _model.TryTransfer(incoming.InstanceId, stash.Id, amount: 10);
            Assert.That(transfer.Amount, Is.EqualTo(preview.Amount));
            Assert.That(transfer.Item, Is.Not.SameAs(incoming));
            Assert.That(transfer.Item.Amount, Is.EqualTo(9));
            Assert.That(stash.GetItemAt(placement.Position), Is.SameAs(transfer.Item));
            Assert.That(destination.Amount, Is.EqualTo(60));
            Assert.That(incoming.Amount, Is.EqualTo(30));
            Assert.That(incoming.ContainerId, Is.EqualTo("Backpack"));
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
            var validator = new TestPlacementValidator((_, container, _) => container.Id != "Stash");
            _model.AddPlacementValidator(validator);
            ItemInstance rifle = _model.CreateItem(_rifle);

            Assert.That(_model.TryAdd(rifle, "Stash", Vector2Int.zero, false).Failure, Is.EqualTo(InventoryFailure.ValidatorRejected));
            Assert.That(_model.TryAdd(rifle, "Backpack", Vector2Int.zero, false).Success, Is.True);
            _model.RemovePlacementValidator(validator);
            Assert.That(_model.TryMove(rifle.InstanceId, "Stash", Vector2Int.zero, false).Success, Is.True);
        }

        [Test]
        public void AddCommitsThePlacementApprovedByTheValidator()
        {
            int calls = 0;
            _model.AddPlacementValidator(new TestPlacementValidator((_, _, _) => ++calls == 1));
            ItemInstance rifle = _model.CreateItem(_rifle);
            Vector2Int position = new(3, 3);

            Assert.That(_model.TryAdd(rifle, "Backpack", position, false).Success, Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_model.GetContainer("Backpack").GetItemAt(position), Is.SameAs(rifle));
            Assert.That(rifle.Location.Position, Is.EqualTo(position));
            Assert.That(rifle.Location.SlotId, Is.EqualTo(SlotId.None));
        }

        [Test]
        public void ArrangeRejectsDuplicateItemsWithoutChangingOccupancy()
        {
            InventoryContainer backpack = _model.GetContainer("Backpack");
            ItemInstance rifle = _model.CreateItem(_rifle);
            ItemInstance medkit = _model.CreateItem(_medkit);
            Vector2Int riflePosition = new(1, 1);
            Vector2Int medkitPosition = new(9, 7);
            Assert.That(_model.TryAdd(rifle, backpack.Id, riflePosition, false).Success, Is.True);
            Assert.That(_model.TryAdd(medkit, backpack.Id, medkitPosition, false).Success, Is.True);
            _changes.Clear();

            Assert.That(_model.TryArrange(backpack.Id, new[] { rifle, rifle }, new[] { 1, 1 }).Failure, Is.EqualTo(InventoryFailure.Duplicate));
            Assert.That(backpack.GetItemAt(riflePosition), Is.SameAs(rifle));
            Assert.That(backpack.GetItemAt(medkitPosition), Is.SameAs(medkit));
            Assert.That(backpack.Items, Is.EqualTo(new[] { rifle, medkit }));
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void MergeFromMovePreviewUsesTheSameWeightLimitAsCommit()
        {
            InventoryContainer stash = _model.GetContainer("Stash");
            ItemInstance target = _model.CreateItem(_medkit, 2);
            ItemInstance source = _model.CreateItem(_medkit, 1);
            Assert.That(_model.TryAdd(target, stash.Id, Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(source, "Backpack", Vector2Int.zero, false).Success, Is.True);
            stash.MaxWeight = target.TotalWeight;

            Assert.That(_model.CanMove(source.InstanceId, stash.Id, Vector2Int.zero, false).Failure, Is.EqualTo(InventoryFailure.NoSpace));
            Assert.That(_model.TryMove(source.InstanceId, stash.Id, Vector2Int.zero, false).Failure, Is.EqualTo(InventoryFailure.NoSpace));
            Assert.That(target.Amount, Is.EqualTo(2));
            Assert.That(source.ContainerId, Is.EqualTo("Backpack"));
        }

        [Test]
        public void ReconfigurePreviewPreservesContainerOrdering()
        {
            InventoryContainer backpack = _model.GetContainer("Backpack");
            ItemInstance rifle = _model.CreateItem(_rifle);
            ItemInstance ammo = _model.CreateItem(_ammo);
            ItemInstance returned = _model.CreateItem(_medkit);
            Assert.That(_model.TryAdd(rifle, backpack.Id, Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(ammo, backpack.Id, new Vector2Int(5, 0), false).Success, Is.True);
            _changes.Clear();

            InventoryResult preview = _model.TryReconfigureItem(rifle, rifle.CreateDraft(), rifle.Location.Position, returned: returned, returnContainerId: backpack.Id, apply: false);

            Assert.That(preview.Success, Is.True);
            Assert.That(backpack.Items, Is.EqualTo(new[] { rifle, ammo }));
            Assert.That(backpack.GetItemAt(Vector2Int.zero), Is.SameAs(rifle));
            Assert.That(returned.Location.IsPlaced, Is.False);
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void ChangesFromAListenerFollowTheCurrentNotification()
        {
            ItemInstance first = _model.CreateItem(_ammo);
            ItemInstance second = _model.CreateItem(_ammo);
            var seen = new List<ItemInstance>();

            _model.Changed += change =>
            {
                if (change.Kind == InventoryChangeKind.ItemAdded && change.Item == first)
                {
                    _model.TryAdd(second, "Backpack", Vector2Int.right, false);
                }
            };

            _model.Changed += change =>
            {
                if (change.Kind == InventoryChangeKind.ItemAdded)
                {
                    seen.Add(change.Item);
                }
            };

            Assert.That(_model.TryAdd(first, "Backpack", Vector2Int.zero, false).Success, Is.True);
            Assert.That(seen, Is.EqualTo(new[] { first, second }));
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
        public void ConsumeMultipleStacksNotifiesOnlyAfterAllCountsAndStateCommit()
        {
            ItemInstance weapon = _model.CreateItem(_rifle);
            ItemInstance first = _model.CreateItem(_ammo, 4);
            ItemInstance second = _model.CreateItem(_ammo, 8);
            Assert.That(_model.TryAdd(weapon, "Stash", Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(first, "Backpack", Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(second, "Backpack", Vector2Int.right, false).Success, Is.True);
            var reductions = new KeyValuePair<ItemInstance, int>[] { new(first, 4), new(second, 2) };
            ItemInstance draft = weapon.CreateDraft();
            draft.SetState("magazine", "6");
            _changes.Clear();

            _model.Changed += _ =>
            {
                Assert.That(_model.ContainsItem(first), Is.False);
                Assert.That(second.Amount, Is.EqualTo(6));
                Assert.That(weapon.GetState("magazine"), Is.EqualTo("6"));
            };

            Assert.That(_model.TryConsumeStacksAndUpdate(weapon, draft, reductions).Amount, Is.EqualTo(6));
            Assert.That(_changes.Count, Is.EqualTo(3));
            Assert.That(_changes[0].Kind, Is.EqualTo(InventoryChangeKind.ItemRemoved));
            Assert.That(_changes[1].Kind, Is.EqualTo(InventoryChangeKind.AmountChanged));
            Assert.That(_changes[2].Kind, Is.EqualTo(InventoryChangeKind.ItemStateChanged));
        }

        [Test]
        public void RejectedStackReductionLeavesEarlierStacksAndOwnerUntouched()
        {
            ItemInstance weapon = _model.CreateItem(_rifle);
            ItemInstance first = _model.CreateItem(_ammo, 4);
            ItemInstance second = _model.CreateItem(_ammo, 8);
            Assert.That(_model.TryAdd(weapon, "Stash", Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(first, "Backpack", Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(second, "Backpack", Vector2Int.right, false).Success, Is.True);
            var reductions = new KeyValuePair<ItemInstance, int>[] { new(first, 4), new(second, 9) };
            ItemInstance draft = weapon.CreateDraft();
            draft.SetState("magazine", "13");
            _changes.Clear();

            Assert.That(_model.TryConsumeStacksAndUpdate(weapon, draft, reductions).Failure, Is.EqualTo(InventoryFailure.InvalidAmount));
            Assert.That(first.Amount, Is.EqualTo(4));
            Assert.That(second.Amount, Is.EqualTo(8));
            Assert.That(weapon.GetState("magazine"), Is.Null);
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void ReturnRejectsAllChangesIfTheLastStackCannotBePlaced()
        {
            ItemDefinition ammo = _items.Create("small_box", Vector2Int.one, 3);
            ItemInstance weapon = _model.CreateItem(_rifle);
            ItemInstance stack = _model.CreateItem(ammo, 2);
            var destination = _model.AddContainer(new GridContainer("Small", 2, 1));
            weapon.SetState("magazine", "6");
            Assert.That(_model.TryAdd(weapon, "Stash", Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(stack, destination.Id, Vector2Int.zero, false).Success, Is.True);
            ItemInstance draft = weapon.CreateDraft();
            draft.SetState("magazine", "0");
            _changes.Clear();

            Assert.That(_model.TryReturnRoundsAndUpdate(weapon, draft, destination, ammo, 6).Failure, Is.EqualTo(InventoryFailure.NoSpace));
            Assert.That(stack.Amount, Is.EqualTo(2));
            Assert.That(destination.Items, Is.EqualTo(new[] { stack }));
            Assert.That(destination.GetItemAt(Vector2Int.right), Is.Null);
            Assert.That(weapon.GetState("magazine"), Is.EqualTo("6"));
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void ReturnPlansGridGrowthAndPublishesOnlyTheFinalMagazineAndStacks()
        {
            ItemDefinition ammo = _items.Create("small_box", Vector2Int.one, 3);
            ItemInstance weapon = _model.CreateItem(_rifle);
            ItemInstance stack = _model.CreateItem(ammo, 2);
            var destination = _model.AddContainer(new GridContainer("Expandable", 1, 1, null, null, 1));
            weapon.SetState("magazine", "7");
            Assert.That(_model.TryAdd(weapon, "Stash", Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(stack, destination.Id, Vector2Int.zero, false).Success, Is.True);
            ItemInstance draft = weapon.CreateDraft();
            draft.SetState("magazine", "0");
            _changes.Clear();

            _model.Changed += _ =>
            {
                Assert.That(weapon.GetState("magazine"), Is.EqualTo("0"));
                Assert.That(stack.Amount, Is.EqualTo(3));
                Assert.That(destination.GetItemAt(Vector2Int.up)?.Amount, Is.EqualTo(3));
                Assert.That(destination.GetItemAt(Vector2Int.up * 2)?.Amount, Is.EqualTo(3));
                Assert.That(destination.Height, Is.EqualTo(3));
            };

            Assert.That(_model.TryReturnRoundsAndUpdate(weapon, draft, destination, ammo, 7).Amount, Is.EqualTo(7));
            Assert.That(_changes.Count, Is.EqualTo(4));
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
    }
}
