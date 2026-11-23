using System;
using Inventory;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [Serializable]
    public sealed class WeaponHudComponent : ItemDefinitionComponent
    {
        public const string SCOPE_BONE = "wpn_scope";
        public const string SILENCER_BONE = "wpn_silencer";
        public const string GRENADE_LAUNCHER_BONE = "wpn_launcher";

        [SerializeField] private GameObject _hudModel;
        [SerializeField] private XRayMotionLibrary _weaponMotions;
        [SerializeField] private XRayMotionLibrary _handsMotions;
        [SerializeField] private string _handsMotionPrefix;
        [SerializeField] private string[] _attachmentBones = Array.Empty<string>();
        [SerializeField] private FirstPersonWeaponAsset _firstPersonWeapon;
        [SerializeField] private ScopeAimOffset[] _scopeAimOffsets = Array.Empty<ScopeAimOffset>();

        public GameObject HudModel => _hudModel;
        public XRayMotionLibrary WeaponMotions => _weaponMotions;
        public XRayMotionLibrary HandsMotions => _handsMotions;
        public string HandsMotionPrefix => _handsMotionPrefix ?? string.Empty;
        public string[] AttachmentBones => _attachmentBones ?? Array.Empty<string>();
        public FirstPersonWeaponAsset FirstPersonWeapon => _firstPersonWeapon;

        public Vector3? GetScopeAimOffset(ItemDefinition scope)
        {
            if (scope == null)
            {
                return null;
            }

            if (_scopeAimOffsets != null)
            {
                foreach (ScopeAimOffset offset in _scopeAimOffsets)
                {
                    if (offset.Scope == scope)
                    {
                        return offset.Position;
                    }
                }
            }

            WeaponAddonComponent addon = scope.GetComponent<WeaponAddonComponent>();
            return addon != null && addon.OverrideAimOffsetPosition ? addon.AimOffsetPosition : null;
        }

        [Serializable]
        private struct ScopeAimOffset
        {
            [SerializeField] private ItemDefinition _scope;
            [SerializeField] private Vector3 _position;

            public ItemDefinition Scope => _scope;
            public Vector3 Position => _position;
        }
    }
}
