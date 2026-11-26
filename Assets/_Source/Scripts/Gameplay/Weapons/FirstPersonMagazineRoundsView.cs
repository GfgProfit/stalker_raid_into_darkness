using System;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    public sealed class FirstPersonMagazineRoundsView : MonoBehaviour
    {
        [SerializeField] private Transform[] _bones = Array.Empty<Transform>();
        [SerializeField] private int[] _visibleByCount = Array.Empty<int>();
        [SerializeField] [Range(0f, 1f)] private float _tacticalExchange;
        [SerializeField] [Range(0f, 1f)] private float _emptyExchange;

        internal void Refresh(int loaded, bool reloading, bool empty, float reloadProgress, int incoming)
        {
            if (_visibleByCount.Length == 0)
            {
                return;
            }

            float exchange = empty ? _emptyExchange : _tacticalExchange;
            int displayed = reloading && reloadProgress >= exchange ? incoming : loaded;
            int mask = _visibleByCount[Mathf.Clamp(displayed, 0, _visibleByCount.Length - 1)];
            for (int i = 0; i < _bones.Length; i++)
            {
                Vector3 scale = (mask & (1 << i)) != 0 ? Vector3.one : Vector3.zero;
                if (_bones[i].localScale != scale)
                {
                    _bones[i].localScale = scale;
                }
            }
        }
    }
}
