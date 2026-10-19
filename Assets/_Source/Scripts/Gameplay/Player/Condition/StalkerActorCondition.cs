using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerActorCondition : MonoBehaviour
    {
        private const float EPS = 0.0000001f;
        private const string STAMINA_SPEED_KEY = "ASS_speed_stamina";
        private const string HILL_SPEED_KEY = "ASS_speed_hill";

        [SerializeField] private StalkerMovementProfile _profile;
        [SerializeField] private MonoBehaviour _carryWeightSource;
        [SerializeField] private MonoBehaviour _sprintSource;
        [SerializeField] private MonoBehaviour _powerLossSource;
        [SerializeField] private MonoBehaviour _healthSource;
        [SerializeField] private MonoBehaviour _satietySource;
        [Range(0.0f, 1.0f)]
        [SerializeField] private float _power = 1.0f;
        [Range(0.0f, 1.0f)]
        [SerializeField] private float _neutralHealth = 1.0f;
        [Range(0.0f, 1.0f)]
        [SerializeField] private float _neutralSatiety = 1.0f;
        [SerializeField] private float _neutralWeight;

        private readonly Dictionary<string, float> _runModifiers = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _sprintModifiers = new Dictionary<string, float>();
        private float _maxPower = 1.0f;
        private bool _cantWalk;
        private bool _cantSprint;
        private bool _limping;
        private float _modTimer;
        private float _lastPositionY;
        private bool _hasLastPosition;
        private float _runCoef;
        private float _runBackCoef;
        private float _sprintKoef;

        public float Power => _power;
        public float MaxPower => _maxPower;
        public float Health => _healthSource is IStalkerHealthSource source ? source.Health : _neutralHealth;
        public float Satiety => _satietySource is IStalkerSatietySource source ? source.Satiety : _neutralSatiety;
        public float TotalWeight => _carryWeightSource is IStalkerCarryWeightSource source ? source.TotalWeight : _neutralWeight;
        public float AdditionalWeight => _carryWeightSource is IStalkerCarryWeightSource source ? source.AdditionalWeight : 0.0f;
        public float MaxCarryWeight => _profile.Actor.MaxItemMass + AdditionalWeight;
        public float MaxWalkWeight => _profile.Condition.MaxWalkWeight + AdditionalWeight;
        public bool SprintAllowed => _sprintSource is IStalkerSprintSource source ? source.SprintAllowed : true;
        public float RunCoef => _runCoef;
        public float RunBackCoef => _runBackCoef;
        public float SprintKoef => _sprintKoef;
        public IReadOnlyDictionary<string, float> RunModifiers => _runModifiers;
        public IReadOnlyDictionary<string, float> SprintModifiers => _sprintModifiers;

        public void Initialize(StalkerMovementProfile profile)
        {
            _profile = profile;
            RecalculateSpeeds();
        }

        public void SetCarryWeightSource(MonoBehaviour source)
        {
            _carryWeightSource = source;
        }

        public void SetSprintSource(MonoBehaviour source)
        {
            _sprintSource = source;
        }

        public void SetPower(float power)
        {
            _power = Mathf.Clamp(power, 0.0f, _maxPower);
        }

        public void SetSpeedModifier(string key, float multiplier, bool sprint)
        {
            (sprint ? _sprintModifiers : _runModifiers)[key] = multiplier;
            RecalculateSpeeds();
        }

        public void RemoveSpeedModifier(string key)
        {
            _runModifiers.Remove(key);
            _sprintModifiers.Remove(key);
            RecalculateSpeeds();
        }

        public bool IsCantWalk()
        {
            StalkerConditionValues values = _profile.Condition;
            if (_power < values.CantWalkPowerBegin)
            {
                _cantWalk = true;
            }
            else if (_power > values.CantWalkPowerEnd)
            {
                _cantWalk = false;
            }

            return _cantWalk;
        }

        public bool IsCantSprint()
        {
            StalkerConditionValues values = _profile.Condition;
            if (_power < values.CantSprintPowerBegin)
            {
                _cantSprint = true;
            }
            else if (_power > values.CantSprintPowerEnd)
            {
                _cantSprint = false;
            }

            return _cantSprint;
        }

        public bool IsLimping()
        {
            StalkerConditionValues values = _profile.Condition;
            float health = Health;
            if (_power < values.LimpingPowerBegin || health < values.LimpingHealthBegin)
            {
                _limping = true;
            }
            else if (_power > values.LimpingPowerEnd && health > values.LimpingHealthEnd)
            {
                _limping = false;
            }

            return _limping;
        }

        public bool IsCantWalkWeight()
        {
            return TotalWeight > MaxWalkWeight;
        }

        public void ConditionJump()
        {
            StalkerConditionValues values = _profile.Condition;
            float weight = TotalWeight / MaxCarryWeight;
            float power = values.JumpPower + values.JumpWeightPower * weight * (weight > 1.0f ? values.OverweightJumpK : 1.0f);
            _power = Mathf.Clamp01(_power - HitPowerEffect(power));
        }

        public void Tick(StalkerMoveFlags realFlags, bool airborne, float positionY, float deltaTime)
        {
            StalkerConditionValues values = _profile.Condition;
            float gameDelta = deltaTime * values.TimeFactor;
            float baseWeight = MaxCarryWeight;
            float currentWeight = TotalWeight;
            float weight = currentWeight / baseWeight;
            if (realFlags.Has(StalkerMoveFlags.AnyMove))
            {
                ConditionWalk(weight, realFlags.IsAccelerated(), realFlags.Has(StalkerMoveFlags.Sprint), gameDelta);
            }
            else
            {
                _power = Mathf.Clamp01(_power - values.StandPower * gameDelta);
            }

            float leakFactor = 1.0f + Mathf.Min(currentWeight, baseWeight) / baseWeight + Mathf.Max(0.0f, (currentWeight - baseWeight) / 10.0f);
            _maxPower = Mathf.Clamp01(_maxPower - values.MaxPowerLeakSpeed * gameDelta * leakFactor);
            _power += values.SatietyPowerV * Satiety * gameDelta;
            _power = Mathf.Clamp(_power, 0.0f, _maxPower);
            TickStaminaMod(realFlags, airborne, positionY, deltaTime);
        }

        private void ConditionWalk(float weight, bool accelerated, bool sprint, float gameDelta)
        {
            StalkerConditionValues values = _profile.Condition;
            float power = values.WalkPower;
            if (values.ProgressiveStaminaCost)
            {
                power += values.WalkWeightPower * values.OverweightWalkK * weight;
            }
            else
            {
                power += values.WalkWeightPower * weight * (weight > 1.0f ? values.OverweightWalkK : 1.0f);
            }

            power *= gameDelta * (accelerated ? (sprint ? values.SprintK : values.AccelK) : 1.0f);
            _power = Mathf.Clamp01(_power - HitPowerEffect(power));
        }

        private float HitPowerEffect(float powerLoss)
        {
            var source = _powerLossSource as IStalkerPowerLossSource;
            float factor = (source?.OutfitPowerLoss ?? EPS) + (source?.HelmetPowerLoss ?? EPS) + (source?.BackpackPowerLoss ?? EPS);
            factor = factor / 3.0f * _profile.Condition.PowerLossFactor + _profile.Condition.PowerLossBias;
            return powerLoss * Mathf.Clamp01(factor);
        }

        private void TickStaminaMod(StalkerMoveFlags realFlags, bool airborne, float positionY, float deltaTime)
        {
            StalkerStaminaModValues values = _profile.StaminaMod;
            if (!_hasLastPosition)
            {
                _lastPositionY = positionY;
                _hasLastPosition = true;
            }

            _modTimer += deltaTime;
            while (_modTimer >= values.UpdateInterval)
            {
                _modTimer -= values.UpdateInterval;
                float difference = positionY - _lastPositionY;
                bool jumping = realFlags.Has(StalkerMoveFlags.Jump | StalkerMoveFlags.Fall) || airborne;
                if (values.EnableUphillDrain && !jumping)
                {
                    float coeff = difference > 0.0f ? values.UphillDrainCoeff : values.UphillDrainCoeff * values.UphillDrainDownhillReduction;
                    _power = Mathf.Clamp(_power - difference * coeff * values.UphillDrainModifier, 0.0f, _maxPower);
                }

                if (values.EnableUphillSpeedModifier && !jumping && realFlags.Has(StalkerMoveFlags.AnyMove))
                {
                    float coeff = difference > 0.0f ? values.UphillSpeedCoeff : values.UphillSpeedCoeff * values.UphillSpeedDownhillReduction;
                    _runModifiers[HILL_SPEED_KEY] = 1.0f - difference * coeff * values.UphillSpeedModifierModifier;
                }

                if (values.EnableStaminaSpeedModifier)
                {
                    float normalized = (_power - values.PowerSpeedMin) / (values.PowerSpeedMax - values.PowerSpeedMin);
                    _runModifiers[STAMINA_SPEED_KEY] = _power >= values.PowerSpeedMax ? 1.0f : Mathf.Pow(Mathf.Max(normalized, 0.0f), values.StaminaSpeedModifierModifier);
                }

                _lastPositionY = positionY;
                RecalculateSpeeds();
            }
        }

        private void RecalculateSpeeds()
        {
            if (_profile == null)
            {
                return;
            }

            float run = 1.0f;
            foreach (float multiplier in _runModifiers.Values)
            {
                run *= multiplier;
            }

            float sprint = 1.0f;
            foreach (float multiplier in _sprintModifiers.Values)
            {
                sprint *= multiplier;
            }

            StalkerStaminaModValues limits = _profile.StaminaMod;
            _runCoef = Mathf.Clamp(_profile.Actor.RunCoef * run, limits.SpeedCoefMin, limits.SpeedCoefMax);
            _runBackCoef = Mathf.Clamp(_profile.Actor.RunBackCoef * run, limits.SpeedCoefMin, limits.SpeedCoefMax);
            _sprintKoef = Mathf.Clamp(_profile.Actor.SprintKoef * sprint, limits.SpeedCoefMin, limits.SpeedCoefMax);
        }
    }
}
