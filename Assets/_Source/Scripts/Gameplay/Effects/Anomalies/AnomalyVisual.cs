using System;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    public abstract class AnomalyVisual : MonoBehaviour
    {
        [SerializeField] private ParticleSystem _idle;
        [SerializeField] private AnomalyAudio _audio;
        [SerializeField] private bool _playIdleChildren;
        [SerializeField] private bool _keepIdleDuringDischarge;

        private bool _isVisible;

        public bool IsVisible => _isVisible;
        public AnomalyAudio Audio => _audio;
        public abstract bool IsDischarging { get; }

        protected abstract bool HasDischarge { get; }

        public event Action DischargeStarted;
        public event Action Hidden;

        [ContextMenu("Visual/Show Idle")]
        public void ShowIdle()
        {
            StopDischarge();
            _isVisible = isActiveAndEnabled;
            TickVisual(0f);
            if (_audio != null)
            {
                _audio.SetVisible(_isVisible);
            }
            EnsureIdle();
        }

        [ContextMenu("Visual/Play Discharge")]
        public void PlayDischarge()
        {
            if (isActiveAndEnabled == false || HasDischarge == false)
            {
                return;
            }

            bool wasVisible = _isVisible;
            _isVisible = true;
            if (_keepIdleDuringDischarge)
            {
                EnsureIdle();
            }
            else
            {
                Clear(_idle);
            }
            if (StartDischarge() == false)
            {
                _isVisible = wasVisible;
                if (_isVisible)
                {
                    EnsureIdle();
                }
                else
                {
                    Clear(_idle);
                }
                return;
            }
            if (_audio != null)
            {
                _audio.PlayDischarge();
            }
            DischargeStarted?.Invoke();
        }

        [ContextMenu("Visual/Hide")]
        public void Hide()
        {
            _isVisible = false;
            StopDischarge();
            Clear(_idle);
            TickVisual(0f);
            if (_audio != null)
            {
                _audio.SetVisible(false);
            }
            Hidden?.Invoke();
        }

        protected virtual void OnEnable()
        {
            ShowIdle();
        }

        protected virtual void OnDisable()
        {
            Hide();
        }

        protected virtual void Update()
        {
            TickVisual(Time.deltaTime);
        }

        protected abstract bool StartDischarge();
        protected abstract void StopDischarge();
        protected abstract void TickVisual(float deltaTime);

        protected static void Clear(ParticleSystem system)
        {
            if (system != null)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void EnsureIdle()
        {
            if (_idle != null && _isVisible && isActiveAndEnabled && _idle.isPlaying == false)
            {
                _idle.Play(_playIdleChildren);
            }
        }
    }
}
