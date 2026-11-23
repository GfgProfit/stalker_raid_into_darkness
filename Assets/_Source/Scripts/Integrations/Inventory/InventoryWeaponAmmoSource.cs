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

        public void GetGrenadeAmmo(object weaponKey, out int loaded, out int reserve, out string name)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            WeaponAddonComponent launcher = GetGrenadeLauncher(weapon);
            ItemDefinition grenade = launcher?.GetLoadedGrenade(weapon);
            loaded = grenade != null ? 1 : 0;
            grenade ??= launcher?.GrenadeAmmo;
            reserve = Count(grenade?.GetComponent<AmmoStatsComponent>()?.Section);

            if (loaded == 0 && reserve == 0 && launcher?.AlternativeGrenadeAmmo != null)
            {
                int alternativeReserve = Count(launcher.AlternativeGrenadeAmmo.GetComponent<AmmoStatsComponent>()?.Section);

                if (alternativeReserve > 0)
                {
                    grenade = launcher.AlternativeGrenadeAmmo;
                    reserve = alternativeReserve;
                }
            }

            name = grenade?.ShortName ?? string.Empty;
        }

        public BallisticAmmoData GetBallistics(object weaponKey) => FindDefinition(GetAmmoSection(weaponKey))?.GetComponent<AmmoBallisticsComponent>()?.Data;

        public float GetBulletSpeed(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();
            AmmoStatsComponent ammo = FindDefinition(GetAmmoSection(weaponKey))?.GetComponent<AmmoStatsComponent>();
            WeaponAddonComponent silencer = stats?.GetSilencer(weapon, _model.Resolver)?.GetComponent<WeaponAddonComponent>();
            return (stats?.BulletSpeed ?? 0f) * (ammo?.SpeedMultiplier ?? 1f) * (silencer?.BulletSpeedFactor ?? 1f);
        }

        public float GetFireDistance(object weaponKey) => GetWeapon(weaponKey)?.Definition.GetComponent<WeaponStatsComponent>()?.FireDistance ?? 0f;

        public float GetDispersionDegrees(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();
            AmmoStatsComponent ammo = FindDefinition(GetAmmoSection(weaponKey))?.GetComponent<AmmoStatsComponent>();
            WeaponAddonComponent silencer = stats?.GetSilencer(weapon, _model.Resolver)?.GetComponent<WeaponAddonComponent>();
            return (stats?.FireDispersionBase ?? 0f) * (ammo?.DispersionMultiplier ?? 1f) * (silencer?.FireDispersionBaseFactor ?? 1f);
        }

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
            _model.NotifyItemChanged(weapon);
            return true;
        }

        public bool CanReloadGrenade(object weaponKey) => FindGrenadeStack(GetWeapon(weaponKey)) != null;

        public bool ReloadGrenade(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            ItemInstance grenade = FindGrenadeStack(weapon);

            if (grenade == null)
            {
                return false;
            }

            ItemInstance updated = InventoryWeaponAttachments.Clone(weapon);
            updated.SetState(InventoryWeaponAttachments.GRENADE_ROUND_STATE, grenade.DefinitionId);
            return _model.TryReconfigureItem(weapon, updated, weapon.Location.Position, consumed: grenade).Success;
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
                _model.NotifyItemChanged(weapon);
            }
        }

        public void Dispose()
        {
            _model.Changed -= OnInventoryChanged;
        }

        private ItemInstance GetWeapon(object weaponKey) => _model.GetItem(weaponKey as string);

        private WeaponAddonComponent GetGrenadeLauncher(ItemInstance weapon) => weapon?.Definition.GetComponent<WeaponStatsComponent>()?.GetGrenadeLauncher(weapon, _model.Resolver)?.GetComponent<WeaponAddonComponent>();

        private ItemInstance FindGrenadeStack(ItemInstance weapon)
        {
            WeaponAddonComponent launcher = GetGrenadeLauncher(weapon);
            if (launcher == null || launcher.GetLoadedGrenade(weapon) != null)
            {
                return null;
            }

            return FindGrenadeStack(launcher.GrenadeAmmo) ?? FindGrenadeStack(launcher.AlternativeGrenadeAmmo);
        }

        private ItemInstance FindGrenadeStack(ItemDefinition grenade)
        {
            ItemInstance stack = FindStack(grenade?.GetComponent<AmmoStatsComponent>()?.Section);
            return stack?.Definition == grenade ? stack : null;
        }

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

            if (InventorySerializer.Restore(plan, snapshot).IsClean == false)
            {
                return false;
            }

            plan.GetItem(weaponKey as string)?.SetState(MAGAZINE_STATE, "0");

            if (ReturnAmmo(plan, definition, magazine) != magazine)
            {
                return false;
            }

            ItemInstance weapon = GetWeapon(weaponKey);
            weapon.SetState(MAGAZINE_STATE, "0");
            int returned = ReturnAmmo(_model, definition, magazine);
            weapon.SetState(MAGAZINE_STATE, (magazine - returned).ToString(CultureInfo.InvariantCulture));
            _model.NotifyItemChanged(weapon);
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
