using UnityEngine;

namespace Inventory.UI
{
    public enum InventorySound
    {
        Open,
        Close,
        ToSlot,
        ToBag,
        ContextMenu,
        Drop,
        Use
    }

    [CreateAssetMenu(menuName = "Inventory/Sound Set", fileName = "InventorySoundSet")]
    public sealed class InventorySoundSet : ScriptableObject
    {
        [SerializeField] private AudioClip _open;
        [SerializeField] private AudioClip _close;
        [SerializeField] private AudioClip _toSlot;
        [SerializeField] private AudioClip _toBag;
        [SerializeField] private AudioClip _contextMenu;
        [SerializeField] private AudioClip _drop;
        [SerializeField] private AudioClip _use;
        [SerializeField] [Range(0f, 1f)] private float _volume = 1f;
        [Tooltip("Sounds requested sooner than this after the previous one are skipped.")]
        [SerializeField] [Min(0f)] private float _minInterval = 0.5f;

        public float Volume => _volume;
        public float MinInterval => _minInterval;

        public AudioClip Get(InventorySound sound)
        {
            switch (sound)
            {
                case InventorySound.Open:
                    return _open;
                case InventorySound.Close:
                    return _close;
                case InventorySound.ToSlot:
                    return _toSlot;
                case InventorySound.ToBag:
                    return _toBag;
                case InventorySound.ContextMenu:
                    return _contextMenu;
                case InventorySound.Drop:
                    return _drop;
                default:
                    return _use;
            }
        }

        public void SetClips(AudioClip open, AudioClip close, AudioClip toSlot, AudioClip toBag, AudioClip contextMenu, AudioClip drop, AudioClip use, float minInterval)
        {
            _open = open;
            _close = close;
            _toSlot = toSlot;
            _toBag = toBag;
            _contextMenu = contextMenu;
            _drop = drop;
            _use = use;
            _minInterval = Mathf.Max(0f, minInterval);
        }
    }
}
