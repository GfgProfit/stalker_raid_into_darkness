using System;
using Inventory;
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

        public GameObject HudModel => _hudModel;
        public XRayMotionLibrary WeaponMotions => _weaponMotions;
        public XRayMotionLibrary HandsMotions => _handsMotions;
        public string HandsMotionPrefix => _handsMotionPrefix ?? string.Empty;
        public string[] AttachmentBones => _attachmentBones ?? Array.Empty<string>();
    }
}
