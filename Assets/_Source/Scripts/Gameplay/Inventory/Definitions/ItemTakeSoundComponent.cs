using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    [Serializable]
    public sealed class ItemTakeSoundComponent : ItemDefinitionComponent
    {
        [Tooltip("Section key snd_on_take (items\\settings\\item_sounds).")]
        [SerializeField] private string _category;
        [SerializeField] private List<AudioClip> _clips = new List<AudioClip>();

        public string Category => _category;
        public IReadOnlyList<AudioClip> Clips => _clips;
    }
}
