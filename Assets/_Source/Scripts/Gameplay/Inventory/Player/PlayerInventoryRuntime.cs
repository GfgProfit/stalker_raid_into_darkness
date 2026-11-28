using System;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Inventory
{
    [DefaultExecutionOrder(-201)]
    [DisallowMultipleComponent]
    public sealed class PlayerInventoryRuntime : MonoBehaviour, IStalkerCarryWeightSource, IStalkerSprintSource, IAnomalyProtectionSource, IAnomalyEquipmentWearSource
    {
        [SerializeField] private ItemDatabase _database;
        [SerializeField] private InventorySeed _seed;
        [SerializeField] private string _ownerId = "Player";
        [SerializeField] private string _equipmentContainerId = "Equipment";
        [SerializeField] private InventoryGridSettings _backpack;
        [SerializeField] private InventoryGridSettings _stash;
        [SerializeField] private string _equipmentDisplayName;
        [SerializeField] private InventorySlotLayout _equipmentLayout;
        [SerializeField] private SlotId[] _equipmentSlots = Array.Empty<SlotId>();
        [SerializeField] private StalkerPlayerController _player;
        [SerializeField] private FirstPersonWeaponController _weapons;
        [SerializeField] private WorldItemInteractor _worldItems;

        private readonly InventoryObservers<Action<ItemInstance, InventoryResult>> _forcedDropCompleted = new();

        private InventoryModel _model;
        private AnomalyDamageReceiver _damageReceiver;
        private FirstPersonOutfitBinder _outfitBinder;
        private bool _seeded;
        private bool _initialized;
        private bool _initializationAttempted;

        internal event Action<ItemInstance, InventoryResult> ForcedDropCompleted
        {
            add => _forcedDropCompleted.Add(value);
            remove => _forcedDropCompleted.Remove(value);
        }

        internal string PrimaryContainerId => _backpack?.Id;
        internal string EquipmentContainerId => _equipmentContainerId;
        internal StalkerPlayerController Player => _player;
        internal FirstPersonWeaponController Weapons => _weapons;
        internal FirstPersonWeaponBinder WeaponBinder { get; private set; }
        internal InventoryWeaponAmmoSource WeaponAmmo { get; private set; }
        internal InventoryWeaponAttachments WeaponAttachments { get; private set; }
        internal EquipmentAnimationQueue EquipmentAnimations { get; private set; }

        public ItemDatabase Database => _database;
        public InventorySeed Seed => _seed;
        public bool IsSeeded => _seeded;
        public float TotalWeight => Model.GetOwnerWeight(_ownerId);

        public float AdditionalWeight
        {
            get
            {
                InventoryContainer equipment = Model.GetContainer(_equipmentContainerId);
                float weight = 0f;

                if (equipment == null)
                {
                    return weight;
                }

                for (int i = 0; i < equipment.Items.Count; i++)
                {
                    ItemDefinition definition = equipment.Items[i].Definition;
                    EquipmentStatsComponent stats = definition.GetComponent<EquipmentStatsComponent>();

                    if (stats != null && (definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY) || definition.IsCategory(AnomalyEquipmentProtection.BACKPACK_CATEGORY)))
                    {
                        weight += stats.AdditionalWeight;
                    }
                }

                return weight;
            }
        }

        public bool SprintAllowed
        {
            get
            {
                InventoryContainer equipment = Model.GetContainer(_equipmentContainerId);

                if (equipment == null)
                {
                    return true;
                }

                for (int i = 0; i < equipment.Items.Count; i++)
                {
                    ItemDefinition definition = equipment.Items[i].Definition;
                    EquipmentStatsComponent stats = definition.GetComponent<EquipmentStatsComponent>();

                    if (stats != null && stats.SprintAllowed == false && definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public InventoryModel Model
        {
            get
            {
                if (_model == null)
                {
                    _model = new InventoryModel(_database);
                }

                return _model;
            }
        }

        public void Configure(InventoryGridSettings backpack, InventoryGridSettings stash, string equipmentContainerId, string equipmentDisplayName, InventorySlotLayout equipmentLayout, SlotId[] equipmentSlots, StalkerPlayerController player, FirstPersonWeaponController weapons, WorldItemInteractor worldItems)
        {
            if (_initialized)
            {
                throw new InvalidOperationException("Player inventory is already initialized.");
            }

            _backpack = backpack;
            _stash = stash;
            _equipmentContainerId = equipmentContainerId;
            _equipmentDisplayName = equipmentDisplayName;
            _equipmentLayout = equipmentLayout;
            _equipmentSlots = equipmentSlots != null ? (SlotId[])equipmentSlots.Clone() : Array.Empty<SlotId>();
            _player = player;
            _weapons = weapons;
            _worldItems = worldItems;
            _initializationAttempted = false;
        }

        internal bool EnsureInitialized()
        {
            if (_initializationAttempted)
            {
                return _initialized;
            }

            _initializationAttempted = true;
            if (_backpack == null || _stash == null || _backpack.IsValid == false || _stash.IsValid == false || string.IsNullOrWhiteSpace(_equipmentContainerId) || _backpack.Id == _stash.Id || _backpack.Id == _equipmentContainerId || _stash.Id == _equipmentContainerId || _equipmentLayout == null)
            {
                Debug.LogError("Player inventory requires distinct backpack, stash and equipment containers with an equipment layout.", this);
                return false;
            }

            if (_equipmentLayout.TryBuildSlots(out List<SlotDefinition> slots, out int width, out int height, out string layoutError) == false)
            {
                Debug.LogError($"Player inventory equipment layout is invalid: {layoutError}", this);
                return false;
            }

            if (_equipmentSlots is { Length: > 0 })
            {
                var selected = new HashSet<SlotId>(_equipmentSlots);
                slots.RemoveAll(slot => selected.Remove(slot.Id) == false);
                if (selected.Count != 0 || slots.Count != _equipmentSlots.Length)
                {
                    Debug.LogError("Player inventory equipment selection contains duplicate or undefined slots.", this);
                    return false;
                }
            }

            InventoryModel model = Model;
            GridContainer backpack = model.AddContainer(_backpack.CreateContainer());
            SlottedContainer equipment = model.AddContainer(new SlottedContainer(_equipmentContainerId, slots, width, height, true, _ownerId, _equipmentDisplayName));
            model.AddContainer(_stash.CreateContainer());
            model.BindOutfitSlotRules(new OutfitSlotRules(equipment, backpack, _worldItems, _player, PublishForcedDrop));
            SeedOnce();

            WeaponAmmo = new InventoryWeaponAmmoSource(model, _backpack.Id);
            WeaponAttachments = new InventoryWeaponAttachments(model, _backpack.Id);

            if (_weapons != null)
            {
                WeaponBinder = new FirstPersonWeaponBinder(model, _equipmentContainerId, _backpack.Id, _weapons, WeaponAmmo);
                WeaponBinder.Refresh();
            }

            if (_player != null)
            {
                BindDamageReceiver(_player.GetComponent<AnomalyDamageReceiver>());
                if (_player.Rig != null)
                {
                    _outfitBinder = new FirstPersonOutfitBinder(model, _equipmentContainerId, _player);
                    _outfitBinder.Refresh();
                }

                if (_player.Motor != null && _player.Motor.Condition != null)
                {
                    _player.Motor.Condition.SetCarryWeightSource(this);
                    _player.Motor.Condition.SetSprintSource(this);
                }
            }

            if (_weapons != null || _player != null && _player.HudAnimator != null && _player.HudAnimator.Library != null)
            {
                EquipmentAnimations = new EquipmentAnimationQueue(this, _player, _weapons);
            }

            _initialized = true;
            if (_worldItems != null)
            {
                _worldItems.BindInventory(this);
            }

            return true;
        }

        internal InventoryResult TryDrop(ItemInstance item)
        {
            if (Model.ContainsItem(item) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            return _worldItems != null && _worldItems.isActiveAndEnabled ? _worldItems.TryDropToWorld(this, item) : Model.TryRemove(item.InstanceId);
        }

        private void PublishForcedDrop(ItemInstance item, InventoryResult result)
        {
            _forcedDropCompleted.Publish((Item: item, Result: result), static (observer, value) => observer(value.Item, value.Result), static value => $"{nameof(ForcedDropCompleted)} for item '{value.Item.InstanceId}'");
        }

        public float GetAnomalyProtection(AnomalyDamageType type, AnomalyStrength strength, AnomalyBalanceConfig config)
        {
            return AnomalyEquipmentProtection.GetCombinedProtection(Model.GetContainer(_equipmentContainerId), type, strength, config);
        }

        public void CaptureEquipmentWear(AnomalyDamageType type, AnomalyBalanceConfig config, List<AnomalyEquipmentWearTarget> targets)
        {
            AnomalyEquipmentWear.Capture(this, Model, Model.GetContainer(_equipmentContainerId), type, config, targets);
        }

        public void ApplyEquipmentWear(AnomalyEquipmentWearTarget target, float remainingCondition)
        {
            AnomalyEquipmentWear.Apply(this, Model, Model.GetContainer(_equipmentContainerId), target, remainingCondition);
        }

        public void BindDamageReceiver(AnomalyDamageReceiver receiver)
        {
            if (_damageReceiver != null && _damageReceiver != receiver)
            {
                _damageReceiver.UnregisterProtectionSource(this);
            }
            _damageReceiver = receiver;
            if (_damageReceiver != null)
            {
                _damageReceiver.RegisterProtectionSource(this);
            }
        }

        private void OnDestroy()
        {
            if (_worldItems != null)
            {
                _worldItems.UnbindInventory(this);
            }

            EquipmentAnimations?.Dispose();
            WeaponBinder?.Dispose();
            WeaponAmmo?.Dispose();
            _outfitBinder?.Dispose();
            _model?.BindOutfitSlotRules(null);
            BindDamageReceiver(null);
        }

        private void Start() => EnsureInitialized();

        private void Update()
        {
            if (_player == null || _player.SimulationPaused == false)
            {
                EquipmentAnimations?.Tick(Time.deltaTime);
            }
        }

        public void SeedOnce()
        {
            if (_seeded || _seed == null)
            {
                return;
            }

            _seeded = true;
            _seed.Apply(Model);
        }

        public IReadOnlyList<ItemDefinition> CollectDefinitionsInUse()
        {
            var definitions = new List<ItemDefinition>();

            foreach (ItemInstance item in Model.Items)
            {
                if (definitions.Contains(item.Definition) == false)
                {
                    definitions.Add(item.Definition);
                }
            }

            return definitions;
        }
    }
}
