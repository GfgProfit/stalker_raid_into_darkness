using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class ContinuousAnomalyVisual : AnomalyVisual
    {
        [SerializeField, Min(0f)] private float _dischargeDuration = 0.3f;

        private float _dischargeRemaining;

        public override bool IsDischarging => _dischargeRemaining > 0f;

        protected override bool HasDischarge => true;

        protected override bool StartDischarge()
        {
            _dischargeRemaining = _dischargeDuration;
            return true;
        }

        protected override void StopDischarge()
        {
            _dischargeRemaining = 0f;
        }

        protected override void TickVisual(float deltaTime)
        {
            _dischargeRemaining = Mathf.Max(0f, _dischargeRemaining - deltaTime);
        }
    }
}
