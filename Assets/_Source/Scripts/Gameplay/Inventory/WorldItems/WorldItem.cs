using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;
using System;
using RaidIntoDarkness.Inventory.Infrastructure.IconGeneration;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure
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
        private bool _pickupClaimed;

        public ItemDefinition Definition => _definition;
        public int Amount => Mathf.Max(1, _amount);
        public ItemInstance Instance => _instance;

        private void Awake()
        {
            ApplyWeaponPose();
        }

        internal bool TryClaimPickup()
        {
            if (_pickupClaimed || isActiveAndEnabled == false)
            {
                return false;
            }

            _pickupClaimed = true;
            return true;
        }

        internal void ReleasePickupClaim() => _pickupClaimed = false;

        public void Configure(ItemInstance item) => ConfigureAppearance(item, item);

        internal void ConfigureDropAppearance(ItemInstance item) => ConfigureAppearance(item, null);

        private void ConfigureAppearance(ItemInstance item, ItemInstance inventoryInstance)
        {
            _definition = item.Definition;
            _amount = item.Amount;
            _instance = inventoryInstance;
            ItemIconVisualConfigurators.Apply(gameObject, item.Definition, item.VisualVariant);
            ApplyWeaponPose(item);
            ApplySilencerCollider();
        }

        internal void CompleteDrop(ItemInstance item) => _instance = item;

        public void Configure(ItemDefinition definition, int amount)
        {
            _definition = definition;
            _amount = Mathf.Max(1, amount);
            _instance = null;
            ItemIconVisualConfigurators.Apply(gameObject, definition, null);
            ApplyWeaponPose();
            ApplySilencerCollider();
        }

        private void ApplyWeaponPose() => ApplyWeaponPose(_instance);

        private void ApplyWeaponPose(ItemInstance item)
        {
            if (_weaponPose.Length == 0)
            {
                return;
            }

            bool empty = WeaponMagazineState.GetLoadedRounds(item) == 0;
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
