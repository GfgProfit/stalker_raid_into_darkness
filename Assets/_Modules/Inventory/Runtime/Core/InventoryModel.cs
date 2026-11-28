using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    public sealed class InventoryModel
    {
        private readonly Dictionary<string, InventoryContainer> _containers = new(StringComparer.Ordinal);
        private readonly List<InventoryContainer> _containerList = new();
        private readonly Dictionary<string, ItemInstance> _items = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ISlotValidator> _slotValidators = new(StringComparer.Ordinal);
        private readonly List<IInventoryPlacementValidator> _placementValidators = new();
        private readonly List<ItemInstance> _overlaps = new();
        private readonly List<InventoryChange> _pendingChanges = new();
        private readonly List<EquipEvent> _pendingEquipEvents = new();
        private readonly List<InventoryItemData> _unresolvedItems = new();

        public InventoryModel(IItemDefinitionResolver resolver = null)
        {
            Resolver = resolver;
        }

        public IItemDefinitionResolver Resolver { get; set; }
        public IReadOnlyList<InventoryContainer> Containers => _containerList;
        public IEnumerable<ItemInstance> Items => _items.Values;
        public int ItemCount => _items.Count;
        public IReadOnlyList<InventoryItemData> UnresolvedItems => _unresolvedItems;

        public event Action<InventoryChange> Changed;
        public event Action<ItemInstance, SlottedContainer, SlotDefinition> ItemEquipped;
        public event Action<ItemInstance, SlottedContainer, SlotDefinition> ItemUnequipped;

        public T AddContainer<T>(T container) where T : InventoryContainer
        {
            if (container == null || string.IsNullOrEmpty(container.Id))
            {
                throw new ArgumentException("Container must have an id.");
            }

            if (_containers.ContainsKey(container.Id))
            {
                throw new ArgumentException($"Container '{container.Id}' already exists.");
            }

            container.Model = this;
            _containers.Add(container.Id, container);
            _containerList.Add(container);
            Emit(new InventoryChange(InventoryChangeKind.ContainerAdded, null, container.Id, ItemLocation.None));
            Flush();
            return container;
        }

        public bool RemoveContainer(string containerId)
        {
            if (_containers.TryGetValue(containerId, out InventoryContainer container) == false)
            {
                return false;
            }

            var items = new List<ItemInstance>(container.Items);

            foreach (ItemInstance item in items)
            {
                ItemLocation previous = item.Location;
                ReleaseInternal(item, container);
                _items.Remove(item.InstanceId);
                Emit(new InventoryChange(InventoryChangeKind.ItemRemoved, item, container.Id, previous));
            }

            container.Model = null;
            _containers.Remove(containerId);
            _containerList.Remove(container);
            Emit(new InventoryChange(InventoryChangeKind.ContainerRemoved, null, containerId, ItemLocation.None));
            Flush();
            return true;
        }

        public InventoryContainer GetContainer(string containerId) => containerId != null && _containers.TryGetValue(containerId, out InventoryContainer container) ? container : null;
        public bool TryGetContainer<T>(string containerId, out T container) where T : InventoryContainer => (container = GetContainer(containerId) as T) != null;
        public ItemInstance GetItem(string instanceId) => instanceId != null && _items.TryGetValue(instanceId, out ItemInstance item) ? item : null;
        public bool ContainsItem(ItemInstance item) => item != null && _items.TryGetValue(item.InstanceId, out ItemInstance stored) && stored == item;

        public float GetOwnerWeight(string ownerId)
        {
            float weight = 0f;

            foreach (InventoryContainer container in _containerList)
            {
                if (container.OwnerId == ownerId)
                {
                    weight += container.TotalWeight;
                }
            }

            return weight;
        }

        public void RegisterSlotValidator(string validatorId, ISlotValidator validator)
        {
            if (string.IsNullOrEmpty(validatorId) == false && validator != null)
            {
                _slotValidators[validatorId] = validator;
            }
        }

        public void UnregisterSlotValidator(string validatorId) => _slotValidators.Remove(validatorId ?? string.Empty);
        public void AddPlacementValidator(IInventoryPlacementValidator validator) => _placementValidators.Add(validator);
        public void RemovePlacementValidator(IInventoryPlacementValidator validator) => _placementValidators.Remove(validator);

        public ItemInstance CreateItem(ItemDefinition definition, int amount = 1) => new(definition, amount) { Resolver = Resolver };

        public void NotifyItemChanged(ItemInstance item)
        {
            if (ContainsItem(item))
            {
                Emit(new InventoryChange(InventoryChangeKind.ItemStateChanged, item, item.ContainerId, item.Location));
                Flush();
            }
        }

        public InventoryResult TryReconfigureItem(ItemInstance item, ItemInstance updated, Vector2Int position, ItemInstance consumed = null, ItemInstance returned = null, string returnContainerId = null, bool apply = true, bool mergeReturned = false, ItemInstance returnedStack = null)
        {
            if (ContainsItem(item) == false || updated == null || updated.Definition != item.Definition || updated.Amount != item.Amount || updated.InstanceId != item.InstanceId || consumed == item || (consumed != null && ContainsItem(consumed) == false) || (returned != null && (_items.ContainsKey(returned.InstanceId) || returned.Location.IsPlaced)))
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            if (returnedStack != null && (returned == null || returned.DefinitionId == returnedStack.DefinitionId || returned.InstanceId == returnedStack.InstanceId || _items.ContainsKey(returnedStack.InstanceId) || returnedStack.Location.IsPlaced))
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            InventoryContainer container = GetContainer(item.ContainerId);
            InventoryContainer source = consumed != null ? GetContainer(consumed.ContainerId) : null;
            InventoryContainer destination = returned != null ? GetContainer(returnContainerId) : null;

            if (container.CanRelease(item, out InventoryFailure failure) == false || (consumed != null && source.CanRelease(consumed, out failure) == false))
            {
                return InventoryResult.Fail(failure);
            }

            if (returned != null && destination == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            updated.Resolver = Resolver;

            if (returned != null)
            {
                returned.Resolver = Resolver;
            }

            if (returnedStack != null)
            {
                returnedStack.Resolver = Resolver;
            }

            var ignored = new List<ItemInstance> { item };

            if (consumed != null && consumed.Amount == 1)
            {
                ignored.Add(consumed);
            }

            if (container.CanPlace(updated, position, item.Location.Rotated, out ItemPlacement placement, out failure, ignored) == false)
            {
                return InventoryResult.Fail(failure);
            }

            foreach (InventoryContainer candidate in _containerList)
            {
                float delta = candidate == container ? updated.TotalWeight - item.TotalWeight : 0f;
                delta -= candidate == source ? consumed.UnitWeight : 0f;
                delta += candidate == destination ? returned.TotalWeight + (returnedStack?.TotalWeight ?? 0f) : 0f;

                if (delta > 0f && WeightAllows(candidate, delta) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }
            }

            ItemLocation previous = item.Location;
            var original = new ItemPlacement(previous.Position, container.GetItemArea(item), previous.Rotated, previous.SlotId);
            ItemLocation consumedLocation = consumed?.Location ?? ItemLocation.None;
            ItemPlacement consumedPlacement = consumed != null ? new ItemPlacement(consumedLocation.Position, source.GetItemArea(consumed), consumedLocation.Rotated, consumedLocation.SlotId) : default;
            ItemPlacement returnedPlacement = default;
            ItemPlacement stackPlacement = default;
            int returnedAmount = returned?.Amount ?? 0;
            int stackAmount = returnedStack?.Amount ?? 0;
            List<KeyValuePair<ItemInstance, int>> returnMerges = null;
            List<KeyValuePair<ItemInstance, int>> stackMerges = null;

            if (mergeReturned && returned?.IsStackable == true)
            {
                returnMerges = PlanMerges(returned, returnedAmount, destination, item, consumed);
                returnedAmount -= SumMerges(returnMerges);
            }

            if (returnedStack?.IsStackable == true)
            {
                stackMerges = PlanMerges(returnedStack, stackAmount, destination, item, consumed);
                stackAmount -= SumMerges(stackMerges);
            }

            if (returnedAmount > 0 || stackAmount > 0)
            {
                bool found;
                container.Release(item);

                if (consumed != null && consumed.Amount == 1)
                {
                    source.Release(consumed);
                }

                container.Occupy(updated, placement);

                try
                {
                    found = TryPlaceReturns(destination, returned, returnedAmount, returnedStack, stackAmount, out returnedPlacement, out stackPlacement);
                }
                finally
                {
                    container.Release(updated);
                    container.Occupy(item, original);

                    if (consumed != null && consumed.Amount == 1)
                    {
                        source.Occupy(consumed, consumedPlacement);
                    }
                }

                if (found == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }
            }

            if (apply == false)
            {
                return InventoryResult.Ok(item, 1);
            }

            if (consumed != null)
            {
                ReduceOrRemove(consumed, 1);
            }

            container.Release(item);
            item.CopyStateFrom(updated);
            container.CommitPlacement(item, placement);

            CommitReturn(returned, returnedAmount, returnMerges, destination, returnedPlacement);
            CommitReturn(returnedStack, stackAmount, stackMerges, destination, stackPlacement);

            Emit(new InventoryChange(InventoryChangeKind.ItemStateChanged, item, container.Id, previous, placement.SlotId));
            Flush();
            return InventoryResult.Ok(item, 1);
        }

        private static bool TryPlaceReturns(InventoryContainer destination, ItemInstance first, int firstAmount, ItemInstance second, int secondAmount, out ItemPlacement firstPlacement, out ItemPlacement secondPlacement)
        {
            firstPlacement = default;
            secondPlacement = default;
            int originalFirstAmount = first?.Amount ?? 0;
            int originalSecondAmount = second?.Amount ?? 0;
            bool firstOccupied = false;

            try
            {
                if (firstAmount > 0)
                {
                    first.Amount = firstAmount;
                    if (!destination.TryFindSpace(first, true, out firstPlacement))
                    {
                        return false;
                    }

                    if (secondAmount > 0)
                    {
                        destination.Occupy(first, firstPlacement);
                        firstOccupied = true;
                    }
                }

                if (secondAmount == 0)
                {
                    return true;
                }

                second.Amount = secondAmount;
                return destination.TryFindSpace(second, true, out secondPlacement);
            }
            finally
            {
                if (firstOccupied)
                {
                    destination.Release(first);
                }

                if (firstAmount > 0)
                {
                    first.Amount = originalFirstAmount;
                }

                if (secondAmount > 0)
                {
                    second.Amount = originalSecondAmount;
                }
            }
        }

        private void CommitReturn(ItemInstance item, int amount, List<KeyValuePair<ItemInstance, int>> merges, InventoryContainer destination, ItemPlacement placement)
        {
            if (merges != null)
            {
                ApplyMerges(merges);
            }

            if (amount > 0)
            {
                item.Amount = amount;
                _items.Add(item.InstanceId, item);
                OccupyInternal(item, destination, placement);
                Emit(new InventoryChange(InventoryChangeKind.ItemAdded, item, destination.Id, ItemLocation.None, placement.SlotId));
            }
        }

        public InventoryResult CanAdd(ItemInstance item, string containerId, Vector2Int position, bool rotated)
        {
            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            if (_items.ContainsKey(item.InstanceId) || item.Location.IsPlaced)
            {
                return InventoryResult.Fail(InventoryFailure.AlreadyInInventory);
            }

            item.Resolver = Resolver;
            InventoryContainer container = GetContainer(containerId);

            if (container == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (container.CanPlace(item, position, rotated, out _, out InventoryFailure failure) == false)
            {
                return InventoryResult.Fail(failure);
            }

            return WeightAllows(container, item.TotalWeight) ? InventoryResult.Ok(item, item.Amount) : InventoryResult.Fail(InventoryFailure.NoSpace);
        }

        public InventoryResult TryAdd(ItemInstance item, string containerId, Vector2Int position, bool rotated)
        {
            InventoryResult check = CanAdd(item, containerId, position, rotated);

            if (check.Success == false)
            {
                return check;
            }

            InventoryContainer container = GetContainer(containerId);
            container.CanPlace(item, position, rotated, out ItemPlacement placement, out _);
            _items.Add(item.InstanceId, item);
            OccupyInternal(item, container, placement);
            Emit(new InventoryChange(InventoryChangeKind.ItemAdded, item, container.Id, ItemLocation.None, placement.SlotId));
            Flush();
            return InventoryResult.Ok(item, item.Amount);
        }

        public InventoryResult TryAddAuto(ItemInstance item, string containerId, bool mergeStacks = true, bool allowRotation = true)
        {
            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            if (_items.ContainsKey(item.InstanceId) || item.Location.IsPlaced)
            {
                return InventoryResult.Fail(InventoryFailure.AlreadyInInventory);
            }

            item.Resolver = Resolver;
            InventoryContainer container = GetContainer(containerId);

            if (container == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (WeightAllows(container, item.TotalWeight) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            List<KeyValuePair<ItemInstance, int>> merges = mergeStacks ? PlanMerges(item, item.Amount, container, null) : new List<KeyValuePair<ItemInstance, int>>();
            int merged = SumMerges(merges);
            int remainder = item.Amount - merged;
            ItemPlacement placement = default;

            if (remainder > 0)
            {
                int original = item.Amount;
                item.Amount = remainder;
                bool found = container.TryFindSpace(item, allowRotation, out placement);
                item.Amount = original;

                if (found == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }
            }

            ItemInstance resultItem = ApplyMerges(merges);

            if (remainder > 0)
            {
                item.Amount = remainder;
                _items.Add(item.InstanceId, item);
                OccupyInternal(item, container, placement);
                Emit(new InventoryChange(InventoryChangeKind.ItemAdded, item, container.Id, ItemLocation.None, placement.SlotId));
                resultItem = item;
            }

            Flush();
            return InventoryResult.Ok(resultItem, merged + remainder);
        }

        public InventoryResult CanMove(string instanceId, string containerId, Vector2Int position, bool rotated, bool allowMerge = true)
        {
            return PlanMove(instanceId, containerId, position, rotated, allowMerge, out _, out _, out _, out _);
        }

        public InventoryResult TryMove(string instanceId, string containerId, Vector2Int position, bool rotated, bool allowMerge = true)
        {
            InventoryResult plan = PlanMove(instanceId, containerId, position, rotated, allowMerge, out ItemInstance item, out InventoryContainer target, out ItemPlacement placement, out ItemInstance mergeTarget);

            if (plan.Success == false)
            {
                return plan;
            }

            if (mergeTarget != null)
            {
                return TryMerge(item.InstanceId, mergeTarget.InstanceId);
            }

            InventoryContainer source = GetContainer(item.ContainerId);
            ItemLocation previous = item.Location;
            ReleaseInternal(item, source);
            OccupyInternal(item, target, placement);
            Emit(new InventoryChange(InventoryChangeKind.ItemMoved, item, target.Id, previous, placement.SlotId));
            Flush();
            return InventoryResult.Ok(item, item.Amount);
        }

        public InventoryResult TryRotate(string instanceId)
        {
            ItemInstance item = GetItem(instanceId);

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (item.Definition.AllowRotation == false)
            {
                return InventoryResult.Fail(InventoryFailure.RotationNotAllowed);
            }

            return TryMove(instanceId, item.ContainerId, item.Location.Position, item.Location.Rotated == false, false);
        }

        public InventoryResult TryMerge(string sourceId, string targetId, int amount = int.MaxValue)
        {
            ItemInstance source = GetItem(sourceId);
            ItemInstance target = GetItem(targetId);

            if (source == null || target == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (source.CanStackWith(target) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NotStackable);
            }

            int moved = Mathf.Min(amount, source.Amount, target.FreeStackSpace);

            if (moved <= 0)
            {
                return InventoryResult.Fail(amount <= 0 ? InventoryFailure.InvalidAmount : InventoryFailure.StackFull);
            }

            InventoryContainer sourceContainer = GetContainer(source.ContainerId);
            InventoryContainer targetContainer = GetContainer(target.ContainerId);

            if (sourceContainer != targetContainer && WeightAllows(targetContainer, source.UnitWeight * moved) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            if (moved == source.Amount && sourceContainer != null && sourceContainer.CanRelease(source, out InventoryFailure releaseFailure) == false)
            {
                return InventoryResult.Fail(releaseFailure);
            }

            target.Amount += moved;
            Emit(new InventoryChange(InventoryChangeKind.AmountChanged, target, target.ContainerId, target.Location));
            ReduceOrRemove(source, moved);
            Flush();
            return InventoryResult.Ok(target, moved);
        }

        public InventoryResult CanSplit(string instanceId, int amount, string containerId, Vector2Int position, bool rotated)
        {
            return PlanSplit(instanceId, amount, containerId, position, rotated, out _, out _, out _, out _, out _);
        }

        public InventoryResult TrySplit(string instanceId, int amount, string containerId, Vector2Int position, bool rotated)
        {
            InventoryResult plan = PlanSplit(instanceId, amount, containerId, position, rotated, out ItemInstance item, out InventoryContainer target, out ItemPlacement placement, out ItemInstance mergeTarget, out int moved);

            if (plan.Success == false)
            {
                return plan;
            }

            if (mergeTarget != null)
            {
                mergeTarget.Amount += moved;
                Emit(new InventoryChange(InventoryChangeKind.AmountChanged, mergeTarget, mergeTarget.ContainerId, mergeTarget.Location));
                ReduceOrRemove(item, moved);
                Flush();
                return InventoryResult.Ok(mergeTarget, moved);
            }

            ItemInstance piece = CreatePiece(item, moved);
            item.Amount -= moved;
            Emit(new InventoryChange(InventoryChangeKind.AmountChanged, item, item.ContainerId, item.Location));
            _items.Add(piece.InstanceId, piece);
            OccupyInternal(piece, target, placement);
            Emit(new InventoryChange(InventoryChangeKind.ItemAdded, piece, target.Id, ItemLocation.None, placement.SlotId));
            Flush();
            return InventoryResult.Ok(piece, moved);
        }

        public InventoryResult TrySplitAuto(string instanceId, int amount, string containerId = null)
        {
            ItemInstance item = GetItem(instanceId);

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (item.IsStackable == false)
            {
                return InventoryResult.Fail(InventoryFailure.NotStackable);
            }

            if (amount <= 0 || amount >= item.Amount)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidAmount);
            }

            InventoryContainer target = GetContainer(containerId ?? item.ContainerId);

            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            ItemInstance probe = CreatePiece(item, amount);

            if (target.TryFindSpace(probe, true, out ItemPlacement placement) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            return TrySplit(instanceId, amount, target.Id, placement.Position, placement.Rotated);
        }

        public InventoryResult TryTransfer(string instanceId, string containerId, int amount = int.MaxValue, bool mergeStacks = true)
        {
            ItemInstance item = GetItem(instanceId);

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            InventoryContainer target = GetContainer(containerId);

            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (target.Id == item.ContainerId)
            {
                return InventoryResult.Fail(InventoryFailure.Duplicate);
            }

            int moving = Mathf.Min(amount, item.Amount);

            if (moving <= 0)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidAmount);
            }

            bool whole = moving == item.Amount;
            InventoryContainer source = GetContainer(item.ContainerId);

            if (whole && source != null && source.CanRelease(item, out InventoryFailure releaseFailure) == false)
            {
                return InventoryResult.Fail(releaseFailure);
            }

            if (WeightAllows(target, item.UnitWeight * moving) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            List<KeyValuePair<ItemInstance, int>> merges = mergeStacks ? PlanMerges(item, moving, target, item) : new List<KeyValuePair<ItemInstance, int>>();
            int merged = SumMerges(merges);
            int remainder = moving - merged;
            ItemPlacement placement = default;
            ItemInstance carrier = whole ? item : CreatePiece(item, remainder);

            if (remainder > 0)
            {
                int original = carrier.Amount;
                carrier.Amount = remainder;
                bool found = target.TryFindSpace(carrier, true, out placement);
                carrier.Amount = original;

                if (found == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }
            }

            ItemInstance resultItem = null;

            foreach (KeyValuePair<ItemInstance, int> merge in merges)
            {
                merge.Key.Amount += merge.Value;
                Emit(new InventoryChange(InventoryChangeKind.AmountChanged, merge.Key, merge.Key.ContainerId, merge.Key.Location));
                resultItem = merge.Key;
            }

            if (remainder > 0)
            {
                if (whole)
                {
                    ItemLocation previous = item.Location;
                    item.Amount = remainder;
                    ReleaseInternal(item, source);
                    OccupyInternal(item, target, placement);
                    Emit(new InventoryChange(InventoryChangeKind.ItemMoved, item, target.Id, previous, placement.SlotId));
                    resultItem = item;
                }
                else
                {
                    item.Amount -= moving;
                    Emit(new InventoryChange(InventoryChangeKind.AmountChanged, item, item.ContainerId, item.Location));
                    carrier.Amount = remainder;
                    _items.Add(carrier.InstanceId, carrier);
                    OccupyInternal(carrier, target, placement);
                    Emit(new InventoryChange(InventoryChangeKind.ItemAdded, carrier, target.Id, ItemLocation.None, placement.SlotId));
                    resultItem = carrier;
                }
            }
            else
            {
                ReduceOrRemove(item, moving);
            }

            Flush();
            return InventoryResult.Ok(resultItem, moving);
        }

        public InventoryResult TryRemove(string instanceId, int amount = int.MaxValue)
        {
            ItemInstance item = GetItem(instanceId);

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (amount <= 0)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidAmount);
            }

            if (amount >= item.Amount)
            {
                InventoryContainer container = GetContainer(item.ContainerId);

                if (container != null && container.CanRelease(item, out InventoryFailure failure) == false)
                {
                    return InventoryResult.Fail(failure);
                }

                int removedAmount = item.Amount;
                ReduceOrRemove(item, removedAmount);
                Flush();
                return InventoryResult.Ok(item, removedAmount);
            }

            ItemInstance piece = CreatePiece(item, amount);
            item.Amount -= amount;
            Emit(new InventoryChange(InventoryChangeKind.AmountChanged, item, item.ContainerId, item.Location));
            Flush();
            return InventoryResult.Ok(piece, amount);
        }

        public InventoryResult TryEquip(string instanceId, string equipmentContainerId)
        {
            ItemInstance item = GetItem(instanceId);

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (TryGetContainer(equipmentContainerId, out SlottedContainer equipment) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            InventoryFailure lastFailure = InventoryFailure.NoSlot;

            foreach (SlotDefinition slot in EnumerateEquipCandidates(item, equipment))
            {
                InventoryResult plan = CanMove(item.InstanceId, equipment.Id, slot.Bounds.position, false, false);

                if (plan.Success)
                {
                    return TryMove(item.InstanceId, equipment.Id, slot.Bounds.position, false, false);
                }

                lastFailure = plan.Failure;
            }

            return InventoryResult.Fail(lastFailure);
        }

        public InventoryResult TrySetSlotLocked(string containerId, string slotId, bool locked)
        {
            if (TryGetContainer(containerId, out SlottedContainer container) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (container.GetSlot(slotId) == null)
            {
                return InventoryResult.Fail(InventoryFailure.NoSlot);
            }

            if (container.CanSetSlotLocked(slotId, locked) == false)
            {
                return InventoryResult.Fail(InventoryFailure.Occupied);
            }

            if (container.SetSlotLocked(slotId, locked))
            {
                Emit(new InventoryChange(InventoryChangeKind.SlotStateChanged, null, containerId, ItemLocation.None, slotId));
                Flush();
            }

            return InventoryResult.Ok(null, 0);
        }

        public InventoryResult TryArrange(string containerId, IReadOnlyList<ItemInstance> order, IReadOnlyList<int> groups, bool allowRotation = true)
        {
            InventoryContainer container = GetContainer(containerId);

            if (container == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (container is SlottedContainer || order == null || groups == null || order.Count != groups.Count || order.Count != container.Items.Count)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            var occupied = new bool[container.Width, container.Height];
            var placements = new ItemPlacement[order.Count];
            int rowEnd = -1;
            int startRow = 0;

            for (int i = 0; i < order.Count; i++)
            {
                ItemInstance item = order[i];

                if (item == null || container.Contains(item) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.ItemNotFound);
                }

                if (i == 0 || groups[i] != groups[i - 1])
                {
                    startRow = rowEnd + 1;
                }

                if (TryFitArranged(occupied, item, startRow, allowRotation, out ItemPlacement placement) == false && TryFitArranged(occupied, item, 0, allowRotation, out placement) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }

                for (int x = placement.Area.xMin; x < placement.Area.xMax; x++)
                {
                    for (int y = placement.Area.yMin; y < placement.Area.yMax; y++)
                    {
                        occupied[x, y] = true;
                    }
                }

                rowEnd = Mathf.Max(rowEnd, placement.Area.yMax - 1);
                placements[i] = placement;
            }

            var previous = new ItemLocation[order.Count];
            bool changed = false;

            for (int i = 0; i < order.Count; i++)
            {
                previous[i] = order[i].Location;
                changed |= previous[i].Position != placements[i].Position || previous[i].Rotated != placements[i].Rotated;
            }

            if (changed == false)
            {
                return InventoryResult.Ok(null, 0);
            }

            for (int i = 0; i < order.Count; i++)
            {
                container.Release(order[i]);
            }

            for (int i = 0; i < order.Count; i++)
            {
                container.Occupy(order[i], placements[i]);

                if (previous[i].Position != placements[i].Position || previous[i].Rotated != placements[i].Rotated)
                {
                    Emit(new InventoryChange(InventoryChangeKind.ItemMoved, order[i], container.Id, previous[i]));
                }
            }

            Flush();
            return InventoryResult.Ok(null, order.Count);
        }

        private static bool TryFitArranged(bool[,] occupied, ItemInstance item, int startRow, bool allowRotation, out ItemPlacement placement)
        {
            int width = occupied.GetLength(0);
            int height = occupied.GetLength(1);

            for (int pass = 0; pass < (allowRotation && item.Definition.AllowRotation ? 2 : 1); pass++)
            {
                bool rotated = pass == 1;
                Vector2Int size = item.GetSize(rotated);

                for (int y = Mathf.Max(0, startRow); y <= height - size.y; y++)
                {
                    for (int x = 0; x <= width - size.x; x++)
                    {
                        if (IsFree(occupied, x, y, size))
                        {
                            var position = new Vector2Int(x, y);
                            placement = new ItemPlacement(position, new RectInt(position, size), rotated, string.Empty);
                            return true;
                        }
                    }
                }
            }

            placement = default;
            return false;
        }

        private static bool IsFree(bool[,] occupied, int startX, int startY, Vector2Int size)
        {
            for (int x = startX; x < startX + size.x; x++)
            {
                for (int y = startY; y < startY + size.y; y++)
                {
                    if (occupied[x, y])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public void Clear()
        {
            foreach (InventoryContainer container in _containerList)
            {
                var items = new List<ItemInstance>(container.Items);

                foreach (ItemInstance item in items)
                {
                    ReleaseInternal(item, container);
                }
            }

            _items.Clear();
            _unresolvedItems.Clear();
            Emit(new InventoryChange(InventoryChangeKind.Cleared, null, string.Empty, ItemLocation.None));
            Flush();
        }

        internal bool ValidatePlacement(ItemInstance item, InventoryContainer container, ItemPlacement placement, out InventoryFailure failure)
        {
            for (int i = 0; i < _placementValidators.Count; i++)
            {
                if (_placementValidators[i].CanPlace(item, container, placement) == false)
                {
                    failure = InventoryFailure.ValidatorRejected;
                    return false;
                }
            }

            failure = InventoryFailure.None;
            return true;
        }

        internal bool ValidateSlot(ItemInstance item, SlottedContainer container, SlotDefinition slot)
        {
            string validatorId = slot.Rule.ValidatorId;

            if (string.IsNullOrEmpty(validatorId))
            {
                return true;
            }

            return _slotValidators.TryGetValue(validatorId, out ISlotValidator validator) == false || validator.CanPlace(item, container, slot);
        }

        internal bool RestoreItem(ItemInstance item, InventoryContainer container, Vector2Int position, bool rotated)
        {
            item.Resolver = Resolver;

            if (container.CanPlace(item, position, rotated, out ItemPlacement placement, out _) == false && container.TryFindSpace(item, true, out placement) == false)
            {
                return false;
            }

            _items.Add(item.InstanceId, item);
            OccupyInternal(item, container, placement);
            Emit(new InventoryChange(InventoryChangeKind.ItemAdded, item, container.Id, ItemLocation.None, placement.SlotId));
            Flush();
            return true;
        }

        internal void AddUnresolved(IEnumerable<InventoryItemData> items) => _unresolvedItems.AddRange(items);

        internal void RestoreSlotLock(SlottedContainer container, string slotId, bool locked)
        {
            if (container.SetSlotLocked(slotId, locked))
            {
                Emit(new InventoryChange(InventoryChangeKind.SlotStateChanged, null, container.Id, ItemLocation.None, slotId));
                Flush();
            }
        }

        private InventoryResult PlanMove(string instanceId, string containerId, Vector2Int position, bool rotated, bool allowMerge, out ItemInstance item, out InventoryContainer target, out ItemPlacement placement, out ItemInstance mergeTarget)
        {
            item = GetItem(instanceId);
            target = GetContainer(containerId);
            placement = default;
            mergeTarget = null;

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (target.TryResolvePlacement(item, position, rotated, out placement, out InventoryFailure failure) == false)
            {
                if (allowMerge && TryFindMergeTargetAt(item, target, position, out mergeTarget))
                {
                    return InventoryResult.Ok(item, Mathf.Min(item.Amount, mergeTarget.FreeStackSpace));
                }

                return InventoryResult.Fail(failure);
            }

            target.CollectOverlaps(placement.Area, item, _overlaps);

            if (_overlaps.Count > 0)
            {
                if (allowMerge && _overlaps.Count == 1 && _overlaps[0].CanStackWith(item) && _overlaps[0].IsFullStack == false)
                {
                    mergeTarget = _overlaps[0];
                    return InventoryResult.Ok(item, Mathf.Min(item.Amount, mergeTarget.FreeStackSpace));
                }

                return InventoryResult.Fail(InventoryFailure.Occupied);
            }

            if (target.CanPlace(item, position, rotated, out placement, out failure) == false)
            {
                return InventoryResult.Fail(failure);
            }

            InventoryContainer source = GetContainer(item.ContainerId);
            bool sameSlot = source == target && string.IsNullOrEmpty(placement.SlotId) == false && placement.SlotId == item.Location.SlotId;

            if (source != null && sameSlot == false && source.CanRelease(item, out InventoryFailure releaseFailure) == false)
            {
                return InventoryResult.Fail(releaseFailure);
            }

            if (source != target && WeightAllows(target, item.TotalWeight) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            return InventoryResult.Ok(item, item.Amount);
        }

        private InventoryResult PlanSplit(string instanceId, int amount, string containerId, Vector2Int position, bool rotated, out ItemInstance item, out InventoryContainer target, out ItemPlacement placement, out ItemInstance mergeTarget, out int moved)
        {
            item = GetItem(instanceId);
            target = GetContainer(containerId);
            placement = default;
            mergeTarget = null;
            moved = 0;

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (target == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (item.IsStackable == false)
            {
                return InventoryResult.Fail(InventoryFailure.NotStackable);
            }

            if (amount <= 0 || amount >= item.Amount)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidAmount);
            }

            ItemInstance probe = CreatePiece(item, amount);
            InventoryFailure failure;

            if (target.TryResolvePlacement(probe, position, rotated, out placement, out failure))
            {
                target.CollectOverlaps(placement.Area, probe, _overlaps);
            }
            else
            {
                _overlaps.Clear();

                if (TryFindMergeTargetAt(probe, target, position, out ItemInstance stack))
                {
                    _overlaps.Add(stack);
                }
                else
                {
                    return InventoryResult.Fail(failure);
                }
            }

            if (_overlaps.Count > 0)
            {
                ItemInstance candidate = _overlaps[0];

                if (_overlaps.Count == 1 && candidate != item && candidate.CanStackWith(item) && candidate.IsFullStack == false)
                {
                    mergeTarget = candidate;
                    moved = Mathf.Min(amount, candidate.FreeStackSpace);
                    InventoryContainer mergeContainer = GetContainer(candidate.ContainerId);
                    return mergeContainer != GetContainer(item.ContainerId) && WeightAllows(mergeContainer, item.UnitWeight * moved) == false ? InventoryResult.Fail(InventoryFailure.NoSpace) : InventoryResult.Ok(candidate, moved);
                }

                return InventoryResult.Fail(InventoryFailure.Occupied);
            }

            if (target.CanPlace(probe, position, rotated, out placement, out failure) == false)
            {
                return InventoryResult.Fail(failure);
            }

            if (target != GetContainer(item.ContainerId) && WeightAllows(target, probe.TotalWeight) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            moved = amount;
            return InventoryResult.Ok(item, amount);
        }

        private bool TryFindMergeTargetAt(ItemInstance item, InventoryContainer container, Vector2Int cell, out ItemInstance stack)
        {
            stack = container.GetItemAt(cell);

            if (stack != null && stack != item && stack.CanStackWith(item) && stack.IsFullStack == false)
            {
                return true;
            }

            stack = null;
            return false;
        }

        private List<KeyValuePair<ItemInstance, int>> PlanMerges(ItemInstance item, int amount, InventoryContainer container, ItemInstance exclude, ItemInstance consumed = null)
        {
            var merges = new List<KeyValuePair<ItemInstance, int>>();
            int remaining = amount;

            if (item.IsStackable == false)
            {
                return merges;
            }

            foreach (ItemInstance candidate in container.Items)
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (candidate == exclude || candidate.CanStackWith(item) == false)
                {
                    continue;
                }

                int free = candidate.FreeStackSpace;
                if (candidate == consumed)
                {
                    free = candidate.Amount > 1 ? free + 1 : 0;
                }

                if (free == 0)
                {
                    continue;
                }

                int moved = Mathf.Min(remaining, free);
                merges.Add(new KeyValuePair<ItemInstance, int>(candidate, moved));
                remaining -= moved;
            }

            return merges;
        }

        private static int SumMerges(List<KeyValuePair<ItemInstance, int>> merges)
        {
            int sum = 0;

            foreach (KeyValuePair<ItemInstance, int> merge in merges)
            {
                sum += merge.Value;
            }

            return sum;
        }

        private ItemInstance ApplyMerges(List<KeyValuePair<ItemInstance, int>> merges)
        {
            ItemInstance last = null;

            foreach (KeyValuePair<ItemInstance, int> merge in merges)
            {
                merge.Key.Amount += merge.Value;
                Emit(new InventoryChange(InventoryChangeKind.AmountChanged, merge.Key, merge.Key.ContainerId, merge.Key.Location));
                last = merge.Key;
            }

            return last;
        }

        private void ReduceOrRemove(ItemInstance item, int amount)
        {
            if (amount < item.Amount)
            {
                item.Amount -= amount;
                Emit(new InventoryChange(InventoryChangeKind.AmountChanged, item, item.ContainerId, item.Location));
                return;
            }

            InventoryContainer container = GetContainer(item.ContainerId);
            ItemLocation previous = item.Location;
            ReleaseInternal(item, container);
            _items.Remove(item.InstanceId);
            Emit(new InventoryChange(InventoryChangeKind.ItemRemoved, item, previous.ContainerId, previous, previous.SlotId));
        }

        private static ItemInstance CreatePiece(ItemInstance source, int amount)
        {
            var piece = new ItemInstance(source.Definition, amount);
            piece.CopyStateFrom(source);
            return piece;
        }

        private IEnumerable<SlotDefinition> EnumerateEquipCandidates(ItemInstance item, SlottedContainer equipment)
        {
            var yielded = new HashSet<string>(StringComparer.Ordinal);
            EquippableItemComponent equippable = item.Definition.GetComponent<EquippableItemComponent>();

            if (equippable != null)
            {
                foreach (string slotId in equippable.PreferredSlotIds)
                {
                    SlotDefinition slot = equipment.GetSlot(slotId);

                    if (slot != null && slot.Mode == SlotMode.Single && yielded.Add(slot.Id))
                    {
                        yield return slot;
                    }
                }
            }

            foreach (SlotDefinition slot in equipment.Slots)
            {
                if (slot.Mode == SlotMode.Single && slot.Rule.AcceptsDefinition(item.Definition) && yielded.Add(slot.Id))
                {
                    yield return slot;
                }
            }
        }

        private static bool WeightAllows(InventoryContainer container, float addedWeight) => container.MaxWeight < 0f || container.TotalWeight + addedWeight <= container.MaxWeight + 0.0001f;

        private void OccupyInternal(ItemInstance item, InventoryContainer container, ItemPlacement placement)
        {
            item.Resolver = Resolver;
            container.CommitPlacement(item, placement);

            if (container is SlottedContainer slotted)
            {
                SlotDefinition slot = slotted.GetSlot(placement.SlotId);

                if (slotted.IsEquipment && slot != null && slot.Mode == SlotMode.Single)
                {
                    _pendingEquipEvents.Add(new EquipEvent(true, item, slotted, slot));
                }

                Emit(new InventoryChange(InventoryChangeKind.SlotStateChanged, null, slotted.Id, ItemLocation.None, placement.SlotId));
            }
        }

        private void ReleaseInternal(ItemInstance item, InventoryContainer container)
        {
            if (container == null)
            {
                item.Location = ItemLocation.None;
                return;
            }

            string slotId = item.Location.SlotId;
            container.Release(item);

            if (container is SlottedContainer slotted)
            {
                SlotDefinition slot = slotted.GetSlot(slotId);

                if (slotted.IsEquipment && slot != null && slot.Mode == SlotMode.Single)
                {
                    _pendingEquipEvents.Add(new EquipEvent(false, item, slotted, slot));
                }

                Emit(new InventoryChange(InventoryChangeKind.SlotStateChanged, null, slotted.Id, ItemLocation.None, slotId));
            }
        }

        private void Emit(InventoryChange change) => _pendingChanges.Add(change);

        private void Flush()
        {
            if (_pendingChanges.Count == 0 && _pendingEquipEvents.Count == 0)
            {
                return;
            }

            InventoryChange[] changes = _pendingChanges.ToArray();
            EquipEvent[] equipEvents = _pendingEquipEvents.ToArray();
            _pendingChanges.Clear();
            _pendingEquipEvents.Clear();

            for (int i = 0; i < equipEvents.Length; i++)
            {
                EquipEvent equipEvent = equipEvents[i];

                if (equipEvent.Equipped)
                {
                    ItemEquipped?.Invoke(equipEvent.Item, equipEvent.Container, equipEvent.Slot);
                }
                else
                {
                    ItemUnequipped?.Invoke(equipEvent.Item, equipEvent.Container, equipEvent.Slot);
                }
            }

            for (int i = 0; i < changes.Length; i++)
            {
                Changed?.Invoke(changes[i]);
            }
        }
    }
}
