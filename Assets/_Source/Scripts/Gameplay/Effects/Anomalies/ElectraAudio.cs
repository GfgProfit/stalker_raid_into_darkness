using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class ElectraAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource _idle;
        [SerializeField] private AudioSource _blowout;
        [SerializeField] private AudioSource _entrance;
        [SerializeField] private AudioSource _hit;

        private bool _visible;
        private Vector3 _blowoutPosition;
        private Vector3 _entrancePosition;
        private Vector3 _hitPosition;

        public void SetVisible(bool visible)
        {
            _visible = visible;

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
            Play(_blowout, _blowoutPosition);
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
        }

        private void LateUpdate()
        {
            KeepPosition(_blowout, _blowoutPosition);
            KeepPosition(_entrance, _entrancePosition);
            KeepPosition(_hit, _hitPosition);
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
