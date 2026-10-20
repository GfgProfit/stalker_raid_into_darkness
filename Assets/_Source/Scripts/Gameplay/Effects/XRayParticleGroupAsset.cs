using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [Serializable]
    public sealed class XRayParticleGroupEntry
    {
        public const uint DEFERRED_STOP = 1u << 0;
        public const uint ENABLED = 1u << 2;

        [SerializeField] private XRayParticleEffectAsset _effect;
        [SerializeField] private float _time0;
        [SerializeField] private float _time1;
        [SerializeField] private uint _flags;

        public XRayParticleGroupEntry(XRayParticleEffectAsset effect, float time0, float time1, uint flags)
        {
            _effect = effect;
            _time0 = time0;
            _time1 = time1;
            _flags = flags;
        }

        public XRayParticleEffectAsset Effect => _effect;
        public float Time0 => _time0;
        public float Time1 => _time1;
        public uint Flags => _flags;
        public bool IsEnabled => (_flags & ENABLED) != 0;
        public bool IsDeferredStop => (_flags & DEFERRED_STOP) != 0;
    }

    public sealed class XRayParticleGroupAsset : ScriptableObject
    {
        [SerializeField] private string _groupName;
        [SerializeField] private float _timeLimit;
        [SerializeField] private List<XRayParticleGroupEntry> _entries = new List<XRayParticleGroupEntry>();

        public string GroupName => _groupName;
        public float TimeLimit => _timeLimit;
        public IReadOnlyList<XRayParticleGroupEntry> Entries => _entries;

        public void SetImportData(string groupName, float timeLimit, List<XRayParticleGroupEntry> entries)
        {
            _groupName = groupName;
            _timeLimit = timeLimit;
            _entries = entries ?? new List<XRayParticleGroupEntry>();
        }
    }
}
