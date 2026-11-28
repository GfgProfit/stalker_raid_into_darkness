using System;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.UI;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    public sealed class InventoryItemSounds : IDisposable
    {
        public const float TAKE_VOLUME = 1f;
        public const float PUT_IN_BOX_VOLUME = 0.75f;
        public const float MIN_INTERVAL = 0.025f;

        private readonly InventoryWindow _window;
        private readonly InventoryModel _model;
        private float _lastTime = float.MinValue;

        public InventoryItemSounds(InventoryWindow window)
        {
            _window = window;
            _model = window.Model;
            _model.Changed += OnChanged;
        }

        public int PlayedCount { get; private set; }
        public AudioClip LastClip { get; private set; }
        public float LastVolume { get; private set; }

        public void PlayTake(ItemInstance item)
        {
            Play(item, TAKE_VOLUME);
        }

        public void Dispose()
        {
            _model.Changed -= OnChanged;
        }

        private void OnChanged(InventoryChange change)
        {
            if (change.Kind != InventoryChangeKind.ItemMoved || change.Item == null)
            {
                return;
            }

            bool fromStash = change.PreviousContainerId == _window.SecondaryContainerId;
            bool toStash = change.ContainerId == _window.SecondaryContainerId;
            bool fromPlayer = change.PreviousContainerId == _window.PrimaryContainerId || change.PreviousContainerId == _window.EquipmentContainerId;
            bool toPlayer = change.ContainerId == _window.PrimaryContainerId || change.ContainerId == _window.EquipmentContainerId;

            if (fromStash && toPlayer)
            {
                Play(change.Item, TAKE_VOLUME);
            }
            else if (fromPlayer && toStash)
            {
                Play(change.Item, PUT_IN_BOX_VOLUME);
            }
        }

        private void Play(ItemInstance item, float volume)
        {
            ItemTakeSoundComponent sound = item != null ? item.Definition.GetComponent<ItemTakeSoundComponent>() : null;
            AudioClip clip = sound != null ? sound.Pick() : null;

            if (clip == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            bool allowed = now > _lastTime + MIN_INTERVAL;
            _lastTime = now;

            if (allowed == false)
            {
                return;
            }

            LastClip = clip;
            LastVolume = volume;
            PlayedCount++;
            _window.PlayClip(clip, volume);
        }
    }
}
