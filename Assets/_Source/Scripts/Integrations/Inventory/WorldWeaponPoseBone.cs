using System;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [Serializable]
    public struct WorldWeaponPoseBone
    {
        [SerializeField] private Transform _bone;
        [SerializeField] private Vector3 _loadedPosition;
        [SerializeField] private Quaternion _loadedRotation;
        [SerializeField] private Vector3 _emptyPosition;
        [SerializeField] private Quaternion _emptyRotation;

        internal void Apply(bool empty)
        {
            if (_bone != null)
            {
                _bone.SetLocalPositionAndRotation(empty ? _emptyPosition : _loadedPosition, empty ? _emptyRotation : _loadedRotation);
            }
        }
    }
}
