using System;
using System.Collections.Generic;
using System.Globalization;
using Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public enum WeaponAddonStatus
    {
        None = 0,
        Permanent = 1,
        Attachable = 2
    }

    [Serializable]
    public sealed class WeaponStatsComponent : ItemDefinitionComponent
    {
        [SerializeField] private string _section;
        [SerializeField] private string _weaponClass;
        [SerializeField] private int _cost;
        [SerializeField] private string _repairType;
        [SerializeField] private float _hitPower;
        [SerializeField] private float _hitImpulse;
        [SerializeField] [InspectorName("Flatness (m/s)")] [Min(1f)] private float _bulletSpeed;
        [SerializeField] private float _fireDistance;
        [SerializeField] private float _rpm;
        [SerializeField] private int[] _fireModes = Array.Empty<int>();
        [SerializeField] private int _magazineSize;
        [SerializeField] private string[] _ammoClasses = Array.Empty<string>();
        [SerializeField] private float _fireDispersionBase;
        [SerializeField] private float _camDispersion;
        [SerializeField] private float _camDispersionIncrement;
        [SerializeField] private float _conditionShotDecrement;
        [SerializeField] private float _misfireProbability;
        [SerializeField] private WeaponAddonStatus _silencerStatus;
        [SerializeField] private string _silencerSection;
        [SerializeField] private WeaponAddonStatus _scopeStatus;
        [SerializeField] private string[] _scopes = Array.Empty<string>();
        [SerializeField] private ItemDefinition[] _compatibleScopes = Array.Empty<ItemDefinition>();
        [SerializeField] private WeaponAddonStatus _grenadeLauncherStatus;
        [SerializeField] private string _grenadeLauncherSection;

        private ItemDatabase _ammoDatabase;
        private Dictionary<string, ItemDefinition> _ammoDefinitions;

        public string Section => _section ?? string.Empty;
        public string WeaponClass => _weaponClass ?? string.Empty;
        public int Cost => _cost;
        public string RepairType => _repairType ?? string.Empty;
        public float HitPower => _hitPower;
        public float HitImpulse => _hitImpulse;
        public float BulletSpeed => _bulletSpeed;
        public float FireDistance => _fireDistance;
        public float Rpm => _rpm;
        public int[] FireModes => _fireModes ?? Array.Empty<int>();
        public int MagazineSize => _magazineSize;
        public string[] AmmoClasses => _ammoClasses ?? Array.Empty<string>();
        public float FireDispersionBase => _fireDispersionBase;
        public float CamDispersion => _camDispersion;
        public float CamDispersionIncrement => _camDispersionIncrement;
        public float ConditionShotDecrement => _conditionShotDecrement;
        public float MisfireProbability => _misfireProbability;
        public WeaponAddonStatus SilencerStatus => _silencerStatus;
        public string SilencerSection => _silencerSection ?? string.Empty;
        public WeaponAddonStatus ScopeStatus => _scopeStatus;
        public string[] Scopes => _scopes ?? Array.Empty<string>();
        public IReadOnlyList<ItemDefinition> CompatibleScopes => _compatibleScopes ?? Array.Empty<ItemDefinition>();
        public WeaponAddonStatus GrenadeLauncherStatus => _grenadeLauncherStatus;
        public string GrenadeLauncherSection => _grenadeLauncherSection ?? string.Empty;

        public override float GetAdditionalWeight(ItemInstance item, IItemDefinitionResolver resolver)
        {
            ItemDefinition launcher = GetGrenadeLauncher(item, resolver);
            float attachmentWeight = (GetSilencer(item, resolver)?.Weight ?? 0f) + (GetScope(item, resolver)?.Weight ?? 0f) + (launcher?.Weight ?? 0f) + (launcher?.GetComponent<WeaponAddonComponent>()?.GetAdditionalWeight(item, resolver) ?? 0f);
            string section = item.GetState(InventoryWeaponAmmoSource.AMMO_SECTION_STATE);

            if (SupportsAmmo(section) == false || int.TryParse(item.GetState(InventoryWeaponAmmoSource.MAGAZINE_STATE), NumberStyles.Integer, CultureInfo.InvariantCulture, out int magazine) == false || resolver is not ItemDatabase database)
            {
                return attachmentWeight;
            }

            if (_ammoDatabase != database || _ammoDefinitions == null)
            {
                _ammoDatabase = database;
                _ammoDefinitions = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

                for (int i = 0; i < database.Definitions.Count; i++)
                {
                    ItemDefinition definition = database.Definitions[i];
                    AmmoStatsComponent ammo = definition != null ? definition.GetComponent<AmmoStatsComponent>() : null;

                    if (ammo != null)
                    {
                        _ammoDefinitions[ammo.Section] = definition;
                    }
                }
            }

            return attachmentWeight + (_ammoDefinitions.TryGetValue(section, out ItemDefinition loaded) ? loaded.Weight * Math.Clamp(magazine, 0, MagazineSize) : 0f);
        }

        public override Vector2Int GetSizeDelta(ItemInstance item, IItemDefinitionResolver resolver) => GetSilencer(item, resolver) != null ? Vector2Int.right : Vector2Int.zero;

        public bool SupportsSilencer(ItemDefinition definition)
        {
            WeaponAddonComponent addon = definition != null ? definition.GetComponent<WeaponAddonComponent>() : null;
            return SilencerStatus == WeaponAddonStatus.Attachable && addon != null && addon.Kind == WeaponAddonKind.Silencer && string.IsNullOrEmpty(SilencerSection) == false && addon.Section == SilencerSection;
        }

        public ItemDefinition GetSilencer(ItemInstance item, IItemDefinitionResolver resolver)
        {
            string id = item.GetState(InventoryWeaponAttachments.SILENCER_STATE);
            return resolver != null && string.IsNullOrEmpty(id) == false && resolver.TryResolve(id, out ItemDefinition definition) && SupportsSilencer(definition) ? definition : null;
        }

        public bool SupportsScope(ItemDefinition definition)
        {
            WeaponAddonComponent addon = definition != null ? definition.GetComponent<WeaponAddonComponent>() : null;

            if (addon == null || addon.Kind != WeaponAddonKind.Scope)
            {
                return false;
            }

            foreach (ItemDefinition compatible in CompatibleScopes)
            {
                if (compatible != null && compatible == definition)
                {
                    return true;
                }
            }

            return false;
        }

        public ItemDefinition GetScope(ItemInstance item, IItemDefinitionResolver resolver)
        {
            string id = item?.GetState(InventoryWeaponAttachments.SCOPE_STATE);
            return resolver != null && string.IsNullOrEmpty(id) == false && resolver.TryResolve(id, out ItemDefinition definition) && SupportsScope(definition) ? definition : null;
        }

        public bool SupportsGrenadeLauncher(ItemDefinition definition)
        {
            WeaponAddonComponent addon = definition?.GetComponent<WeaponAddonComponent>();
            return GrenadeLauncherStatus == WeaponAddonStatus.Attachable && addon != null && addon.Kind == WeaponAddonKind.GrenadeLauncher && GrenadeLauncherSection.Length > 0 && addon.Section == GrenadeLauncherSection;
        }

        public ItemDefinition GetGrenadeLauncher(ItemInstance item, IItemDefinitionResolver resolver)
        {
            string id = item?.GetState(InventoryWeaponAttachments.GRENADE_LAUNCHER_STATE);
            return resolver != null && string.IsNullOrEmpty(id) == false && resolver.TryResolve(id, out ItemDefinition definition) && SupportsGrenadeLauncher(definition) ? definition : null;
        }

        public bool SupportsAmmo(string section)
        {
            if (string.IsNullOrEmpty(section))
            {
                return false;
            }

            foreach (string compatible in AmmoClasses)
            {
                if (compatible == section)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
