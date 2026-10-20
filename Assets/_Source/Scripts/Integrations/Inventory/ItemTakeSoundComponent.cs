using System;
using System.Collections.Generic;
using Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [Serializable]
    public sealed class ItemTakeSoundComponent : ItemDefinitionComponent
    {
        [Tooltip("Section key snd_on_take (items\\settings\\item_sounds).")]
        [SerializeField] private string _category;
        [SerializeField] private List<AudioClip> _clips = new List<AudioClip>();

        public string Category => _category;
        public IReadOnlyList<AudioClip> Clips => _clips;

        public AudioClip Pick()
        {
            return _clips != null && _clips.Count > 0 ? _clips[UnityEngine.Random.Range(0, _clips.Count)] : null;
        }
    }
}
