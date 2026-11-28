using RaidIntoDarkness.Inventory.Domain;
using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    [Serializable]
    internal struct ScopeAimOffset
    {
        [SerializeField] private ItemDefinition _scope;
        [SerializeField] private Vector3 _position;
        [SerializeField] private float _depthOffset;

        public ItemDefinition Scope => _scope;
        public Vector3 Position => _position;
        public float DepthOffset => _depthOffset;
    }
}
