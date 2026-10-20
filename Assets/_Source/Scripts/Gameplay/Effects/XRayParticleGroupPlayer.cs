using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class XRayParticleGroupPlayer : MonoBehaviour
    {
        [SerializeField] private XRayParticleGroupAsset _asset;
        [SerializeField] private XRayParticleEffectPlayer[] _effects = new XRayParticleEffectPlayer[0];

        private bool[] _playing;
        private float _time;
        private bool _follow;

        public XRayParticleGroupAsset Asset => _asset;
        public bool IsPlaying { get; private set; }
        public bool IsDeferredStop { get; private set; }
        public float CurrentTime => _time;

        public bool IsAlive
        {
            get
            {
                for (int i = 0; i < _effects.Length; i++)
                {
                    if (_effects[i] != null && _effects[i].IsPlaying)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void Setup(XRayParticleGroupAsset asset, XRayParticleEffectPlayer[] effects)
        {
            _asset = asset;
            _effects = effects ?? new XRayParticleEffectPlayer[0];
        }

        public void Play(bool follow)
        {
            if (_asset == null)
            {
                return;
            }

            Stop(false);
            _follow = follow;
            _time = 0f;
            _playing = new bool[_effects.Length];
            IsPlaying = true;
            IsDeferredStop = false;
        }

        public void Stop(bool deferred)
        {
            if (deferred)
            {
                IsDeferredStop = true;
            }
            else
            {
                IsPlaying = false;
            }

            for (int i = 0; i < _effects.Length; i++)
            {
                if (_effects[i] != null)
                {
                    _effects[i].Stop(deferred);
                }
            }
        }

        private void LateUpdate()
        {
            if (IsPlaying == false)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Advance(deltaTime);

            for (int i = 0; i < _effects.Length; i++)
            {
                if (_effects[i] != null)
                {
                    _effects[i].Tick(deltaTime);
                }
            }

            if (IsDeferredStop && IsAlive == false)
            {
                IsPlaying = false;
                IsDeferredStop = false;
            }
        }

        private void Advance(float deltaTime)
        {
            int count = Mathf.Min(_effects.Length, _asset.Entries.Count);

            for (int i = 0; i < count; i++)
            {
                XRayParticleGroupEntry entry = _asset.Entries[i];

                if (entry.IsEnabled == false || _effects[i] == null)
                {
                    continue;
                }

                if (_effects[i].IsPlaying)
                {
                    if (_time <= entry.Time1 && _time + deltaTime >= entry.Time1 && deltaTime > 0f)
                    {
                        _effects[i].Stop(entry.IsDeferredStop);
                    }
                }
                else if (IsDeferredStop == false && _playing[i] == false && _time <= entry.Time0 && _time + Mathf.Max(deltaTime, 0.0001f) >= entry.Time0)
                {
                    _playing[i] = true;
                    _effects[i].Play(_follow);
                }
            }

            _time += deltaTime;

            if (_asset.TimeLimit > 0f && _time > _asset.TimeLimit && IsDeferredStop == false)
            {
                Stop(true);
            }
        }
    }
}
