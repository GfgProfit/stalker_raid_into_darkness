using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class AnomalyAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource _idle;
        [SerializeField] private AudioSource _blowout;
        [SerializeField] private AudioSource _entrance;
        [SerializeField] private AudioSource _hit;
        [SerializeField] private AudioSource _awaking;
        [SerializeField] private AudioSource _accumulate;
        [SerializeField, Min(0f)] private float _blowoutDelay;

        private bool _visible;
        private Vector3 _blowoutPosition;
        private Vector3 _entrancePosition;
        private Vector3 _hitPosition;
        private Vector3 _awakingPosition;
        private Vector3 _accumulatePosition;
        private float _blowoutAt;
        private bool _blowoutPending;

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_visible == false)
            {
                _blowoutPending = false;
                Stop(_awaking);
                Stop(_accumulate);
            }

            if (_idle == null)
            {
                return;
            }

            if (_visible && isActiveAndEnabled)
            {
                if (_idle.isPlaying == false && _idle.clip != null)
                {
                    _idle.Play();
                }
            }
            else
            {
                _idle.Stop();
            }
        }

        public void PlayDischarge()
        {
            SetVisible(true);
            _blowoutPosition = transform.position;
            _blowoutAt = Time.time + _blowoutDelay;
            _blowoutPending = _blowoutDelay > 0f;
            if (_blowoutPending == false)
            {
                Play(_blowout, _blowoutPosition);
            }
        }

        public void PlayAwaking()
        {
            _awakingPosition = transform.position;
            Play(_awaking, _awakingPosition);
        }

        public void PlayAccumulate()
        {
            _accumulatePosition = transform.position;
            Play(_accumulate, _accumulatePosition);
        }

        public void PlayEntrance(Vector3 position)
        {
            if (_visible == false || isActiveAndEnabled == false)
            {
                return;
            }

            _entrancePosition = position;
            Play(_entrance, _entrancePosition);
        }

        public void PlayHit(Vector3 position)
        {
            if (_visible == false || isActiveAndEnabled == false)
            {
                return;
            }

            _hitPosition = position;
            Play(_hit, _hitPosition);
        }

        private void OnEnable()
        {
            SetVisible(_visible);
        }

        private void OnDisable()
        {
            Stop(_idle);
            Stop(_blowout);
            Stop(_entrance);
            Stop(_hit);
            Stop(_awaking);
            Stop(_accumulate);
            _blowoutPending = false;
        }

        private void LateUpdate()
        {
            if (_blowoutPending && Time.time >= _blowoutAt)
            {
                _blowoutPending = false;
                Play(_blowout, _blowoutPosition);
            }
            KeepPosition(_blowout, _blowoutPosition);
            KeepPosition(_entrance, _entrancePosition);
            KeepPosition(_hit, _hitPosition);
            KeepPosition(_awaking, _awakingPosition);
            KeepPosition(_accumulate, _accumulatePosition);
        }

        private void Play(AudioSource source, Vector3 position)
        {
            if (_visible == false || isActiveAndEnabled == false || source == null || source.clip == null)
            {
                return;
            }

            source.Stop();
            source.transform.position = position;
            source.Play();
        }

        private static void KeepPosition(AudioSource source, Vector3 position)
        {
            if (source != null && source.isPlaying)
            {
                source.transform.position = position;
            }
        }

        private static void Stop(AudioSource source)
        {
            if (source != null)
            {
                source.Stop();
            }
        }
    }
}
