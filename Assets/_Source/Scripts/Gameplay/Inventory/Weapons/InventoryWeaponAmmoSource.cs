using System;
using System.Collections.Generic;
using System.Globalization;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Weapons;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
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

        public int GetMagazine(object weaponKey) => GetLoadedMagazine(GetWeapon(weaponKey));

        public bool HasLoadedAmmo(ItemInstance weapon) => GetLoadedMagazine(weapon) > 0 || GetGrenadeLauncher(weapon)?.GetLoadedGrenade(weapon) != null;

        public InventoryResult Unload(ItemInstance weapon, bool apply = true)
        {
            if (!_model.ContainsItem(weapon))
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            int magazine = GetLoadedMagazine(weapon);
            ItemDefinition ammo = magazine > 0 ? FindDefinition(weapon.GetState(AMMO_SECTION_STATE)) : null;
            ItemDefinition grenade = GetGrenadeLauncher(weapon)?.GetLoadedGrenade(weapon);

            if ((magazine == 0 && grenade == null) || (magazine > 0 && (ammo == null || magazine > ammo.MaxStack)))
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            ItemInstance returnedAmmo = ammo != null ? _model.CreateItem(ammo, magazine) : null;
            ItemInstance returnedGrenade = grenade != null ? _model.CreateItem(grenade) : null;
            ItemInstance updated = weapon.CreateDraft();

            if (returnedAmmo != null)
            {
                updated.SetState(MAGAZINE_STATE, "0");
            }

            if (returnedGrenade != null)
            {
                updated.SetState(InventoryWeaponAttachments.GRENADE_ROUND_STATE, null);
            }

            string destination = _model.GetContainer(weapon.ContainerId) is SlottedContainer ? _containerId : weapon.ContainerId;
            return _model.TryReconfigureItem(weapon, updated, weapon.Location.Position, returned: returnedAmmo ?? returnedGrenade, returnContainerId: destination, apply: apply, mergeReturned: true, returnedStack: returnedAmmo != null ? returnedGrenade : null);
        }

        public int GetReserve(object weaponKey) => Count(GetAmmoSection(weaponKey));

        public string GetAmmoSection(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            return ResolveAmmoSection(weapon, weapon?.Definition.GetComponent<WeaponStatsComponent>());
        }

        internal static string ResolveAmmoSection(ItemInstance weapon, WeaponStatsComponent stats)
        {
            if (stats == null)
            {
                return string.Empty;
            }

            string loaded = weapon?.GetState(AMMO_SECTION_STATE);

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

        public GrenadeAmmoData GetGrenadeShot(object weaponKey, out GameObject visual)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            ItemDefinition grenade = GetGrenadeLauncher(weapon)?.GetLoadedGrenade(weapon);
            visual = grenade != null ? grenade.VisualPrefab : null;
            return grenade?.GetComponent<AmmoBallisticsComponent>()?.Grenade;
        }

        public GrenadeAmmoData GetLauncherShot(object weaponKey, out GameObject visual)
        {
            ItemDefinition definition = GetMagazine(weaponKey) > 0 ? FindDefinition(GetAmmoSection(weaponKey)) : null;
            GrenadeAmmoData ammo = definition?.GetComponent<AmmoBallisticsComponent>()?.Grenade;
            visual = ammo?.ProjectileVisual != null ? ammo.ProjectileVisual : definition?.VisualPrefab;
            return ammo;
        }

        public float GetBulletSpeed(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();
            AmmoStatsComponent ammo = FindDefinition(GetAmmoSection(weaponKey))?.GetComponent<AmmoStatsComponent>();
            WeaponAddonComponent silencer = stats?.GetSilencer(weapon)?.GetComponent<WeaponAddonComponent>();
            return GetBulletSpeed(stats, ammo, silencer);
        }

        internal static float GetBulletSpeed(WeaponStatsComponent stats, AmmoStatsComponent ammo, WeaponAddonComponent silencer)
        {
            return (stats?.BulletSpeed ?? 0f) * (ammo?.SpeedMultiplier ?? 1f) * (silencer?.BulletSpeedFactor ?? 1f);
        }

        public float GetFireDistance(object weaponKey) => GetWeapon(weaponKey)?.Definition.GetComponent<WeaponStatsComponent>()?.FireDistance ?? 0f;

        public float GetDispersionDegrees(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();
            AmmoStatsComponent ammo = FindDefinition(GetAmmoSection(weaponKey))?.GetComponent<AmmoStatsComponent>();
            WeaponAddonComponent silencer = stats?.GetSilencer(weapon)?.GetComponent<WeaponAddonComponent>();
            return GetDispersionDegrees(stats, ammo, silencer);
        }

        internal static float GetDispersionDegrees(WeaponStatsComponent stats, AmmoStatsComponent ammo, WeaponAddonComponent silencer)
        {
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

            return _model.TrySetItemStateInPlace(weapon, AMMO_SECTION_STATE, section);
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

                int magazine = GetLoadedMagazine(weapon);
                ItemDefinition returnedAmmo = magazine > 0 ? FindDefinition(current) : null;
                if (magazine > 0 && returnedAmmo == null)
                {
                    return false;
                }

                ItemInstance updated = weapon.CreateDraft();
                if (magazine > 0)
                {
                    updated.SetState(MAGAZINE_STATE, "0");
                }

                updated.SetState(AMMO_SECTION_STATE, next);
                bool notifyAfterCommit = magazine == 0 && weapon.ContainerId != _containerId;
                InventoryResult result = _model.TryReturnRoundsAndUpdate(weapon, updated, _model.GetContainer(_containerId), returnedAmmo, magazine);
                if (result.Success && notifyAfterCommit)
                {
                    Changed?.Invoke();
                }

                return result.Success;
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

            string remaining = (magazine - 1).ToString(CultureInfo.InvariantCulture);

            if (weapon.Definition.GetComponent<WeaponStatsComponent>()?.GetIconVariantId(weapon) != null && magazine == 1)
            {
                ItemInstance updated = weapon.CreateDraft();
                updated.SetState(MAGAZINE_STATE, remaining);
                return _model.TryReconfigureItem(weapon, updated, weapon.Location.Position).Success;
            }

            return _model.TrySetItemStateInPlace(weapon, MAGAZINE_STATE, remaining);
        }

        public bool CanReloadGrenade(object weaponKey) => FindGrenadeStack(GetWeapon(weaponKey)) != null;

        public bool TryShootGrenade(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            if (GetGrenadeLauncher(weapon)?.GetLoadedGrenade(weapon) == null)
            {
                return false;
            }

            return _model.TrySetItemStateInPlace(weapon, InventoryWeaponAttachments.GRENADE_ROUND_STATE, null);
        }

        public bool ReloadGrenade(object weaponKey)
        {
            ItemInstance weapon = GetWeapon(weaponKey);
            ItemInstance grenade = FindGrenadeStack(weapon);

            if (grenade == null)
            {
                return false;
            }

            ItemInstance updated = weapon.CreateDraft();
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

            if (stats.HasExposedRound)
            {
                ItemInstance round = missing > 0 ? FindStack(section) : null;

                if (round != null)
                {
                    ItemInstance updated = weapon.CreateDraft();
                    updated.SetState(AMMO_SECTION_STATE, section);
                    updated.SetState(MAGAZINE_STATE, (magazine + 1).ToString(CultureInfo.InvariantCulture));
                    _model.TryReconfigureItem(weapon, updated, weapon.Location.Position, consumed: round);
                }

                return;
            }

            InventoryContainer backpack = _model.GetContainer(_containerId);
            if (missing == 0 || backpack == null || string.IsNullOrEmpty(section))
            {
                return;
            }

            List<KeyValuePair<ItemInstance, int>> consumed = null;
            int taken = 0;
            for (int i = 0; i < backpack.Items.Count && taken < missing; i++)
            {
                ItemInstance stack = backpack.Items[i];
                if (stack == weapon || stack.Definition.GetComponent<AmmoStatsComponent>()?.Section != section)
                {
                    continue;
                }

                int amount = Math.Min(stack.Amount, missing - taken);
                consumed ??= new List<KeyValuePair<ItemInstance, int>>();
                consumed.Add(new KeyValuePair<ItemInstance, int>(stack, amount));
                taken += amount;
            }

            if (consumed == null)
            {
                return;
            }

            ItemInstance draft = weapon.CreateDraft();
            draft.SetState(AMMO_SECTION_STATE, section);
            draft.SetState(MAGAZINE_STATE, (magazine + taken).ToString(CultureInfo.InvariantCulture));
            _model.TryConsumeStacksAndUpdate(weapon, draft, consumed);
        }

        public void Dispose()
        {
            _model.Changed -= OnInventoryChanged;
        }

        internal static int GetLoadedMagazine(ItemInstance weapon)
        {
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();

            if (stats == null || stats.SupportsAmmo(weapon.GetState(AMMO_SECTION_STATE)) == false)
            {
                return 0;
            }

            return int.TryParse(weapon.GetState(MAGAZINE_STATE), NumberStyles.Integer, CultureInfo.InvariantCulture, out int magazine) ? Math.Clamp(magazine, 0, stats.MagazineSize) : 0;
        }

        public void GetHandGrenadeStock(object weaponKey, out int count, out string name)
        {
            count = 0;
            name = string.Empty;
            ItemInstance selected = GetWeapon(weaponKey);
            InventoryContainer backpack = _model.GetContainer(_containerId);
            if (selected == null || selected.ContainerId != _containerId || backpack == null)
            {
                return;
            }

            long total = 0;
            foreach (ItemInstance item in backpack.Items)
            {
                if (item.Definition == selected.Definition)
                {
                    total += item.Amount;
                }
            }

            count = (int)Math.Min(total, int.MaxValue);
            name = selected.Definition.ShortName;
        }

        public bool TryConsumeHandGrenade(object weaponKey)
        {
            ItemInstance item = GetWeapon(weaponKey);
            return item != null && item.ContainerId == _containerId && item.Definition.IsCategory("Grenade") && _model.TryRemove(item.InstanceId, 1).Success;
        }

        private ItemInstance GetWeapon(object weaponKey) => _model.GetItem(weaponKey as string);

        private WeaponAddonComponent GetGrenadeLauncher(ItemInstance weapon) => weapon?.Definition.GetComponent<WeaponStatsComponent>()?.GetGrenadeLauncher(weapon)?.GetComponent<WeaponAddonComponent>();

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

        internal ItemDefinition FindDefinition(string section)
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
