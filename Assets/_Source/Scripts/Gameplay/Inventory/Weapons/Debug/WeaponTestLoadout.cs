using RaidIntoDarkness.Inventory.UI;
using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;
using System.Collections.Generic;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    [DisallowMultipleComponent]
    public sealed class WeaponTestLoadout : MonoBehaviour
    {
        private const int AMMO_BOX_COUNT = 5;
        private const int FULL_CONDITION = 100;

        [SerializeField] private InventoryWindowController _inventoryController;

        private void Update()
        {
            if (Keyboard.current?.f5Key.wasPressedThisFrame == true)
            {
                GrantLoadout();
            }
        }

        private void GrantLoadout()
        {
            InventoryWindowController inventory = _inventoryController;
            PlayerInventoryRuntime runtime = inventory != null ? inventory.Inventory : null;
            if (runtime == null || runtime.Database == null || runtime.EnsureInitialized() == false)
            {
                Debug.LogWarning("Инвентарь игрока недоступен.", this);
                return;
            }

            FirstPersonWeaponController weapons = runtime.Weapons;
            if (weapons == null || weapons.Model == null || weapons.State is FirstPersonWeaponState.Hidden or FirstPersonWeaponState.Holstering)
            {
                Debug.LogWarning("Возьми оружие в руки перед нажатием F5.", this);
                return;
            }

            SlotId slot = FirstPersonWeaponBinder.GetEquipmentSlotId(weapons.ActiveSlot);
            SlottedContainer equipment = runtime.Model.GetContainer(runtime.EquipmentContainerId) as SlottedContainer;
            ItemDefinition held = equipment?.GetSlotOccupant(slot)?.Definition;
            WeaponStatsComponent stats = held != null ? held.GetComponent<WeaponStatsComponent>() : null;
            if (stats == null || held.GetComponent<WeaponHudComponent>()?.FirstPersonWeapon != weapons.ActiveWeapon)
            {
                Debug.LogWarning("Не найден предмет оружия в руках.", this);
                return;
            }

            IReadOnlyList<ItemDefinition> definitions = runtime.Database.Definitions;
            var addons = new HashSet<ItemDefinition>();
            ItemDefinition compatibleAmmo = null;
            ItemDefinition fallbackAmmo = null;
            foreach (ItemDefinition definition in definitions)
            {
                if (definition == null)
                {
                    continue;
                }

                if (stats.SupportsScope(definition) || stats.SupportsSilencer(definition) || stats.SupportsGrenadeLauncher(definition))
                {
                    addons.Add(definition);
                }

                AmmoStatsComponent ammo = definition.GetComponent<AmmoStatsComponent>();
                if (ammo != null && ammo.BoxSize > 0)
                {
                    fallbackAmmo ??= definition;
                    if (compatibleAmmo == null && stats.SupportsAmmo(ammo.Section))
                    {
                        compatibleAmmo = definition;
                    }
                }
            }

            ItemDefinition ammoDefinition = compatibleAmmo ?? fallbackAmmo;
            int requestedRounds = ammoDefinition != null ? ammoDefinition.GetComponent<AmmoStatsComponent>().BoxSize * AMMO_BOX_COUNT : 0;
            int roundsAdded = requestedRounds > 0 ? InventorySpawn.Grant(runtime.isActiveAndEnabled ? runtime.Model : null, runtime.PrimaryContainerId, ammoDefinition, requestedRounds, FULL_CONDITION, out _) : 0;
            int addonsAdded = 0;
            foreach (ItemDefinition addon in addons)
            {
                addonsAdded += InventorySpawn.Grant(runtime.isActiveAndEnabled ? runtime.Model : null, runtime.PrimaryContainerId, addon, 1, FULL_CONDITION, out _);
            }

            string result = $"{held.DisplayName}: модули {addonsAdded}/{addons.Count}, патроны {roundsAdded}/{requestedRounds}.";
            if (addonsAdded < addons.Count || roundsAdded < requestedRounds || ammoDefinition == null)
            {
                Debug.LogWarning(result, this);
            }
            else
            {
                Debug.Log(result, this);
            }
        }
    }
}
