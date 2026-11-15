using System;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    public abstract class AnomalyVisual : MonoBehaviour
    {
        [SerializeField] private ParticleSystem _idle;
        [SerializeField] private ParticleSystem _awaking;
        [SerializeField] private Light _idleIllumination;
        [SerializeField] private AnomalyLightAnimation _idleLightAnimation;
        [SerializeField, Min(0f)] private float _idleLightRange;
        [SerializeField, Min(0f)] private float _idleLightRangeJitter;
        [SerializeField] private AnomalyAudio _audio;
        [SerializeField] private AnomalyContactEffects _contactEffects;
        [SerializeField] private AnomalyContactEffects _smallContactEffects;
        [SerializeField] private bool _useSourceContactEffects;
        [SerializeField] private AnomalyContactEffects _entranceContactEffects;
        [SerializeField] private AnomalyContactEffects _idleContactEffects;
        [SerializeField] private AnomalyContactEffects _smallEntranceContactEffects;
        [SerializeField] private AnomalyContactEffects _smallIdleContactEffects;
        [SerializeField] private ParticleSystem[] _idleAuxiliarySystems;
        [SerializeField] private bool _playIdleChildren;
        [SerializeField] private bool _keepIdleDuringDischarge;

        private bool _isVisible;
        private bool _sourceActive = true;
        private bool _diagnosticFixedIdleRange;

        public bool IsVisible => _isVisible;
        public AnomalyAudio Audio => _audio;
        public abstract bool IsDischarging { get; }

        protected abstract bool HasDischarge { get; }

        public event Action DischargeStarted;
        public event Action Hidden;

        public void ConfigureDiagnosticIdleRange(bool fixedRange)
        {
            _diagnosticFixedIdleRange = fixedRange;
        }

        public void ConfigureDiagnosticIdleShadows(bool disabled)
        {
            if (disabled && _idleIllumination != null)
            {
                _idleIllumination.shadows = LightShadows.None;
            }
        }

        [ContextMenu("Visual/Show Idle")]
        public void ShowIdle()
        {
            StopDischarge();
            Clear(_awaking);
            _isVisible = isActiveAndEnabled && _sourceActive;
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
            if (isActiveAndEnabled == false || _sourceActive == false || HasDischarge == false)
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
                ClearIdle();
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
                    ClearIdle();
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
            Clear(_awaking);
            ClearIdle();
            if (_contactEffects != null)
            {
                _contactEffects.Clear();
            }
            if (_smallContactEffects != null)
            {
                _smallContactEffects.Clear();
            }
            if (_entranceContactEffects != null)
            {
                _entranceContactEffects.Clear();
            }
            if (_idleContactEffects != null)
            {
                _idleContactEffects.Clear();
            }
            if (_smallEntranceContactEffects != null)
            {
                _smallEntranceContactEffects.Clear();
            }
            if (_smallIdleContactEffects != null)
            {
                _smallIdleContactEffects.Clear();
            }
            TickVisual(0f);
            if (_audio != null)
            {
                _audio.SetVisible(false);
            }
            Hidden?.Invoke();
        }

        public void SetSourceActive(bool active)
        {
            _sourceActive = active;
            _isVisible = active && isActiveAndEnabled;
            if (_isVisible == false)
            {
                ClearIdle();
                if (_idleContactEffects != null)
                {
                    _idleContactEffects.Clear();
                }
                if (_smallIdleContactEffects != null)
                {
                    _smallIdleContactEffects.Clear();
                }
            }
            if (_audio != null)
            {
                _audio.SetVisible(_isVisible);
            }
            if (_isVisible)
            {
                EnsureIdle();
            }
        }

        public void PlayEntrance(Transform target, bool small = false)
        {
            if (_isVisible == false || isActiveAndEnabled == false || target == null)
            {
                return;
            }
            if (_audio != null)
            {
                _audio.PlayEntrance(target.position);
            }
            AnomalyContactEffects entrance = SelectContact(_entranceContactEffects != null || _useSourceContactEffects ? _entranceContactEffects : _contactEffects, _smallEntranceContactEffects, small);
            if (entrance != null)
            {
                entrance.PlayEntrance(target);
            }
            AnomalyContactEffects idle = SelectContact(_idleContactEffects, _smallIdleContactEffects, small);
            if (idle != null)
            {
                idle.PlayIdle(target);
            }
        }

        public void StopContact(Transform target, bool small = false)
        {
            AnomalyContactEffects idle = SelectContact(_idleContactEffects, _smallIdleContactEffects, small);
            if (idle != null)
            {
                idle.StopTarget(target);
            }
        }

        public void PlayHit(Transform target, bool small = false)
        {
            if (_isVisible == false || isActiveAndEnabled == false || target == null)
            {
                return;
            }
            if (_audio != null)
            {
                _audio.PlayHit(target.position);
            }
            AnomalyContactEffects contact = SelectContact(_contactEffects, _smallContactEffects, small);
            if (contact != null)
            {
                contact.PlayHit(target);
            }
        }

        public void PlayAwaking()
        {
            if (_isVisible == false || isActiveAndEnabled == false)
            {
                return;
            }
            if (_awaking != null)
            {
                Clear(_awaking);
                _awaking.Play(true);
            }
            if (_audio != null)
            {
                _audio.PlayAwaking();
            }
        }

        public void PlayAccumulate()
        {
            if (_isVisible && isActiveAndEnabled && _audio != null)
            {
                _audio.PlayAccumulate();
            }
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
            UpdateIdleLight(Time.time);
        }

        protected abstract bool StartDischarge();
        protected abstract void StopDischarge();
        protected abstract void TickVisual(float deltaTime);

        private AnomalyContactEffects SelectContact(AnomalyContactEffects large, AnomalyContactEffects small, bool isSmall)
        {
            return isSmall && (_useSourceContactEffects || small != null) ? small : large;
        }

        protected static void Clear(ParticleSystem system)
        {
            if (system != null)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ClearIdle()
        {
            if (_idleIllumination != null)
            {
                AnomalyVisual09Events.LightAssignment(_idleIllumination, false);
                _idleIllumination.enabled = false;
            }
            Clear(_idle);
            if (_idleAuxiliarySystems == null)
            {
                return;
            }
            foreach (ParticleSystem system in _idleAuxiliarySystems)
            {
                Clear(system);
            }
        }

        private void EnsureIdle()
        {
            if (_idleIllumination != null)
            {
                AnomalyVisual09Events.LightAssignment(_idleIllumination, _isVisible && isActiveAndEnabled);
                _idleIllumination.enabled = _isVisible && isActiveAndEnabled;
                UpdateIdleLight(Time.time);
            }
            if (_idle != null && _isVisible && isActiveAndEnabled && _idle.isPlaying == false)
            {
                AnomalyVisual09Events.Record(this, 2, 0, 0.0, _idle);
                _idle.Play(_playIdleChildren);
            }
        }

        private void UpdateIdleLight(float globalTime)
        {
            if (_idleIllumination == null || _idleIllumination.enabled == false)
            {
                return;
            }
            if (_idleLightAnimation != null && _idleLightAnimation.IsValid)
            {
                _idleIllumination.color = _idleLightAnimation.Evaluate(globalTime);
            }
            if (_idleLightRange > 0f)
            {
                float range = Mathf.Max(0.001f, _idleLightRange + _idleLightRangeJitter * UnityEngine.Random.Range(-1f, 1f));
                _idleIllumination.range = _diagnosticFixedIdleRange ? Mathf.Max(0.001f, _idleLightRange) : range;
            }
        }
    }
}
