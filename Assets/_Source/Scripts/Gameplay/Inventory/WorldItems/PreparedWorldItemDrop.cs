using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Player;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    internal sealed class PreparedWorldItemDrop : IPreparedOutfitDrop
    {
        private readonly StalkerPlayerController _owner;
        private readonly WorldItem _worldItem;
        private readonly Collider[] _colliders;
        private readonly Vector3 _direction;

        public PreparedWorldItemDrop(StalkerPlayerController owner, WorldItem worldItem, Collider[] colliders, Vector3 direction)
        {
            _owner = owner;
            _worldItem = worldItem;
            _colliders = colliders;
            _direction = direction;
        }

        public void Complete(ItemInstance item)
        {
            _worldItem.CompleteDrop(item);
            WorldItemDrop.ApplyMotion(_owner, _worldItem, _colliders, _direction);
            _worldItem.enabled = true;
        }

        public void Cancel() => WorldItemDrop.Cancel(_worldItem);
    }
}
