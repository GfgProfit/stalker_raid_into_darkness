using System;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Inventory.Domain
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

        public Vector3? GetScopeAimOffset(ItemDefinition scope) => GetScopeAimOffset(scope, out _);

        public Vector3? GetScopeAimOffset(ItemDefinition scope, out float depthOffset)
        {
            depthOffset = 0f;
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
                        depthOffset = offset.DepthOffset;
                        return offset.Position;
                    }
                }
            }

            WeaponAddonComponent addon = scope.GetComponent<WeaponAddonComponent>();
            return addon != null && addon.OverrideAimOffsetPosition ? addon.AimOffsetPosition : null;
        }
    }
}
