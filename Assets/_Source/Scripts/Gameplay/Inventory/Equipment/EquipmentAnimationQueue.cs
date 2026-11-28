using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Weapons;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    public sealed class EquipmentAnimationQueue : IInventoryEquipmentPresentation, IDisposable
    {
        public const float UNEQUIP_FADE_DELAY = 0.4f;
        public const float EQUIP_FADE_START = 0.35f;
        public const float EQUIP_FADE_STOP = 0.25f;

        private const int MAX_STEPS_PER_TICK = 64;
        private readonly EquipmentInventoryCommands _commands;
        private readonly InventoryModel _model;
        private readonly string _equipmentContainerId;
        private readonly string _primaryContainerId;
        private readonly StalkerPlayerController _player;
        private readonly FirstPersonWeaponController _weapons;
        private readonly FirstPersonHudAnimator _animator;
        private readonly EquipmentHudPlayback _playback;
        private readonly Queue<EquipmentAnimationRequest> _requests = new Queue<EquipmentAnimationRequest>();
        private readonly Queue<EquipmentAnimationStep> _steps = new Queue<EquipmentAnimationStep>();
        private readonly HashSet<ItemInstance> _inUse = new HashSet<ItemInstance>();
        private readonly List<string> _outfitMemory = new List<string>();
        private string _community = FirstPersonEquipmentAnimationLibrary.DEFAULT_COMMUNITY;

        internal EquipmentAnimationQueue(EquipmentInventoryCommands commands, InventoryModel model, string equipmentContainerId, string primaryContainerId, StalkerPlayerController player, FirstPersonWeaponController weapons)
        {
            _commands = commands;
            _model = model;
            _equipmentContainerId = equipmentContainerId;
            _primaryContainerId = primaryContainerId;
            _player = player;
            _weapons = weapons;
            _animator = player != null ? player.HudAnimator : null;
            _playback = new EquipmentHudPlayback(player, _animator);
            if (_animator != null)
            {
                _animator.Stopped += OnAnimationStopped;
            }

        }

        internal event Action<ItemInstance> ItemUseChanged;
        internal event Action<EquipmentAnimationRequestKind, ItemInstance, InventoryResult, bool> OperationCompleted;

        event Action<ItemInstance> IInventoryEquipmentPresentation.ItemUseChanged
        {
            add => ItemUseChanged += value;
            remove => ItemUseChanged -= value;
        }

        event Action<EquipmentAnimationRequestKind, ItemInstance, InventoryResult, bool> IInventoryEquipmentPresentation.OperationCompleted
        {
            add => OperationCompleted += value;
            remove => OperationCompleted -= value;
        }

        public bool IsBusy => _steps.Count > 0 || _requests.Count > 0;
        public int PendingRequests => _requests.Count;
        public IReadOnlyCollection<ItemInstance> ItemsInUse => _inUse;

        public string Community
        {
            get => _community;
            set => _community = string.IsNullOrEmpty(value) ? FirstPersonEquipmentAnimationLibrary.DEFAULT_COMMUNITY : value;
        }

        private FirstPersonEquipmentAnimationLibrary Library => _animator != null ? _animator.Library : null;

        public bool IsInUse(ItemInstance item) => item != null && _inUse.Contains(item);

        public bool HasPatchMotion(ItemInstance item) => GetPatchMotion(item)?.Hands != null;

        public bool TryQueuePatch(ItemInstance item)
        {
            if (IsInUse(item) || HasPatchMotion(item) == false)
            {
                return false;
            }

            InventoryResult placement = _commands.RemovePatch(item, false);
            if (placement.Success == false)
            {
                OperationCompleted?.Invoke(EquipmentAnimationRequestKind.Patch, item, placement, false);
                return false;
            }

            Enqueue(new EquipmentAnimationRequest { Kind = EquipmentAnimationRequestKind.Patch, Item = item });
            return true;
        }

        public bool HandleEquip(ItemInstance item, SlotDefinition slot)
        {
            if (IsInUse(item))
            {
                return true;
            }

            if (IsAnimated(item) == false || IsEquipped(item))
            {
                return false;
            }

            if (_commands.CanEquip(item) == false)
            {
                return false;
            }

            Enqueue(new EquipmentAnimationRequest { Kind = EquipmentAnimationRequestKind.Equip, Item = item, SlotId = slot?.Id ?? SlotId.None });
            return true;
        }

        public bool HandleUnequip(ItemInstance item, InventoryUnequipTarget target)
        {
            if (IsInUse(item))
            {
                return true;
            }

            if (IsAnimated(item) == false || IsEquipped(item) == false)
            {
                return false;
            }

            Enqueue(new EquipmentAnimationRequest { Kind = EquipmentAnimationRequestKind.Unequip, Item = item, Target = target });
            return true;
        }

        public bool HandleDrop(ItemInstance item)
        {
            if (IsInUse(item))
            {
                return true;
            }

            if (IsAnimated(item) == false || IsEquipped(item) == false)
            {
                return false;
            }

            Enqueue(new EquipmentAnimationRequest { Kind = EquipmentAnimationRequestKind.Drop, Item = item });
            return true;
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < MAX_STEPS_PER_TICK; i++)
            {
                if (_steps.Count == 0)
                {
                    if (_requests.Count == 0)
                    {
                        return;
                    }

                    Expand(_requests.Dequeue());
                    continue;
                }

                if (Advance(_steps.Peek(), deltaTime) == false)
                {
                    return;
                }

                _steps.Dequeue();
                deltaTime = 0.0f;
            }
        }

        public void Dispose()
        {
            if (_animator != null)
            {
                _animator.Stopped -= OnAnimationStopped;
            }

            _requests.Clear();
            _steps.Clear();
            _inUse.Clear();
        }

        private FirstPersonHudMotion GetPatchMotion(ItemInstance item)
        {
            if (item == null || _model.ContainsItem(item) == false || item.Definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY) == false || EquipmentInventoryCommands.IsPatchRemoved(item))
            {
                return null;
            }

            bool equipped = IsEquipped(item);
            if (equipped == false && item.ContainerId != _primaryContainerId)
            {
                return null;
            }

            return _commands.TryFindPatch(item, out string community, out _) ? Library?.FindPatch(community, false, equipped) : null;
        }

        private bool IsAnimated(ItemInstance item)
        {
            if (IsWeaponInHands(item))
            {
                return true;
            }

            FirstPersonEquipmentAnimationLibrary library = Library;
            if (item == null || library == null)
            {
                return false;
            }

            ItemDefinition definition = item.Definition;
            if (definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY))
            {
                return library.OutfitEnabled;
            }

            if (definition.IsCategory(AnomalyEquipmentProtection.HELMET_CATEGORY))
            {
                return library.HeadgearEnabled;
            }

            return definition.IsCategory(AnomalyEquipmentProtection.BACKPACK_CATEGORY) && library.BackpackEnabled;
        }

        private bool IsEquipped(ItemInstance item) => item != null && item.ContainerId == _equipmentContainerId;

        private bool IsWeaponInHands(ItemInstance item)
        {
            if (item == null || _weapons == null || _weapons.State == FirstPersonWeaponState.Hidden || _weapons.ActiveSlot < 0)
            {
                return false;
            }

            SlottedContainer equipment = _model.GetContainer(_equipmentContainerId) as SlottedContainer;
            SlotId slotId = FirstPersonWeaponBinder.GetEquipmentSlotId(_weapons.ActiveSlot);
            return equipment != null && equipment.GetSlotOccupant(slotId) == item;
        }

        private void Enqueue(EquipmentAnimationRequest request)
        {
            SetInUse(request.Item, true);
            _requests.Enqueue(request);
            Tick(0.0f);
        }

        private void SetInUse(ItemInstance item, bool value)
        {
            bool changed = value ? _inUse.Add(item) : _inUse.Remove(item);
            if (changed)
            {
                ItemUseChanged?.Invoke(item);
            }
        }

        private void Expand(EquipmentAnimationRequest request)
        {
            ItemInstance item = request.Item;
            if (request.Kind == EquipmentAnimationRequestKind.Patch)
            {
                FirstPersonHudMotion motion = GetPatchMotion(item);
                if (motion?.Hands != null)
                {
                    _steps.Enqueue(new EquipmentAnimationStep { Kind = EquipmentAnimationStepKind.Animation, Motion = motion, Speed = 1.0f, Item = item });
                }
                else
                {
                    SetInUse(item, false);
                }

                return;
            }

            bool equipped = _model.ContainsItem(item) && IsEquipped(item);
            bool valid = _model.ContainsItem(item) && (request.Kind == EquipmentAnimationRequestKind.Equip ? equipped == false : equipped);
            if (valid == false)
            {
                SetInUse(item, false);
                return;
            }

            ItemDefinition definition = item.Definition;
            if (definition.GetComponent<WeaponHudComponent>() != null)
            {
                _steps.Enqueue(new EquipmentAnimationStep { Kind = EquipmentAnimationStepKind.WeaponHolster, Item = item });
                AddMove(request);
            }
            else if (definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY))
            {
                ExpandOutfit(request);
            }
            else if (definition.IsCategory(AnomalyEquipmentProtection.HELMET_CATEGORY))
            {
                ExpandHeadgear(request);
            }
            else
            {
                ExpandBackpack(request);
            }
        }

        private void ExpandOutfit(EquipmentAnimationRequest request)
        {
            _steps.Enqueue(new EquipmentAnimationStep
            {
                Kind = EquipmentAnimationStepKind.Move,
                Move = () =>
                {
                    if (ApplyRequest(request))
                    {
                        string section = request.Kind == EquipmentAnimationRequestKind.Equip ? SelectOutfitSection(GetSection(request.Item)) : FirstPersonEquipmentAnimationLibrary.OUTFIT_FAST_SECTION;
                        AddAnimation(Library.Find(section, FirstPersonEquipmentAnimationLibrary.ANM_EQUIP), 1.0f, false);
                    }
                }
            });
        }

        private void ExpandHeadgear(EquipmentAnimationRequest request)
        {
            FirstPersonEquipmentAnimationLibrary library = Library;
            float speed = library.HeadgearSpeed;
            if (request.Kind != EquipmentAnimationRequestKind.Equip)
            {
                AddHeadgearRemoval(request.Item, () => ApplyRequest(request), speed);
                return;
            }

            ItemInstance old = EquipmentItemStats.FindEquipped(_model.GetContainer(_equipmentContainerId), AnomalyEquipmentProtection.HELMET_CATEGORY);
            if (old != null && old != request.Item)
            {
                AddHeadgearRemoval(old, () => Unequip(old, InventoryUnequipTarget.Auto(_primaryContainerId), false), speed);
            }

            string section = GetSection(request.Item);
            if (library.IsHeadgearBlacklisted(section) == false)
            {
                bool fade = library.UsesFade(section + FirstPersonEquipmentAnimationLibrary.HEADGEAR_SUFFIX);
                AddAnimation(library.Find(library.GetHeadgearSection(section), FirstPersonEquipmentAnimationLibrary.ANM_EQUIP), speed, fade);
            }

            AddMove(request);
        }

        private void AddHeadgearRemoval(ItemInstance item, Action move, float speed)
        {
            FirstPersonEquipmentAnimationLibrary library = Library;
            string section = GetSection(item);
            if (library.UsesFade(section + FirstPersonEquipmentAnimationLibrary.HEADGEAR_SUFFIX))
            {
                _steps.Enqueue(new EquipmentAnimationStep { Kind = EquipmentAnimationStepKind.Fade });
            }

            if (library.IsHeadgearBlacklisted(section) == false)
            {
                AddAnimation(library.Find(library.GetHeadgearSection(section), FirstPersonEquipmentAnimationLibrary.ANM_UNEQUIP), speed, false);
            }

            _steps.Enqueue(new EquipmentAnimationStep { Kind = EquipmentAnimationStepKind.Move, Move = move });
        }

        private void ExpandBackpack(EquipmentAnimationRequest request)
        {
            if (request.Kind == EquipmentAnimationRequestKind.Equip && _model.CanEquipItem(request.Item, _equipmentContainerId, _primaryContainerId) == false)
            {
                SetInUse(request.Item, false);
                return;
            }

            FirstPersonEquipmentAnimationLibrary library = Library;
            FirstPersonHudMotion equip = library.Find(library.GetBackpackSection(FirstPersonEquipmentAnimationLibrary.BACKPACK_EQUIP_PREFIX, _community), FirstPersonEquipmentAnimationLibrary.ANM_USE);
            FirstPersonHudMotion unequip = library.Find(library.GetBackpackSection(FirstPersonEquipmentAnimationLibrary.BACKPACK_UNEQUIP_PREFIX, _community), FirstPersonEquipmentAnimationLibrary.ANM_USE);
            if (request.Kind != EquipmentAnimationRequestKind.Equip)
            {
                AddAnimation(unequip, 1.0f, false);
                AddMove(request);
                return;
            }

            ItemInstance old = EquipmentItemStats.FindEquipped(_model.GetContainer(_equipmentContainerId), AnomalyEquipmentProtection.BACKPACK_CATEGORY);
            if (old != null && old != request.Item)
            {
                AddAnimation(unequip, 1.0f, false);
                _steps.Enqueue(new EquipmentAnimationStep { Kind = EquipmentAnimationStepKind.Move, Move = () => Unequip(old, InventoryUnequipTarget.Auto(_primaryContainerId), false) });
            }

            AddAnimation(equip, 1.0f, false);
            AddMove(request);
        }

        private string SelectOutfitSection(string itemSection)
        {
            FirstPersonEquipmentAnimationLibrary library = Library;
            if (library.AlwaysShort)
            {
                return FirstPersonEquipmentAnimationLibrary.OUTFIT_FAST_SECTION;
            }

            if (library.OutfitMemory == false || string.IsNullOrEmpty(itemSection))
            {
                return FirstPersonEquipmentAnimationLibrary.OUTFIT_SECTION;
            }

            if (_outfitMemory.Contains(itemSection))
            {
                return FirstPersonEquipmentAnimationLibrary.OUTFIT_FAST_SECTION;
            }

            _outfitMemory.Add(itemSection);
            if (library.OutfitMemorySize > 0 && _outfitMemory.Count > library.OutfitMemorySize)
            {
                _outfitMemory.RemoveAt(0);
            }

            return FirstPersonEquipmentAnimationLibrary.OUTFIT_SECTION;
        }

        private void AddMove(EquipmentAnimationRequest request)
        {
            _steps.Enqueue(new EquipmentAnimationStep { Kind = EquipmentAnimationStepKind.Move, Move = () => ApplyRequest(request) });
        }

        private void AddAnimation(FirstPersonHudMotion motion, float speed, bool fadeAtEnd)
        {
            if (motion != null && motion.Hands != null)
            {
                _steps.Enqueue(new EquipmentAnimationStep { Kind = EquipmentAnimationStepKind.Animation, Motion = motion, Speed = speed, FadeAtEnd = fadeAtEnd });
            }
        }

        private bool ApplyRequest(EquipmentAnimationRequest request)
        {
            ItemInstance item = request.Item;
            SetInUse(item, false);
            if (_model.ContainsItem(item) == false)
            {
                return false;
            }

            switch (request.Kind)
            {
                case EquipmentAnimationRequestKind.Equip:
                    if (IsEquipped(item) == false)
                    {
                        InventoryResult result = _commands.Equip(item, request.SlotId);
                        OperationCompleted?.Invoke(EquipmentAnimationRequestKind.Equip, item, result, true);
                        return result.Success;
                    }

                    break;
                case EquipmentAnimationRequestKind.Unequip:
                    if (IsEquipped(item))
                    {
                        return Unequip(item, request.Target, true);
                    }

                    break;
                case EquipmentAnimationRequestKind.Drop:
                    InventoryResult dropped = _commands.Drop(item);
                    OperationCompleted?.Invoke(EquipmentAnimationRequestKind.Drop, item, dropped, true);
                    return dropped.Success;
            }

            return false;
        }

        private bool Unequip(ItemInstance item, InventoryUnequipTarget target, bool playSound)
        {
            if (_model.ContainsItem(item) == false)
            {
                return false;
            }

            InventoryResult result = _commands.Unequip(item, target);
            OperationCompleted?.Invoke(EquipmentAnimationRequestKind.Unequip, item, result, playSound);
            return result.Success;
        }

        private bool Advance(EquipmentAnimationStep step, float deltaTime)
        {
            switch (step.Kind)
            {
                case EquipmentAnimationStepKind.Move:
                    step.Move?.Invoke();
                    return true;
                case EquipmentAnimationStepKind.Fade:
                    return _playback.AdvanceFade(step, deltaTime);
                case EquipmentAnimationStepKind.WeaponHolster:
                    if (IsWeaponInHands(step.Item))
                    {
                        _weapons.Holster();
                        return IsWeaponInHands(step.Item) == false;
                    }

                    return true;
                default:
                    if (!step.Started && step.Item != null && GetPatchMotion(step.Item) != step.Motion)
                    {
                        return FinishAnimation(step);
                    }

                    return _playback.AdvanceAnimation(step, deltaTime) && FinishAnimation(step);
            }
        }

        private void OnAnimationStopped(FirstPersonHudMotion motion, bool completed)
        {
            if (_steps.TryPeek(out EquipmentAnimationStep step) && step.Kind == EquipmentAnimationStepKind.Animation && step.Started && step.Item != null && step.Motion == motion)
            {
                step.Completed = completed;
            }
        }

        private bool FinishAnimation(EquipmentAnimationStep step)
        {
            ItemInstance item = step.Item;
            if (item != null)
            {
                if (step.Completed && GetPatchMotion(item) == step.Motion)
                {
                    InventoryResult result = _commands.RemovePatch(item, true);
                    if (result.Success == false)
                    {
                        OperationCompleted?.Invoke(EquipmentAnimationRequestKind.Patch, item, result, false);
                    }
                }

                SetInUse(item, false);
            }

            return true;
        }

        private static string GetSection(ItemInstance item)
        {
            EquipmentStatsComponent stats = item != null ? item.Definition.GetComponent<EquipmentStatsComponent>() : null;
            return stats != null ? stats.Section : string.Empty;
        }
    }
}
