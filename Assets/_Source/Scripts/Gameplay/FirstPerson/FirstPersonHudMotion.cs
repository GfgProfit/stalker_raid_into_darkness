using System;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.FirstPerson
{
    [Serializable]
    public sealed class FirstPersonHudMotion
    {
        [Tooltip("<hud section>|<anm key>, for example outfit_animation_hud|anm_equip.")]
        [SerializeField] private string _id;
        [SerializeField] private AnimationClip _hands;
        [SerializeField] private GameObject _itemModel;
        [SerializeField] private AnimationClip _item;
        [SerializeField] private XRayAnmAsset _cameraEffect;
        [SerializeField] private AudioClip _sound;
        [Tooltip("Section 'hud_fov'; 0 keeps the console value.")]
        [SerializeField] private float _hudFieldOfView;
        [Tooltip("Section 'hands_position_16x9'.")]
        [SerializeField] private Vector3 _handsPosition;
        [Tooltip("Section 'hands_orientation_16x9': heading, pitch, bank in degrees.")]
        [SerializeField] private Vector3 _handsOrientation;

        public FirstPersonHudMotion(string id, AnimationClip hands, GameObject itemModel, AnimationClip item, XRayAnmAsset cameraEffect, AudioClip sound, float hudFieldOfView, Vector3 handsPosition, Vector3 handsOrientation)
        {
            _id = id;
            _hands = hands;
            _itemModel = itemModel;
            _item = item;
            _cameraEffect = cameraEffect;
            _sound = sound;
            _hudFieldOfView = hudFieldOfView;
            _handsPosition = handsPosition;
            _handsOrientation = handsOrientation;
        }

        public string Id => _id;
        public AnimationClip Hands => _hands;
        public GameObject ItemModel => _itemModel;
        public AnimationClip Item => _item;
        public XRayAnmAsset CameraEffect => _cameraEffect;
        public AudioClip Sound => _sound;
        public float HudFieldOfView => _hudFieldOfView;
        public Vector3 HandsPosition => _handsPosition;
        public Vector3 HandsOrientation => _handsOrientation;
        public Quaternion HandsRotation => Quaternion.Euler(_handsOrientation.y, _handsOrientation.x, _handsOrientation.z);

        public static string MakeId(string section, string key) => section + "|" + key;
    }
}
