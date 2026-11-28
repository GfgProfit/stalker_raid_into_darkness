using System;
using Inventory;
using Inventory.IconGeneration;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WorldItem : MonoBehaviour
    {
        [SerializeField] private ItemDefinition _definition;
        [SerializeField] [Min(1)] private int _amount = 1;
        [SerializeField] private GameObject _silencerVisual;
        [SerializeField] private BoxCollider _silencerCollider;
        [SerializeField] private WorldWeaponPoseBone[] _weaponPose = Array.Empty<WorldWeaponPoseBone>();

        private ItemInstance _instance;

        public ItemDefinition Definition => _definition;
        public int Amount => Mathf.Max(1, _amount);
        public ItemInstance Instance => _instance;

        private void Awake()
        {
            ApplyWeaponPose();
        }

        public void Configure(ItemInstance item)
        {
            _definition = item.Definition;
            _amount = item.Amount;
            _instance = item;
            ItemIconVisualConfigurators.Apply(gameObject, item.Definition, item.VisualVariant);
            ApplyWeaponPose();
            ApplySilencerCollider();
        }

        public void Configure(ItemDefinition definition, int amount)
        {
            _definition = definition;
            _amount = Mathf.Max(1, amount);
            _instance = null;
            ItemIconVisualConfigurators.Apply(gameObject, definition, null);
            ApplyWeaponPose();
            ApplySilencerCollider();
        }

        private void ApplyWeaponPose()
        {
            if (_weaponPose.Length == 0)
            {
                return;
            }

            bool empty = InventoryWeaponAmmoSource.GetLoadedMagazine(_instance) == 0;
            for (int i = 0; i < _weaponPose.Length; i++)
            {
                _weaponPose[i].Apply(empty);
            }
        }

        private void ApplySilencerCollider()
        {
            if (_silencerCollider == null)
            {
                return;
            }

            bool installed = _silencerVisual != null && _silencerVisual.activeSelf;
            Transform colliderTransform = _silencerCollider.transform;
            Transform parent = installed ? _silencerVisual.transform : transform;

            if (colliderTransform.parent != parent)
            {
                colliderTransform.SetParent(parent, false);
            }

            _silencerCollider.gameObject.SetActive(installed);
        }
    }
}
