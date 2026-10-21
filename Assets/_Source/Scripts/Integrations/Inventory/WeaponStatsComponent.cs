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
        [SerializeField] private float _bulletSpeed;
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
        public WeaponAddonStatus GrenadeLauncherStatus => _grenadeLauncherStatus;
        public string GrenadeLauncherSection => _grenadeLauncherSection ?? string.Empty;

        public override float GetAdditionalWeight(ItemInstance item, IItemDefinitionResolver resolver)
        {
            string section = item.GetState(InventoryWeaponAmmoSource.AMMO_SECTION_STATE);

            if (SupportsAmmo(section) == false || int.TryParse(item.GetState(InventoryWeaponAmmoSource.MAGAZINE_STATE), NumberStyles.Integer, CultureInfo.InvariantCulture, out int magazine) == false || resolver is not ItemDatabase database)
            {
                return 0f;
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

            return _ammoDefinitions.TryGetValue(section, out ItemDefinition loaded) ? loaded.Weight * Math.Clamp(magazine, 0, MagazineSize) : 0f;
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
