using System.Collections.Generic;
using Inventory;
using RaidIntoDarkness.Player;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [DisallowMultipleComponent]
    public sealed class PlayerInventoryRuntime : MonoBehaviour, IStalkerCarryWeightSource, IStalkerSprintSource, IAnomalyProtectionSource, IAnomalyEquipmentWearSource
    {
        [SerializeField] private ItemDatabase _database;
        [SerializeField] private InventorySeed _seed;
        [SerializeField] private string _ownerId = "Player";
        [SerializeField] private string _equipmentContainerId = "Equipment";

        private InventoryModel _model;
        private AnomalyDamageReceiver _damageReceiver;
        private bool _seeded;

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
            BindDamageReceiver(null);
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

        public string SaveJson() => InventorySerializer.ToJson(Model);

        public InventoryLoadReport LoadJson(string json) => InventorySerializer.FromJson(Model, json);
    }
}
