using System;
using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.Application
{
    internal readonly struct InventoryPresentationBindings
    {
        public InventoryPresentationBindings(InventoryModel model, IItemIconProvider icons, InventoryWeaponAmmoSource weaponAmmo, InventoryWeaponAttachments weaponAttachments, IInventoryEquipmentPresentation equipment, IInventoryWeaponStatus weaponStatus, IInventorySpawnCatalog spawnCatalog, IWorldItemInteraction world, Func<ItemInstance, InventoryResult> drop)
        {
            Model = model;
            Icons = icons;
            WeaponAmmo = weaponAmmo;
            WeaponAttachments = weaponAttachments;
            Equipment = equipment;
            WeaponStatus = weaponStatus;
            SpawnCatalog = spawnCatalog;
            World = world;
            Drop = drop;
        }

        public InventoryModel Model { get; }
        public IItemIconProvider Icons { get; }
        public InventoryWeaponAmmoSource WeaponAmmo { get; }
        public InventoryWeaponAttachments WeaponAttachments { get; }
        public IInventoryEquipmentPresentation Equipment { get; }
        public IInventoryWeaponStatus WeaponStatus { get; }
        public IInventorySpawnCatalog SpawnCatalog { get; }
        public IWorldItemInteraction World { get; }
        public Func<ItemInstance, InventoryResult> Drop { get; }
    }
}
