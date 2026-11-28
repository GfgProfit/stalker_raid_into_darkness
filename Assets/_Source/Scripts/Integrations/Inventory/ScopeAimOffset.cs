using System;
using Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
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
