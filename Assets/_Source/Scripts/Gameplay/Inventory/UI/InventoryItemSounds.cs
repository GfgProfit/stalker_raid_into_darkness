using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;
using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class InventoryItemSounds : IDisposable
    {
        public const float TAKE_VOLUME = 1f;
        public const float PUT_IN_BOX_VOLUME = 0.75f;
        public const float MIN_INTERVAL = 0.025f;

        private readonly InventoryWindow _window;
        private readonly InventoryModel _model;
        private readonly IWorldItemInteraction _world;
        private float _lastTime = float.MinValue;

        internal InventoryItemSounds(InventoryWindow window, IWorldItemInteraction world)
        {
            _window = window;
            _model = window.Model;
            _model.Changed += OnChanged;
            _world = world;
            if (_world != null)
            {
                _world.PickupSucceeded += PlayTake;
            }
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
            if (_world != null)
            {
                _world.PickupSucceeded -= PlayTake;
            }
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
            var clips = sound?.Clips;
            AudioClip clip = clips is { Count: > 0 } ? clips[UnityEngine.Random.Range(0, clips.Count)] : null;

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
