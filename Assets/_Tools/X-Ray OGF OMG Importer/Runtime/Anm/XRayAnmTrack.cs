using System;
using UnityEngine;

namespace XRay.Importer
{
    [Serializable]
    public sealed class XRayAnmTrack
    {
        [SerializeField] private XRayAnmAsset _asset;
        [SerializeField] [Range(0.0f, 1.0f)] private float _weight = 1.0f;
        [SerializeField] private float _speed = 1.0f;
        [SerializeField] private bool _loop;
        [SerializeField] private float _startTime;

        private float _time;
        private bool _isPlaying;
        private bool _isPaused;

        public XRayAnmTrack()
        {
        }

        public XRayAnmTrack(XRayAnmAsset asset, float weight, float speed, bool loop, float startTime)
        {
            _asset = asset;
            _weight = weight;
            _speed = speed;
            _loop = loop;
            _startTime = startTime;
        }

        public XRayAnmAsset Asset
        {
            get => _asset;
            set => _asset = value;
        }

        public float Weight
        {
            get => _weight;
            set => _weight = Mathf.Clamp01(value);
        }

        public float Speed
        {
            get => _speed;
            set => _speed = value;
        }

        public bool Loop
        {
            get => _loop;
            set => _loop = value;
        }

        public float StartTime
        {
            get => _startTime;
            set => _startTime = value;
        }

        public float Time => _time;
        public bool IsPlaying => _isPlaying;
        public bool IsPaused => _isPaused;
        public bool IsActive => _isPlaying && _asset != null;
        public float Duration => _asset != null ? _asset.Duration : 0.0f;

        public void Play(float startTime)
        {
            _time = startTime;
            _isPlaying = true;
            _isPaused = false;
            Clamp();
        }

        public void Stop()
        {
            _isPlaying = false;
            _isPaused = false;
            _time = 0.0f;
        }

        public void Pause()
        {
            _isPaused = _isPlaying;
        }

        public void Resume()
        {
            _isPaused = false;
        }

        public void Seek(float time)
        {
            _time = time;
            Clamp();
        }

        public bool Advance(float deltaTime)
        {
            if (!_isPlaying || _isPaused)
            {
                return false;
            }

            _time += deltaTime * _speed;
            if (_loop)
            {
                Clamp();
                return false;
            }

            float duration = Duration;
            if (_time >= duration || _time < 0.0f)
            {
                _time = Mathf.Clamp(_time, 0.0f, duration);
                return true;
            }

            return false;
        }

        public bool Evaluate(out Vector3 position, out Quaternion rotation)
        {
            if (_asset == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            return _asset.Evaluate(_time, out position, out rotation);
        }

        private void Clamp()
        {
            float duration = Duration;
            if (duration <= 0.0f)
            {
                _time = 0.0f;
                return;
            }

            _time = _loop ? Mathf.Repeat(_time, duration) : Mathf.Clamp(_time, 0.0f, duration);
        }
    }
}
