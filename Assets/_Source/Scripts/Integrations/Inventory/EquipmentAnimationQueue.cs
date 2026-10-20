using System;
using System.Collections.Generic;
using Inventory;
using Inventory.UI;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class EquipmentAnimationQueue : IInventoryEquipmentGate, IDisposable
    {
        public const float UNEQUIP_FADE_DELAY = 0.4f;
        public const float EQUIP_FADE_START = 0.35f;
        public const float EQUIP_FADE_STOP = 0.25f;

        private const int MAX_STEPS_PER_TICK = 64;

        public static readonly Color IN_USE_TINT = new Color32(255, 159, 82, 100);

        private enum RequestKind
        {
            Equip,
            Unequip,
            Drop
        }

        private enum StepKind
        {
            Move,
            Animation,
            Fade
        }

        private sealed class Request
        {
            public RequestKind Kind { get; set; }
            public ItemInstance Item { get; set; }
            public SlotDefinition Slot { get; set; }
            public InventoryUnequipTarget Target { get; set; }
        }

        private sealed class Step
        {
            public StepKind Kind { get; set; }
            public Action Move { get; set; }
            public FirstPersonHudMotion Motion { get; set; }
            public float Speed { get; set; }
            public bool FadeAtEnd { get; set; }
            public bool Started { get; set; }
            public bool FadeStarted { get; set; }
            public bool FadeStopped { get; set; }
            public float Time { get; set; }
        }

        private readonly InventoryWindow _window;
        private readonly StalkerPlayerController _player;
        private readonly Func<ItemInstance, InventoryItemDecoration> _decorator;
        private readonly Queue<Request> _requests = new Queue<Request>();
        private readonly Queue<Step> _steps = new Queue<Step>();
        private readonly HashSet<ItemInstance> _inUse = new HashSet<ItemInstance>();
        private readonly List<string> _outfitMemory = new List<string>();
        private string _community = FirstPersonEquipmentAnimationLibrary.DEFAULT_COMMUNITY;

        public EquipmentAnimationQueue(InventoryWindow window, StalkerPlayerController player)
        {
            _window = window;
            _player = player;
            _decorator = window.ItemDecorator;
            _window.ItemDecorator = Decorate;
            _window.EquipmentGate = this;
        }

        public bool IsBusy => _steps.Count > 0 || _requests.Count > 0;
        public int PendingRequests => _requests.Count;
        public IReadOnlyCollection<ItemInstance> ItemsInUse => _inUse;

        public string Community
        {
            get => _community;
            set => _community = string.IsNullOrEmpty(value) ? FirstPersonEquipmentAnimationLibrary.DEFAULT_COMMUNITY : value;
        }

        private FirstPersonHudAnimator Animator => _player != null ? _player.HudAnimator : null;
        private FirstPersonEquipmentAnimationLibrary Library => Animator != null ? Animator.Library : null;

        public bool IsInUse(ItemInstance item) => item != null && _inUse.Contains(item);

        public bool HandleEquip(ItemInstance item, SlotDefinition slot)
        {
            if (IsAnimated(item) == false || _window.IsEquipped(item))
            {
                return false;
            }

            if (_inUse.Contains(item))
            {
                return true;
            }

            if (_window.CanEquipItem(item) == false)
            {
                return false;
            }

            Enqueue(new Request { Kind = RequestKind.Equip, Item = item, Slot = slot });
            return true;
        }

        public bool HandleUnequip(ItemInstance item, InventoryUnequipTarget target)
        {
            if (IsAnimated(item) == false || _window.IsEquipped(item) == false)
            {
                return false;
            }

            if (_inUse.Contains(item) == false)
            {
                Enqueue(new Request { Kind = RequestKind.Unequip, Item = item, Target = target });
            }

            return true;
        }

        public bool HandleDrop(ItemInstance item)
        {
            if (IsAnimated(item) == false || _window.IsEquipped(item) == false)
            {
                return false;
            }

            if (_inUse.Contains(item) == false)
            {
                Enqueue(new Request { Kind = RequestKind.Drop, Item = item });
            }

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
            if (_window.EquipmentGate == this)
            {
                _window.EquipmentGate = null;
            }

            _window.ItemDecorator = _decorator;
            _requests.Clear();
            _steps.Clear();
            _inUse.Clear();
        }

        private InventoryItemDecoration Decorate(ItemInstance item)
        {
            InventoryItemDecoration inner = _decorator != null ? _decorator(item) : InventoryItemDecoration.None;
            return item != null && _inUse.Contains(item) ? new InventoryItemDecoration(IN_USE_TINT, inner.Badge) : inner;
        }

        private bool IsAnimated(ItemInstance item)
        {
            FirstPersonEquipmentAnimationLibrary library = Library;
            if (item == null || library == null)
            {
                return false;
            }

            ItemDefinition definition = item.Definition;
            if (definition.IsCategory(EquipmentStatsFormatter.OUTFIT_CATEGORY))
            {
                return library.OutfitEnabled;
            }

            if (definition.IsCategory(EquipmentStatsFormatter.HELMET_CATEGORY))
            {
                return library.HeadgearEnabled;
            }

            return definition.IsCategory(EquipmentStatsFormatter.BACKPACK_CATEGORY) && library.BackpackEnabled;
        }

        private void Enqueue(Request request)
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
                _window.RefreshItems();
            }
        }

        private void Expand(Request request)
        {
            ItemInstance item = request.Item;
            bool equipped = _window.Model != null && _window.Model.ContainsItem(item) && _window.IsEquipped(item);
            bool valid = _window.Model != null && _window.Model.ContainsItem(item) && (request.Kind == RequestKind.Equip ? equipped == false : equipped);
            if (valid == false)
            {
                SetInUse(item, false);
                return;
            }

            ItemDefinition definition = item.Definition;
            if (definition.IsCategory(EquipmentStatsFormatter.OUTFIT_CATEGORY))
            {
                ExpandOutfit(request);
            }
            else if (definition.IsCategory(EquipmentStatsFormatter.HELMET_CATEGORY))
            {
                ExpandHeadgear(request);
            }
            else
            {
                ExpandBackpack(request);
            }
        }

        private void ExpandOutfit(Request request)
        {
            FirstPersonEquipmentAnimationLibrary library = Library;
            if (request.Kind == RequestKind.Equip)
            {
                AddMove(request);
                AddAnimation(library.Find(SelectOutfitSection(GetSection(request.Item)), FirstPersonEquipmentAnimationLibrary.ANM_EQUIP), 1.0f, false);
                return;
            }

            AddMove(request);
            AddAnimation(library.Find(FirstPersonEquipmentAnimationLibrary.OUTFIT_FAST_SECTION, FirstPersonEquipmentAnimationLibrary.ANM_EQUIP), 1.0f, false);
        }

        private void ExpandHeadgear(Request request)
        {
            FirstPersonEquipmentAnimationLibrary library = Library;
            float speed = library.HeadgearSpeed;
            if (request.Kind != RequestKind.Equip)
            {
                AddHeadgearRemoval(request.Item, () => ApplyRequest(request), speed);
                return;
            }

            ItemInstance old = EquipmentStatsFormatter.FindEquipped(_window, EquipmentStatsFormatter.HELMET_CATEGORY);
            if (old != null && old != request.Item)
            {
                AddHeadgearRemoval(old, () => _window.UnequipItemNow(old, InventoryUnequipTarget.Auto(_window.PrimaryContainerId), false), speed);
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
                _steps.Enqueue(new Step { Kind = StepKind.Fade });
            }

            if (library.IsHeadgearBlacklisted(section) == false)
            {
                AddAnimation(library.Find(library.GetHeadgearSection(section), FirstPersonEquipmentAnimationLibrary.ANM_UNEQUIP), speed, false);
            }

            _steps.Enqueue(new Step { Kind = StepKind.Move, Move = move });
        }

        private void ExpandBackpack(Request request)
        {
            FirstPersonEquipmentAnimationLibrary library = Library;
            FirstPersonHudMotion equip = library.Find(library.GetBackpackSection(FirstPersonEquipmentAnimationLibrary.BACKPACK_EQUIP_PREFIX, _community), FirstPersonEquipmentAnimationLibrary.ANM_USE);
            FirstPersonHudMotion unequip = library.Find(library.GetBackpackSection(FirstPersonEquipmentAnimationLibrary.BACKPACK_UNEQUIP_PREFIX, _community), FirstPersonEquipmentAnimationLibrary.ANM_USE);
            if (request.Kind != RequestKind.Equip)
            {
                AddAnimation(unequip, 1.0f, false);
                AddMove(request);
                return;
            }

            ItemInstance old = EquipmentStatsFormatter.FindEquipped(_window, EquipmentStatsFormatter.BACKPACK_CATEGORY);
            if (old != null && old != request.Item)
            {
                AddAnimation(unequip, 1.0f, false);
                _steps.Enqueue(new Step { Kind = StepKind.Move, Move = () => _window.UnequipItemNow(old, InventoryUnequipTarget.Auto(_window.PrimaryContainerId), false) });
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

        private void AddMove(Request request)
        {
            _steps.Enqueue(new Step { Kind = StepKind.Move, Move = () => ApplyRequest(request) });
        }

        private void AddAnimation(FirstPersonHudMotion motion, float speed, bool fadeAtEnd)
        {
            if (motion != null && motion.Hands != null)
            {
                _steps.Enqueue(new Step { Kind = StepKind.Animation, Motion = motion, Speed = speed, FadeAtEnd = fadeAtEnd });
            }
        }

        private void ApplyRequest(Request request)
        {
            ItemInstance item = request.Item;
            SetInUse(item, false);
            if (_window.Model == null || _window.Model.ContainsItem(item) == false)
            {
                return;
            }

            switch (request.Kind)
            {
                case RequestKind.Equip:
                    if (_window.IsEquipped(item) == false)
                    {
                        _window.EquipItemNow(item, request.Slot);
                    }

                    break;
                case RequestKind.Unequip:
                    if (_window.IsEquipped(item))
                    {
                        _window.UnequipItemNow(item, request.Target);
                    }

                    break;
                case RequestKind.Drop:
                    _window.DropItemNow(item);
                    break;
            }
        }

        private bool Advance(Step step, float deltaTime)
        {
            switch (step.Kind)
            {
                case StepKind.Move:
                    step.Move?.Invoke();
                    return true;
                case StepKind.Fade:
                    return AdvanceFade(step, deltaTime);
                default:
                    return AdvanceAnimation(step, deltaTime);
            }
        }

        private bool AdvanceFade(Step step, float deltaTime)
        {
            StalkerScreenFade fade = _player != null ? _player.ScreenFade : null;
            if (step.FadeStarted == false)
            {
                step.FadeStarted = true;
                step.Time = 0.0f;
                if (fade != null)
                {
                    fade.Play();
                }

                return false;
            }

            step.Time += deltaTime;
            if (step.FadeStopped == false)
            {
                step.FadeStopped = true;
                if (fade != null)
                {
                    fade.Stop();
                }

                return false;
            }

            return step.Time > UNEQUIP_FADE_DELAY;
        }

        private bool AdvanceAnimation(Step step, float deltaTime)
        {
            FirstPersonHudAnimator animator = Animator;
            if (animator == null)
            {
                return true;
            }

            if (step.Started == false)
            {
                if (animator.Rig != null && animator.Rig.HandsBusy)
                {
                    animator.Rig.RequestHands(animator);
                    return false;
                }

                step.Started = true;
                step.Time = 0.0f;
                return animator.TryPlay(step.Motion, step.Speed) == false;
            }

            step.Time += deltaTime;
            bool playing = animator.IsPlaying && animator.Current == step.Motion;
            if (step.FadeAtEnd)
            {
                TickEquipFade(step, animator.Duration, playing);
            }

            return playing == false;
        }

        private void TickEquipFade(Step step, float duration, bool playing)
        {
            StalkerScreenFade fade = _player != null ? _player.ScreenFade : null;
            if (fade == null)
            {
                return;
            }

            if (step.FadeStarted == false && (step.Time > duration - EQUIP_FADE_START || playing == false))
            {
                step.FadeStarted = true;
                fade.Play();
            }

            if (step.FadeStarted && step.FadeStopped == false && (step.Time > duration - EQUIP_FADE_STOP || playing == false))
            {
                step.FadeStopped = true;
                fade.Stop();
            }
        }

        private static string GetSection(ItemInstance item)
        {
            EquipmentStatsComponent stats = item != null ? item.Definition.GetComponent<EquipmentStatsComponent>() : null;
            return stats != null ? stats.Section : string.Empty;
        }
    }
}
