using System;
using System.Globalization;
using Inventory;
using RaidIntoDarkness.Weapons;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class InventoryWeaponAmmoSource : IFirstPersonWeaponAmmoSource, IDisposable
    {
        public const string MAGAZINE_STATE = "weapon_magazine";
        public const string AMMO_SECTION_STATE = "weapon_ammo_section";

        private readonly InventoryModel _model;
        private readonly string _containerId;

        public InventoryWeaponAmmoSource(InventoryModel model, string containerId)
        {
            _model = model;
            _containerId = containerId;
            _model.Changed += OnInventoryChanged;
        }

        public event Action Changed;

        public int GetMagazine(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();

            if (stats == null || stats.SupportsAmmo(weapon.GetState(AMMO_SECTION_STATE)) == false)
            {
                return 0;
            }

            return int.TryParse(weapon.GetState(MAGAZINE_STATE), NumberStyles.Integer, CultureInfo.InvariantCulture, out int magazine) ? Math.Clamp(magazine, 0, stats.MagazineSize) : 0;
        }

        public int GetReserve(object weaponKey) => Count(GetAmmoSection(weaponKey));

        public string GetAmmoSection(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();

            if (stats == null)
            {
                return string.Empty;
            }

            string loaded = weapon.GetState(AMMO_SECTION_STATE);

            if (stats.SupportsAmmo(loaded))
            {
                return loaded;
            }

            return stats.AmmoClasses.Length > 0 ? stats.AmmoClasses[0] : string.Empty;
        }

        public string GetAmmoName(object weaponKey) => FindDefinition(GetAmmoSection(weaponKey))?.ShortName ?? string.Empty;

        public bool TryStartReload(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();

            if (stats == null || GetMagazine(weaponKey) >= stats.MagazineSize)
            {
                return false;
            }

            string section = GetReloadSection(weaponKey, stats);

            if (Count(section) <= 0)
            {
                return false;
            }

            weapon.SetState(AMMO_SECTION_STATE, section);
            return true;
        }

        public bool TrySwitchAmmo(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();

            if (stats == null)
            {
                return false;
            }

            string current = GetAmmoSection(weaponKey);
            int index = Array.IndexOf(stats.AmmoClasses, current);

            for (int i = 1; i < stats.AmmoClasses.Length; i++)
            {
                string next = stats.AmmoClasses[(index + i) % stats.AmmoClasses.Length];

                if (Count(next) <= 0)
                {
                    continue;
                }

                if (TryUnload(weaponKey) == false)
                {
                    return false;
                }

                weapon.SetState(AMMO_SECTION_STATE, next);
                Changed?.Invoke();
                return true;
            }

            return false;
        }

        public bool TryShoot(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            int magazine = GetMagazine(weaponKey);

            if (weapon == null || magazine <= 0)
            {
                return false;
            }

            weapon.SetState(MAGAZINE_STATE, (magazine - 1).ToString(CultureInfo.InvariantCulture));
            return true;
        }

        public void Reload(object weaponKey, int capacity)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();

            if (stats == null)
            {
                return;
            }

            int magazine = GetMagazine(weaponKey);
            int missing = Math.Max(0, Math.Min(capacity, stats.MagazineSize) - magazine);
            string section = GetReloadSection(weaponKey, stats);
            int taken = 0;

            while (taken < missing)
            {
                ItemInstance stack = FindStack(section);

                if (stack == null)
                {
                    break;
                }

                InventoryResult result = _model.TryRemove(stack.InstanceId, missing - taken);

                if (result.Success == false || result.Amount <= 0)
                {
                    break;
                }

                taken += result.Amount;
            }

            if (taken > 0)
            {
                weapon.SetState(AMMO_SECTION_STATE, section);
                weapon.SetState(MAGAZINE_STATE, (magazine + taken).ToString(CultureInfo.InvariantCulture));
            }
        }

        public void Dispose()
        {
            _model.Changed -= OnInventoryChanged;
        }

        private ItemInstance GetWeapon(object weaponKey) => _model.GetItem(weaponKey as string);

        private string GetReloadSection(object weaponKey, WeaponStatsComponent stats)
        {
            string section = GetAmmoSection(weaponKey);

            if (GetMagazine(weaponKey) > 0 || Count(section) > 0)
            {
                return section;
            }

            foreach (string compatible in stats.AmmoClasses)
            {
                if (Count(compatible) > 0)
                {
                    return compatible;
                }
            }

            return section;
        }

        private bool TryUnload(object weaponKey)
        {
            int magazine = GetMagazine(weaponKey);

            if (magazine <= 0)
            {
                return true;
            }

            ItemDefinition definition = FindDefinition(GetAmmoSection(weaponKey));
            InventoryContainer container = _model.GetContainer(_containerId);

            if (definition == null || container is not GridContainer)
            {
                return false;
            }

            var plan = new InventoryModel(_model.Resolver);
            plan.AddContainer(new GridContainer(container.Id, container.Width, container.Height) { MaxWeight = container.MaxWeight });
            InventorySaveData snapshot = InventorySerializer.Capture(_model, new[] { _containerId });

            if (InventorySerializer.Restore(plan, snapshot).IsClean == false || ReturnAmmo(plan, definition, magazine) != magazine)
            {
                return false;
            }

            int returned = ReturnAmmo(_model, definition, magazine);
            GetWeapon(weaponKey).SetState(MAGAZINE_STATE, (magazine - returned).ToString(CultureInfo.InvariantCulture));
            return returned == magazine;
        }

        private int ReturnAmmo(InventoryModel model, ItemDefinition definition, int amount)
        {
            int returned = 0;

            while (returned < amount)
            {
                ItemInstance box = model.CreateItem(definition, amount - returned);
                int count = box.Amount;

                if (model.TryAddAuto(box, _containerId).Success == false)
                {
                    break;
                }

                returned += count;
            }

            return returned;
        }

        private ItemDefinition FindDefinition(string section)
        {
            if (_model.Resolver is ItemDatabase database)
            {
                foreach (ItemDefinition definition in database.Definitions)
                {
                    if (definition.GetComponent<AmmoStatsComponent>()?.Section == section)
                    {
                        return definition;
                    }
                }
            }

            foreach (ItemInstance item in _model.Items)
            {
                if (item.Definition.GetComponent<AmmoStatsComponent>()?.Section == section)
                {
                    return item.Definition;
                }
            }

            return null;
        }

        private int Count(string section)
        {
            InventoryContainer container = _model.GetContainer(_containerId);
            int count = 0;

            if (container == null || string.IsNullOrEmpty(section))
            {
                return count;
            }

            for (int i = 0; i < container.Items.Count; i++)
            {
                ItemInstance item = container.Items[i];

                if (item.Definition.GetComponent<AmmoStatsComponent>()?.Section == section)
                {
                    count += item.Amount;
                }
            }

            return count;
        }

        private ItemInstance FindStack(string section)
        {
            InventoryContainer container = _model.GetContainer(_containerId);

            if (container == null || string.IsNullOrEmpty(section))
            {
                return null;
            }

            for (int i = 0; i < container.Items.Count; i++)
            {
                ItemInstance item = container.Items[i];

                if (item.Definition.GetComponent<AmmoStatsComponent>()?.Section == section)
                {
                    return item;
                }
            }

            return null;
        }

        private void OnInventoryChanged(InventoryChange change)
        {
            if (change.Affects(_containerId) || change.Kind == InventoryChangeKind.Cleared)
            {
                Changed?.Invoke();
            }
        }
    }
}
