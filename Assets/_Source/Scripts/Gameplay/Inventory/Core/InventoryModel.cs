using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    public sealed class InventoryModel
    {
        private readonly Dictionary<string, InventoryContainer> _containers = new(StringComparer.Ordinal);
        private readonly List<InventoryContainer> _containerList = new();
        private readonly ReadOnlyCollection<InventoryContainer> _containersView;
        private readonly Dictionary<string, ItemInstance> _items = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ISlotValidator> _slotValidators = new(StringComparer.Ordinal);
        private readonly List<IInventoryPlacementValidator> _placementValidators = new();
        private readonly InventoryPlacementPlanner _placementPlanner = new();
        private readonly InventoryNotifications _notifications = new();

        private OutfitSlotRules _outfitRules;
        private bool _outfitLocksDirty;

        public InventoryModel(IItemDefinitionResolver resolver = null)
        {
            Resolver = resolver;
            _containersView = _containerList.AsReadOnly();
        }

        public IItemDefinitionResolver Resolver { get; }
        public IReadOnlyList<InventoryContainer> Containers => _containersView;
        public IEnumerable<ItemInstance> Items => _items.Values;
        public int ItemCount => _items.Count;

        public event Action<InventoryChange> Changed
        {
            add => _notifications.Changed += value;
            remove => _notifications.Changed -= value;
        }

        public event Action<ItemInstance, SlottedContainer, SlotDefinition> ItemEquipped
        {
            add => _notifications.ItemEquipped += value;
            remove => _notifications.ItemEquipped -= value;
        }

        public event Action<ItemInstance, SlottedContainer, SlotDefinition> ItemUnequipped
        {
            add => _notifications.ItemUnequipped += value;
            remove => _notifications.ItemUnequipped -= value;
        }

        internal void BindOutfitSlotRules(OutfitSlotRules rules)
        {
            _outfitRules = rules;
            _outfitLocksDirty = rules != null;
            Flush();
        }

        public T AddContainer<T>(T container) where T : InventoryContainer
        {
            if (container == null || string.IsNullOrEmpty(container.Id))
            {
                throw new ArgumentException("Container must have an id.");
            }

            if (container.Model != null || _containers.ContainsKey(container.Id))
            {
                throw new ArgumentException($"Container '{container.Id}' already belongs to an inventory or conflicts with an existing container.", nameof(container));
            }

            container.Model = this;
            _containers.Add(container.Id, container);
            _containerList.Add(container);
            _notifications.Add(new InventoryChange(InventoryChangeKind.ContainerAdded, null, container.Id, ItemLocation.None));
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
                item.Owner = null;
                _notifications.Add(new InventoryChange(InventoryChangeKind.ItemRemoved, item, container.Id, previous));
            }

            container.Model = null;
            _containers.Remove(containerId);
            _containerList.Remove(container);
            _notifications.Add(new InventoryChange(InventoryChangeKind.ContainerRemoved, null, containerId, ItemLocation.None));
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

        internal void NotifyContainerConstraintsChanged(InventoryContainer container)
        {
            _notifications.Add(new InventoryChange(InventoryChangeKind.ContainerConstraintsChanged, null, container.Id, ItemLocation.None));
            Flush();
        }

        internal bool TrySetItemStateInPlace(ItemInstance item, string key, string value)
        {
            if (ContainsItem(item) == false || string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (item.CommitStateValue(this, key, value))
            {
                _notifications.Add(new InventoryChange(InventoryChangeKind.ItemStateChanged, item, item.ContainerId, item.Location));
                Flush();
            }

            return true;
        }

        public InventoryResult TryReconfigureItem(ItemInstance item, ItemInstance updated, Vector2Int position, ItemInstance consumed = null, ItemInstance returned = null, string returnContainerId = null, bool apply = true, bool mergeReturned = false, ItemInstance returnedStack = null)
        {
            if (ContainsItem(item) == false || updated == null || updated.Owner != null || updated.Location.IsPlaced || updated.Definition != item.Definition || updated.Amount != item.Amount || updated.InstanceId != item.InstanceId || consumed == item || (consumed != null && ContainsItem(consumed) == false) || (returned != null && (_items.ContainsKey(returned.InstanceId) || returned.Owner != null || returned.Location.IsPlaced)))
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            if (returnedStack != null && (returned == null || returned.DefinitionId == returnedStack.DefinitionId || returned.InstanceId == returnedStack.InstanceId || _items.ContainsKey(returnedStack.InstanceId) || returnedStack.Owner != null || returnedStack.Location.IsPlaced))
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

            var placementPreview = new InventoryPlacementPreview(item, consumed?.Amount == 1 ? consumed : null);

            if (container.CanPlace(updated, position, item.Location.Rotated, out ItemPlacement placement, out failure, placementPreview) == false)
            {
                return InventoryResult.Fail(failure);
            }

            foreach (InventoryContainer candidate in _containerList)
            {
                float delta = candidate == container ? updated.TotalWeight - item.TotalWeight : 0f;
                delta -= candidate == source ? consumed.UnitWeight : 0f;
                delta += candidate == destination ? returned.TotalWeight + (returnedStack?.TotalWeight ?? 0f) : 0f;

                if (delta > 0f && candidate.WeightAllows(delta) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }
            }

            ItemLocation previous = item.Location;
            ItemPlacement returnedPlacement = default;
            ItemPlacement stackPlacement = default;
            int returnedAmount = returned?.Amount ?? 0;
            int stackAmount = returnedStack?.Amount ?? 0;
            List<KeyValuePair<ItemInstance, int>> returnMerges = null;
            List<KeyValuePair<ItemInstance, int>> stackMerges = null;

            if (mergeReturned && returned?.IsStackable == true)
            {
                returnMerges = InventoryStackPlanning.Plan(returned, returnedAmount, destination, item, consumed);
                returnedAmount -= InventoryStackPlanning.Sum(returnMerges);
            }

            if (returnedStack?.IsStackable == true)
            {
                stackMerges = InventoryStackPlanning.Plan(returnedStack, stackAmount, destination, item, consumed);
                stackAmount -= InventoryStackPlanning.Sum(stackMerges);
            }

            if (returnedAmount > 0 || stackAmount > 0)
            {
                var returnPreview = new InventoryPlacementPreview(destination == container ? item : null, destination == source && consumed?.Amount == 1 ? consumed : null);

                if (destination == container)
                {
                    returnPreview = returnPreview.Reserve(updated, placement);
                }

                if (TryPlaceReturns(destination, returned, returnedAmount, returnedStack, stackAmount, returnPreview, out returnedPlacement, out stackPlacement) == false)
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

            _notifications.Add(new InventoryChange(InventoryChangeKind.ItemStateChanged, item, container.Id, previous, placement.SlotId));
            Flush();
            return InventoryResult.Ok(item, 1);
        }

        internal InventoryResult TryConsumeStacksAndUpdate(ItemInstance owner, ItemInstance draft, IReadOnlyList<KeyValuePair<ItemInstance, int>> consumed)
        {
            InventoryResult plan = PlanStateUpdate(owner, draft, out InventoryContainer ownerContainer, out ItemPlacement ownerPlacement);

            if (plan.Success == false)
            {
                return plan;
            }

            if (consumed == null || consumed.Count == 0)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidAmount);
            }

            HashSet<ItemInstance> unique = consumed.Count > 1 ? new HashSet<ItemInstance>() : null;
            float ownerWeightDelta = draft.TotalWeight - owner.TotalWeight;
            int amount = 0;

            for (int i = 0; i < consumed.Count; i++)
            {
                ItemInstance stack = consumed[i].Key;
                int count = consumed[i].Value;

                if (ContainsItem(stack) == false || stack == owner || GetContainer(stack.ContainerId) is not GridContainer)
                {
                    return InventoryResult.Fail(InventoryFailure.InvalidItem);
                }

                if (count <= 0 || count > stack.Amount || count > int.MaxValue - amount)
                {
                    return InventoryResult.Fail(InventoryFailure.InvalidAmount);
                }

                if (unique != null && unique.Add(stack) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.Duplicate);
                }

                if (stack.ContainerId == ownerContainer.Id)
                {
                    ownerWeightDelta -= stack.UnitWeight * count;
                }

                amount += count;
            }

            if (ownerWeightDelta > 0f && ownerContainer.WeightAllows(ownerWeightDelta) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            for (int i = 0; i < consumed.Count; i++)
            {
                ReduceOrRemove(consumed[i].Key, consumed[i].Value);
            }

            CommitStateUpdate(owner, draft, ownerContainer, ownerPlacement);
            Flush();
            return InventoryResult.Ok(owner, amount);
        }

        internal InventoryResult TryReturnRoundsAndUpdate(ItemInstance owner, ItemInstance draft, InventoryContainer destination, ItemDefinition ammoDefinition, int amount)
        {
            InventoryResult plan = PlanStateUpdate(owner, draft, out InventoryContainer ownerContainer, out ItemPlacement ownerPlacement);

            if (plan.Success == false)
            {
                return plan;
            }

            if (amount < 0)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidAmount);
            }

            float ownerWeightDelta = draft.TotalWeight - owner.TotalWeight;

            if ((ownerContainer != destination || amount == 0) && ownerWeightDelta > 0f && ownerContainer.WeightAllows(ownerWeightDelta) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            if (amount == 0)
            {
                CommitStateUpdate(owner, draft, ownerContainer, ownerPlacement);
                Flush();
                return InventoryResult.Ok(owner, 0);
            }

            if (destination is not GridContainer grid || GetContainer(grid.Id) != grid || ammoDefinition == null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemInstance box = CreateItem(ammoDefinition, amount);
            float weightDelta = box.UnitWeight * amount + (ownerContainer == grid ? ownerWeightDelta : 0f);

            if (grid.WeightAllows(weightDelta) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            List<KeyValuePair<ItemInstance, int>> merges = InventoryStackPlanning.Plan(box, amount, grid, owner);
            int remaining = amount - InventoryStackPlanning.Sum(merges);
            var additions = new List<KeyValuePair<ItemInstance, ItemPlacement>>();
            InventoryPlacementPreview preview = default;
            int lastRow = 0;

            if (grid == ownerContainer)
            {
                preview = new InventoryPlacementPreview(owner).Reserve(draft, ownerPlacement);
                lastRow = ownerPlacement.Area.yMax;
            }

            while (remaining > 0)
            {
                box.Amount = Mathf.Min(remaining, box.MaxStack);

                if (grid.TryFindSpace(box, true, out ItemPlacement placement, preview.WithGridReservations(additions, lastRow)) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }

                additions.Add(new KeyValuePair<ItemInstance, ItemPlacement>(box, placement));
                lastRow = Mathf.Max(lastRow, placement.Area.yMax);
                remaining -= box.Amount;

                if (remaining > 0)
                {
                    box = CreateItem(ammoDefinition, remaining);
                }
            }

            CommitStateUpdate(owner, draft, ownerContainer, ownerPlacement);
            ApplyMerges(merges);

            for (int i = 0; i < additions.Count; i++)
            {
                ItemInstance added = additions[i].Key;
                CommitReturn(added, added.Amount, null, grid, additions[i].Value);
            }

            Flush();
            return InventoryResult.Ok(owner, amount);
        }

        private InventoryResult PlanStateUpdate(ItemInstance owner, ItemInstance draft, out InventoryContainer container, out ItemPlacement placement)
        {
            container = null;
            placement = default;

            if (ContainsItem(owner) == false || draft == null || draft.Owner != null || draft.Location.IsPlaced || draft.InstanceId != owner.InstanceId || draft.Definition != owner.Definition || draft.Amount != owner.Amount)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            container = GetContainer(owner.ContainerId);
            draft.Resolver = Resolver;
            ItemLocation previous = owner.Location;
            var preview = new InventoryPlacementPreview(owner);

            if (container.CanPlace(draft, previous.Position, previous.Rotated, out placement, out InventoryFailure failure, preview) == false)
            {
                return InventoryResult.Fail(failure);
            }

            return InventoryResult.Ok(owner, owner.Amount);
        }

        private void CommitStateUpdate(ItemInstance owner, ItemInstance draft, InventoryContainer container, ItemPlacement placement)
        {
            ItemLocation previous = owner.Location;
            owner.CopyStateFrom(draft);

            if (container.GetItemArea(owner) != placement.Area || previous.Position != placement.Position || previous.Rotated != placement.Rotated || previous.SlotId != placement.SlotId)
            {
                container.Release(owner);
                container.CommitPlacement(owner, placement);
            }

            _notifications.Add(new InventoryChange(InventoryChangeKind.ItemStateChanged, owner, container.Id, previous, placement.SlotId));
        }

        private static bool TryPlaceReturns(InventoryContainer destination, ItemInstance first, int firstAmount, ItemInstance second, int secondAmount, InventoryPlacementPreview preview, out ItemPlacement firstPlacement, out ItemPlacement secondPlacement)
        {
            firstPlacement = default;
            secondPlacement = default;

            if (firstAmount > 0)
            {
                if (destination.TryFindSpaceForAmount(first, firstAmount, true, out firstPlacement, preview) == false)
                {
                    return false;
                }

                if (secondAmount > 0)
                {
                    preview = preview.Reserve(first, firstPlacement);
                }
            }

            return secondAmount == 0 || destination.TryFindSpaceForAmount(second, secondAmount, true, out secondPlacement, preview);
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
                _notifications.Add(new InventoryChange(InventoryChangeKind.ItemAdded, item, destination.Id, ItemLocation.None, placement.SlotId));
            }
        }

        public InventoryResult CanAdd(ItemInstance item, string containerId, Vector2Int position, bool rotated)
        {
            InventoryResult result = PlanAdd(item, containerId, position, rotated, out InventoryContainer target, out ItemPlacement placement);
            return PreviewOutfitTransition(result, item, target, placement, false);
        }

        private InventoryResult PlanAdd(ItemInstance item, string containerId, Vector2Int position, bool rotated, out InventoryContainer container, out ItemPlacement placement)
        {
            container = null;
            placement = default;

            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            if (_items.ContainsKey(item.InstanceId) || item.Owner != null || item.Location.IsPlaced)
            {
                return InventoryResult.Fail(InventoryFailure.AlreadyInInventory);
            }

            item.Resolver = Resolver;
            container = GetContainer(containerId);

            if (container == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (container.CanPlace(item, position, rotated, out placement, out InventoryFailure failure) == false)
            {
                return InventoryResult.Fail(failure);
            }

            return container.WeightAllows(item.TotalWeight) ? InventoryResult.Ok(item, item.Amount) : InventoryResult.Fail(InventoryFailure.NoSpace);
        }

        public InventoryResult TryAdd(ItemInstance item, string containerId, Vector2Int position, bool rotated)
        {
            InventoryResult check = PlanAdd(item, containerId, position, rotated, out InventoryContainer container, out ItemPlacement placement);

            if (check.Success == false)
            {
                return check;
            }

            InventoryResult outfitCheck = PlanOutfitTransition(item, container, placement, false, true, out List<OutfitSlotRelocation> relocations);
            if (outfitCheck.Success == false)
            {
                return outfitCheck;
            }

            _items.Add(item.InstanceId, item);
            OccupyInternal(item, container, placement);
            _notifications.Add(new InventoryChange(InventoryChangeKind.ItemAdded, item, container.Id, ItemLocation.None, placement.SlotId));
            CommitOutfitRelocations(relocations);
            InventoryResult result = InventoryResult.Ok(item, item.Amount);
            Flush();
            return result;
        }

        public InventoryResult TryAddAuto(ItemInstance item, string containerId, bool mergeStacks = true, bool allowRotation = true)
        {
            if (item == null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            if (_items.ContainsKey(item.InstanceId) || item.Owner != null || item.Location.IsPlaced)
            {
                return InventoryResult.Fail(InventoryFailure.AlreadyInInventory);
            }

            item.Resolver = Resolver;
            InventoryContainer container = GetContainer(containerId);

            if (container == null)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (container.WeightAllows(item.TotalWeight) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            List<KeyValuePair<ItemInstance, int>> merges = mergeStacks ? InventoryStackPlanning.Plan(item, item.Amount, container, null) : new List<KeyValuePair<ItemInstance, int>>();
            int merged = InventoryStackPlanning.Sum(merges);
            int remainder = item.Amount - merged;
            ItemPlacement placement = default;

            if (remainder > 0)
            {
                if (container.TryFindSpaceForAmount(item, remainder, allowRotation, out placement) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }
            }

            InventoryResult outfitCheck = PlanOutfitTransition(item, container, placement, false, true, out List<OutfitSlotRelocation> relocations);
            if (outfitCheck.Success == false)
            {
                return outfitCheck;
            }

            ItemInstance resultItem = ApplyMerges(merges);

            if (remainder > 0)
            {
                item.Amount = remainder;
                _items.Add(item.InstanceId, item);
                OccupyInternal(item, container, placement);
                _notifications.Add(new InventoryChange(InventoryChangeKind.ItemAdded, item, container.Id, ItemLocation.None, placement.SlotId));
                resultItem = item;
            }

            CommitOutfitRelocations(relocations);
            Flush();
            return InventoryResult.Ok(resultItem, merged + remainder);
        }

        public InventoryResult CanMove(string instanceId, string containerId, Vector2Int position, bool rotated, bool allowMerge = true)
        {
            InventoryResult result = PlanMove(instanceId, containerId, position, rotated, allowMerge, out ItemInstance item, out InventoryContainer target, out ItemPlacement placement, out ItemInstance mergeTarget);
            return mergeTarget == null ? PreviewOutfitTransition(result, item, target, placement, true) : result;
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
                return CommitMerge(item, plan);
            }

            InventoryResult outfitCheck = PlanOutfitTransition(item, target, placement, true, true, out List<OutfitSlotRelocation> relocations);
            if (outfitCheck.Success == false)
            {
                return outfitCheck;
            }

            MoveInternal(item, GetContainer(item.ContainerId), target, placement);
            CommitOutfitRelocations(relocations);
            Flush();
            return plan;
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

        public InventoryResult CanMerge(string sourceId, string targetId, int amount = int.MaxValue)
        {
            return PlanMerge(sourceId, targetId, amount, out _);
        }

        public InventoryResult TryMerge(string sourceId, string targetId, int amount = int.MaxValue)
        {
            InventoryResult plan = PlanMerge(sourceId, targetId, amount, out ItemInstance source);

            if (plan.Success == false)
            {
                return plan;
            }

            return CommitMerge(source, plan);
        }

        private InventoryResult CommitMerge(ItemInstance source, InventoryResult plan)
        {
            ItemInstance target = plan.Item;
            target.Amount += plan.Amount;
            _notifications.Add(new InventoryChange(InventoryChangeKind.AmountChanged, target, target.ContainerId, target.Location));
            ReduceOrRemove(source, plan.Amount);
            Flush();
            return plan;
        }

        private InventoryResult PlanMerge(string sourceId, string targetId, int amount, out ItemInstance source)
        {
            source = GetItem(sourceId);
            ItemInstance target = GetItem(targetId);
            return InventoryStackPlanning.PlanMerge(source, GetContainer(source?.ContainerId), target, GetContainer(target?.ContainerId), amount);
        }

        public InventoryResult CanSplit(string instanceId, int amount, string containerId, Vector2Int position, bool rotated)
        {
            InventoryResult result = PlanSplit(instanceId, amount, containerId, position, rotated, out ItemInstance item, out InventoryContainer target, out ItemPlacement placement, out ItemInstance mergeTarget, out _);
            return mergeTarget == null ? PreviewOutfitTransition(result, item, target, placement, false) : result;
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
                _notifications.Add(new InventoryChange(InventoryChangeKind.AmountChanged, mergeTarget, mergeTarget.ContainerId, mergeTarget.Location));
                ReduceOrRemove(item, moved);
                Flush();
                return InventoryResult.Ok(mergeTarget, moved);
            }

            ItemInstance piece = InventoryStackPlanning.CreatePiece(item, moved);
            InventoryResult outfitCheck = PlanOutfitTransition(piece, target, placement, false, true, out List<OutfitSlotRelocation> relocations);
            if (outfitCheck.Success == false)
            {
                return outfitCheck;
            }

            item.Amount -= moved;
            _notifications.Add(new InventoryChange(InventoryChangeKind.AmountChanged, item, item.ContainerId, item.Location));
            _items.Add(piece.InstanceId, piece);
            OccupyInternal(piece, target, placement);
            _notifications.Add(new InventoryChange(InventoryChangeKind.ItemAdded, piece, target.Id, ItemLocation.None, placement.SlotId));
            CommitOutfitRelocations(relocations);
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

            ItemInstance probe = InventoryStackPlanning.CreatePiece(item, amount);

            if (target.TryFindSpace(probe, true, out ItemPlacement placement) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            return TrySplit(instanceId, amount, target.Id, placement.Position, placement.Rotated);
        }

        internal InventoryResult CanTransfer(string instanceId, string containerId, out ItemPlacement placement, out ItemInstance firstMerge, int amount = int.MaxValue, bool mergeStacks = true)
        {
            InventoryResult result = PlanTransfer(instanceId, containerId, amount, mergeStacks, out InventoryTransferPlan plan);
            placement = plan.Placement;
            firstMerge = plan.Merges != null && plan.Merges.Count > 0 ? plan.Merges[0].Key : null;
            return plan.Remainder > 0 ? PreviewOutfitTransition(result, plan.Carrier, plan.Target, placement, plan.Whole) : result;
        }

        public InventoryResult TryTransfer(string instanceId, string containerId, int amount = int.MaxValue, bool mergeStacks = true)
        {
            InventoryResult result = PlanTransfer(instanceId, containerId, amount, mergeStacks, out InventoryTransferPlan plan);

            if (result.Success == false)
            {
                return result;
            }

            InventoryResult outfitCheck = PlanOutfitTransition(plan.Carrier, plan.Target, plan.Placement, plan.Whole, true, out List<OutfitSlotRelocation> relocations);
            if (outfitCheck.Success == false)
            {
                return outfitCheck;
            }

            ItemInstance item = plan.Item;
            ItemInstance resultItem = plan.Merges != null ? ApplyMerges(plan.Merges) : null;

            if (plan.Remainder > 0)
            {
                if (plan.Whole)
                {
                    item.Amount = plan.Remainder;
                    MoveInternal(item, plan.Source, plan.Target, plan.Placement);
                    resultItem = item;
                }
                else
                {
                    item.Amount -= plan.Moving;
                    _notifications.Add(new InventoryChange(InventoryChangeKind.AmountChanged, item, item.ContainerId, item.Location));
                    ItemInstance carrier = plan.Carrier;
                    _items.Add(carrier.InstanceId, carrier);
                    OccupyInternal(carrier, plan.Target, plan.Placement);
                    _notifications.Add(new InventoryChange(InventoryChangeKind.ItemAdded, carrier, plan.Target.Id, ItemLocation.None, plan.Placement.SlotId));
                    resultItem = carrier;
                }
            }
            else
            {
                ReduceOrRemove(item, plan.Moving);
            }

            CommitOutfitRelocations(relocations);
            Flush();
            return InventoryResult.Ok(resultItem, plan.Moving);
        }

        private InventoryResult PlanTransfer(string instanceId, string containerId, int amount, bool mergeStacks, out InventoryTransferPlan plan)
        {
            ItemInstance item = GetItem(instanceId);
            return InventoryStackPlanning.PlanTransfer(item, GetContainer(item?.ContainerId), GetContainer(containerId), amount, mergeStacks, out plan);
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

            ItemInstance piece = InventoryStackPlanning.CreatePiece(item, amount);
            item.Amount -= amount;
            _notifications.Add(new InventoryChange(InventoryChangeKind.AmountChanged, item, item.ContainerId, item.Location));
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

            foreach (SlotDefinition slot in InventoryEquipmentPlanning.EnumerateEquipCandidates(item, equipment))
            {
                InventoryResult result = TryMove(item.InstanceId, equipment.Id, slot.Bounds.position, false, false);

                if (result.Success)
                {
                    return result;
                }

                lastFailure = result.Failure;
            }

            return InventoryResult.Fail(lastFailure);
        }

        public bool HasEquipmentSlotFor(ItemInstance item, string equipmentContainerId)
        {
            if (item == null || TryGetContainer(equipmentContainerId, out SlottedContainer equipment) == false)
            {
                return false;
            }

            IReadOnlyList<SlotDefinition> slots = equipment.Slots;

            for (int i = 0; i < slots.Count; i++)
            {
                SlotDefinition slot = slots[i];

                if (slot.Mode == SlotMode.Single && slot.Rule.AcceptsDefinition(item.Definition))
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanEquipItem(ItemInstance item, string equipmentContainerId, string parkingContainerId)
        {
            if (ContainsItem(item) == false || TryGetContainer(equipmentContainerId, out SlottedContainer equipment) == false)
            {
                return false;
            }

            IReadOnlyList<SlotDefinition> slots = equipment.Slots;

            for (int i = 0; i < slots.Count; i++)
            {
                SlotDefinition slot = slots[i];

                if (slot.Mode == SlotMode.Single && CanMove(item.InstanceId, equipment.Id, slot.Bounds.position, false, false).Success)
                {
                    return true;
                }
            }

            InventoryContainer parking = GetContainer(parkingContainerId);
            SlotDefinition candidate = InventoryEquipmentPlanning.FindSwapCandidate(item, equipment, parking);
            return candidate != null && CanSwapIntoSlot(item, equipment.Id, candidate.Bounds.position, parkingContainerId);
        }

        public InventoryResult TryEquipItem(ItemInstance item, string equipmentContainerId, SlotId slotId, string parkingContainerId)
        {
            if (ContainsItem(item) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (TryGetContainer(equipmentContainerId, out SlottedContainer equipment) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            InventoryContainer parking = GetContainer(parkingContainerId);
            SlotDefinition slot;
            InventoryResult result;

            if (slotId == SlotId.None)
            {
                result = TryEquip(item.InstanceId, equipment.Id);

                if (result.Success)
                {
                    return result;
                }

                slot = InventoryEquipmentPlanning.FindSwapCandidate(item, equipment, parking);
            }
            else
            {
                slot = equipment.GetSlot(slotId);

                if (slot == null)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSlot);
                }

                result = TryMove(item.InstanceId, equipment.Id, slot.Bounds.position, false, false);

                if (result.Success || InventoryEquipmentPlanning.GetSwappableOccupant(item, equipment, slot, parking) == null)
                {
                    return result;
                }
            }

            return slot != null ? CommitSwap(item, equipment, slot, parking) : result;
        }

        public InventoryResult TryUnequipItem(ItemInstance item, string targetContainerId, Vector2Int origin, bool rotated, bool autoPlace)
        {
            if (ContainsItem(item) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (autoPlace == false)
            {
                InventoryResult moved = TryMove(item.InstanceId, targetContainerId, origin, rotated);

                if (moved.Success)
                {
                    return moved;
                }
            }

            return TryTransfer(item.InstanceId, targetContainerId);
        }

        public bool CanSwapIntoSlot(ItemInstance item, string targetId, Vector2Int cell, string parkingContainerId)
        {
            return ContainsItem(item) && TryGetContainer(targetId, out SlottedContainer target) && InventoryEquipmentPlanning.PlanSwap(item, GetContainer(item.ContainerId), target, target.GetSlotAt(cell), GetContainer(parkingContainerId), _outfitRules?.Equipment, out EquipmentSwap swap).Success && PlanOutfitTransition(item, target, swap.IncomingPlacement, true, false, out _, swap).Success;
        }

        public InventoryResult TrySwapIntoSlot(ItemInstance item, string targetId, Vector2Int cell, string parkingContainerId)
        {
            if (ContainsItem(item) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            if (TryGetContainer(targetId, out SlottedContainer target) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            return CommitSwap(item, target, target.GetSlotAt(cell), GetContainer(parkingContainerId));
        }

        public InventoryResult TrySetSlotLocked(string containerId, SlotId slotId, bool locked)
        {
            if (TryGetContainer(containerId, out SlottedContainer container) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ContainerNotFound);
            }

            if (container.GetSlot(slotId) == null)
            {
                return InventoryResult.Fail(InventoryFailure.NoSlot);
            }

            if (locked == false && container == _outfitRules?.Equipment && OutfitSlotRules.ShouldLock(container.GetSlotOccupant(SlotId.Outfit), slotId))
            {
                return InventoryResult.Fail(InventoryFailure.SlotUnavailable);
            }

            if (container.CanSetSlotLocked(slotId, locked) == false)
            {
                return InventoryResult.Fail(InventoryFailure.Occupied);
            }

            if (container.SetSlotLocked(slotId, locked))
            {
                _notifications.Add(new InventoryChange(InventoryChangeKind.SlotStateChanged, null, containerId, ItemLocation.None, slotId));
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

            InventoryResult plan = InventoryArrangement.Plan(container, order, groups, allowRotation, out ItemPlacement[] placements);
            if (plan.Success == false)
            {
                return plan;
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
                    _notifications.Add(new InventoryChange(InventoryChangeKind.ItemMoved, order[i], container.Id, previous[i]));
                }
            }

            InventoryResult result = InventoryResult.Ok(null, order.Count);
            Flush();
            return result;
        }

        public void Clear()
        {
            foreach (InventoryContainer container in _containerList)
            {
                var items = new List<ItemInstance>(container.Items);

                foreach (ItemInstance item in items)
                {
                    ReleaseInternal(item, container);
                    item.Owner = null;
                }
            }

            _items.Clear();
            _notifications.Add(new InventoryChange(InventoryChangeKind.Cleared, null, string.Empty, ItemLocation.None));
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

        private InventoryResult PlanMove(string instanceId, string containerId, Vector2Int position, bool rotated, bool allowMerge, out ItemInstance item, out InventoryContainer target, out ItemPlacement placement, out ItemInstance mergeTarget)
        {
            item = GetItem(instanceId);
            target = GetContainer(containerId);
            return _placementPlanner.Move(item, GetContainer(item?.ContainerId), target, position, rotated, allowMerge, out placement, out mergeTarget);
        }

        private InventoryResult PlanSplit(string instanceId, int amount, string containerId, Vector2Int position, bool rotated, out ItemInstance item, out InventoryContainer target, out ItemPlacement placement, out ItemInstance mergeTarget, out int moved)
        {
            item = GetItem(instanceId);
            target = GetContainer(containerId);
            return _placementPlanner.Split(item, GetContainer(item?.ContainerId), target, amount, position, rotated, out placement, out mergeTarget, out moved);
        }

        private ItemInstance ApplyMerges(List<KeyValuePair<ItemInstance, int>> merges)
        {
            ItemInstance last = null;

            foreach (KeyValuePair<ItemInstance, int> merge in merges)
            {
                merge.Key.Amount += merge.Value;
                _notifications.Add(new InventoryChange(InventoryChangeKind.AmountChanged, merge.Key, merge.Key.ContainerId, merge.Key.Location));
                last = merge.Key;
            }

            return last;
        }

        private void ReduceOrRemove(ItemInstance item, int amount)
        {
            if (amount < item.Amount)
            {
                item.Amount -= amount;
                _notifications.Add(new InventoryChange(InventoryChangeKind.AmountChanged, item, item.ContainerId, item.Location));
                return;
            }

            InventoryContainer container = GetContainer(item.ContainerId);
            ItemLocation previous = item.Location;
            ReleaseInternal(item, container);
            _items.Remove(item.InstanceId);
            item.Owner = null;
            _notifications.Add(new InventoryChange(InventoryChangeKind.ItemRemoved, item, previous.ContainerId, previous, previous.SlotId));
        }

        private InventoryResult CommitSwap(ItemInstance item, SlottedContainer target, SlotDefinition slot, InventoryContainer parking)
        {
            InventoryResult result = InventoryEquipmentPlanning.PlanSwap(item, GetContainer(item.ContainerId), target, slot, parking, _outfitRules?.Equipment, out EquipmentSwap swap);

            if (result.Success == false)
            {
                return result;
            }

            InventoryResult outfitCheck = PlanOutfitTransition(item, target, swap.IncomingPlacement, true, true, out List<OutfitSlotRelocation> relocations, swap);
            if (outfitCheck.Success == false)
            {
                return outfitCheck;
            }

            InventoryContainer source = GetContainer(item.ContainerId);
            ItemInstance displaced = swap.DisplacedItem;

            if (swap.DisplacedDestination == target)
            {
                ItemLocation displacedFrom = displaced.Location;
                ReleaseInternal(displaced, target);
                MoveInternal(item, source, target, swap.IncomingPlacement);
                OccupyInternal(displaced, target, swap.DisplacedPlacement);
                _notifications.Add(new InventoryChange(InventoryChangeKind.ItemMoved, displaced, target.Id, displacedFrom, swap.DisplacedPlacement.SlotId));
            }
            else
            {
                MoveInternal(displaced, target, swap.DisplacedDestination, swap.DisplacedPlacement);
                MoveInternal(item, source, target, swap.IncomingPlacement);
            }

            CommitOutfitRelocations(relocations);
            Flush();
            return result;
        }

        private InventoryResult PreviewOutfitTransition(InventoryResult movement, ItemInstance incoming, InventoryContainer target, ItemPlacement placement, bool releaseSource)
        {
            if (movement.Success == false)
            {
                return movement;
            }

            InventoryResult check = PlanOutfitTransition(incoming, target, placement, releaseSource, false, out _);
            return check.Success ? movement : check;
        }

        private InventoryResult PlanOutfitTransition(ItemInstance incoming, InventoryContainer target, ItemPlacement placement, bool releaseSource, bool prepareDrops, out List<OutfitSlotRelocation> relocations, EquipmentSwap swap = default)
        {
            relocations = null;
            return _outfitRules == null ? InventoryResult.Ok(incoming, 0) : _outfitRules.Plan(incoming, target, placement, releaseSource, prepareDrops, _outfitRules.Backpack.Model == this, out relocations, swap);
        }

        private void CommitOutfitRelocations(List<OutfitSlotRelocation> relocations)
        {
            if (relocations == null)
            {
                return;
            }

            SlottedContainer equipment = _outfitRules.Equipment;
            GridContainer backpack = _outfitRules.Backpack;
            foreach (OutfitSlotRelocation relocation in relocations)
            {
                ItemInstance item = relocation.Item;
                if (relocation.Merges == null || relocation.Remainder == 0)
                {
                    ReduceOrRemove(item, item.Amount);
                }
                else
                {
                    item.Amount = relocation.Remainder;
                    MoveInternal(item, equipment, backpack, relocation.Placement);
                }

                if (relocation.Merges != null)
                {
                    ApplyMerges(relocation.Merges);
                }
            }

            UpdateOutfitLocks();
            foreach (OutfitSlotRelocation relocation in relocations)
            {
                if (relocation.Merges == null)
                {
                    relocation.Drop.Complete(relocation.Item);
                    _notifications.AddDrop(_outfitRules, relocation.Item, relocation.Item.Amount);
                }
            }
        }

        private void UpdateOutfitLocks()
        {
            if (_outfitLocksDirty == false)
            {
                return;
            }

            _outfitLocksDirty = false;
            SlottedContainer equipment = _outfitRules.Equipment;
            if (equipment.Model != this)
            {
                return;
            }

            ItemInstance outfit = equipment.GetSlotOccupant(SlotId.Outfit);
            UpdateOutfitLock(equipment, SlotId.Head, OutfitSlotRules.ShouldLock(outfit, SlotId.Head));
            UpdateOutfitLock(equipment, SlotId.Backpack, OutfitSlotRules.ShouldLock(outfit, SlotId.Backpack));
        }

        private void UpdateOutfitLock(SlottedContainer equipment, SlotId slotId, bool locked)
        {
            if (equipment.SetSlotLocked(slotId, locked))
            {
                _notifications.Add(new InventoryChange(InventoryChangeKind.SlotStateChanged, null, equipment.Id, ItemLocation.None, slotId));
            }
        }

        private void MoveInternal(ItemInstance item, InventoryContainer source, InventoryContainer target, ItemPlacement placement)
        {
            ItemLocation previous = item.Location;
            ReleaseInternal(item, source);
            OccupyInternal(item, target, placement);
            _notifications.Add(new InventoryChange(InventoryChangeKind.ItemMoved, item, target.Id, previous, placement.SlotId));
        }

        private void OccupyInternal(ItemInstance item, InventoryContainer container, ItemPlacement placement)
        {
            item.Owner = this;
            item.Resolver = Resolver;
            container.CommitPlacement(item, placement);

            if (container is SlottedContainer slotted)
            {
                SlotDefinition slot = slotted.GetSlot(placement.SlotId);

                if (slotted.IsEquipment && slot != null && slot.Mode == SlotMode.Single)
                {
                    _notifications.Add(new EquipEvent(true, item, slotted, slot));
                }

                _notifications.Add(new InventoryChange(InventoryChangeKind.SlotStateChanged, null, slotted.Id, ItemLocation.None, placement.SlotId));
                _outfitLocksDirty |= slotted == _outfitRules?.Equipment && placement.SlotId == SlotId.Outfit;
            }
        }

        private void ReleaseInternal(ItemInstance item, InventoryContainer container)
        {
            if (container == null)
            {
                item.Location = ItemLocation.None;
                return;
            }

            SlotId slotId = item.Location.SlotId;
            container.Release(item);

            if (container is SlottedContainer slotted)
            {
                SlotDefinition slot = slotted.GetSlot(slotId);

                if (slotted.IsEquipment && slot != null && slot.Mode == SlotMode.Single)
                {
                    _notifications.Add(new EquipEvent(false, item, slotted, slot));
                }

                _notifications.Add(new InventoryChange(InventoryChangeKind.SlotStateChanged, null, slotted.Id, ItemLocation.None, slotId));
                _outfitLocksDirty |= slotted == _outfitRules?.Equipment && slotId == SlotId.Outfit;
            }
        }

        private void Flush()
        {
            UpdateOutfitLocks();
            _notifications.PublishCommittedBatch();
        }
    }
}
