using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerActorCondition : MonoBehaviour, IHealthDamageReceiver
    {
        private const float EPS = 0.0000001f;
        private const string STAMINA_SPEED_KEY = "ASS_speed_stamina";
        private const string HILL_SPEED_KEY = "ASS_speed_hill";
        private const string MATERIAL_SPEED_KEY = "ASS_speed";
        private const float MATERIAL_POWER_SCALE = 0.005f;

        [SerializeField] private StalkerMovementProfile _profile;
        [SerializeField] private MonoBehaviour _carryWeightSource;
        [SerializeField] private MonoBehaviour _sprintSource;
        [SerializeField] private MonoBehaviour _powerLossSource;
        [SerializeField] private MonoBehaviour _healthSource;
        [SerializeField] private MonoBehaviour _satietySource;
        [Range(0.0f, 1.0f)]
        [FormerlySerializedAs("_power")]
        [SerializeField] private float _initialPower = 1.0f;
        [Range(0.0f, 1.0f)]
        [SerializeField] private float _neutralHealth = 1.0f;
        [Range(0.0f, 1.0f)]
        [SerializeField] private float _neutralSatiety = 1.0f;
        [SerializeField] private float _neutralWeight;

        private readonly Dictionary<string, float> _runModifiers = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _sprintModifiers = new Dictionary<string, float>();
        private readonly StalkerStat[] _stats = { new StalkerStat(), new StalkerStat(), new StalkerStat(), new StalkerStat(), new StalkerStat(), new StalkerStat() };
        private readonly (float Value, float Maximum, float Capacity)[] _publishedStats = new (float, float, float)[6];
        private GameConfig _configuration;
        private int _configurationRevision;
        private bool _statsInitialized;
        private bool _cantWalk;
        private bool _cantSprint;
        private bool _limping;
        private bool _warnedReadOnlyHealthSource;
        private float _modTimer;
        private float _lastPositionY;
        private bool _hasLastPosition;
        private float _runCoef;
        private float _runBackCoef;
        private float _sprintKoef;
        private StalkerSurfaceType _footstepSurface;

        public event System.Action<StalkerStatKind> StatChanged;

        public float Power => GetStat(StalkerStatKind.Stamina).Value;
        public float MaxPower => GetStat(StalkerStatKind.Stamina).Maximum;
        public float PowerCapacity => GetStat(StalkerStatKind.Stamina).Capacity;
        public float Health => _healthSource != null && _healthSource is IStalkerHealthSource source ? Mathf.Clamp(source.Health * HealthCapacity, 0f, MaxHealth) : GetStat(StalkerStatKind.Health).Value;
        public float MaxHealth => GetStat(StalkerStatKind.Health).Maximum;
        public float HealthCapacity => GetStat(StalkerStatKind.Health).Capacity;
        public float PsyHealth => GetStat(StalkerStatKind.PsyHealth).Value;
        public float MaxPsyHealth => GetStat(StalkerStatKind.PsyHealth).Maximum;
        public float PsyHealthCapacity => GetStat(StalkerStatKind.PsyHealth).Capacity;
        public float Hunger => GetStat(StalkerStatKind.Hunger).Value;
        public float MaxHunger => GetStat(StalkerStatKind.Hunger).Maximum;
        public float HungerCapacity => GetStat(StalkerStatKind.Hunger).Capacity;
        public float Thirst => GetStat(StalkerStatKind.Thirst).Value;
        public float MaxThirst => GetStat(StalkerStatKind.Thirst).Maximum;
        public float ThirstCapacity => GetStat(StalkerStatKind.Thirst).Capacity;
        public float Sleep => GetStat(StalkerStatKind.Sleep).Value;
        public float MaxSleep => GetStat(StalkerStatKind.Sleep).Maximum;
        public float SleepCapacity => GetStat(StalkerStatKind.Sleep).Capacity;
        public float Satiety => _satietySource != null && _satietySource is IStalkerSatietySource source ? Mathf.Clamp01(source.Satiety) : GetStat(StalkerStatKind.Hunger).NormalizedValue;
        public float TotalWeight => _carryWeightSource != null && _carryWeightSource is IStalkerCarryWeightSource source ? source.TotalWeight : _neutralWeight;
        public float AdditionalWeight => _carryWeightSource != null && _carryWeightSource is IStalkerCarryWeightSource source ? source.AdditionalWeight : 0.0f;
        public float MaxCarryWeight => _profile.Actor.MaxItemMass + AdditionalWeight;
        public float MaxWalkWeight => _profile.Condition.MaxWalkWeight + AdditionalWeight;
        public bool SprintAllowed => _sprintSource != null && _sprintSource is IStalkerSprintSource source ? source.SprintAllowed : true;
        public float RunCoef => _runCoef;
        public float RunBackCoef => _runBackCoef;
        public float SprintKoef => _sprintKoef;
        public IReadOnlyDictionary<string, float> RunModifiers => _runModifiers;
        public IReadOnlyDictionary<string, float> SprintModifiers => _sprintModifiers;

        private float NormalizedPower
        {
            get => GetStat(StalkerStatKind.Stamina).NormalizedValue;
            set => GetStat(StalkerStatKind.Stamina).SetValue(value * PowerCapacity);
        }

        private float NormalizedMaxPower
        {
            get => GetStat(StalkerStatKind.Stamina).NormalizedMaximum;
            set => GetStat(StalkerStatKind.Stamina).SetMaximum(value * PowerCapacity);
        }

        public void Initialize(StalkerMovementProfile profile)
        {
            _profile = profile;
            EnsureStats();
            RecalculateSpeeds();
        }

        public StalkerStat GetStat(StalkerStatKind kind)
        {
            EnsureStats();
            int index = (int)kind;
            if (index < 0 || index >= _stats.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(kind));
            }
            return _stats[index];
        }

        public (float Value, float Maximum, float Capacity) ReadStat(StalkerStatKind kind)
        {
            StalkerStat stat = GetStat(kind);
            return (kind == StalkerStatKind.Health ? Health : stat.Value, stat.Maximum, stat.Capacity);
        }

        public void ApplyConfiguration(GameConfig configuration)
        {
            float capacity = configuration != null ? configuration.DefaultPlayerStatCapacity : GameConfig.DEFAULT_PLAYER_STAT_CAPACITY;
            if (float.IsFinite(capacity) == false || capacity <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(configuration), "Player stat capacity must be finite and positive.");
            }
            foreach (StalkerStat stat in _stats)
            {
                stat.SetCapacity(capacity);
            }
            _configuration = configuration;
            _configurationRevision = configuration != null ? configuration.Revision : 0;
            if (_statsInitialized == false)
            {
                _stats[(int)StalkerStatKind.Stamina].SetValue(Mathf.Clamp01(_initialPower) * _stats[(int)StalkerStatKind.Stamina].Capacity);
                _stats[(int)StalkerStatKind.Health].SetValue(Mathf.Clamp01(_neutralHealth) * _stats[(int)StalkerStatKind.Health].Capacity);
                _stats[(int)StalkerStatKind.Hunger].SetValue(Mathf.Clamp01(_neutralSatiety) * _stats[(int)StalkerStatKind.Hunger].Capacity);
                _statsInitialized = true;
            }

            for (int index = 0; index < _stats.Length; index++)
            {
                PublishStat((StalkerStatKind)index);
            }
        }

        public void SetHealth(float health)
        {
            GetStat(StalkerStatKind.Health).SetValue(health);
            PublishStat(StalkerStatKind.Health);
        }

        public bool TryApplyHealthDamage(float absoluteHp)
        {
            if (!float.IsFinite(absoluteHp) || absoluteHp < 0f || !float.IsFinite(Health))
            {
                return false;
            }
            if (absoluteHp == 0f)
            {
                return true;
            }
            float remaining = Mathf.Max(0f, Health - absoluteHp);
            if (_healthSource != null && _healthSource is IStalkerHealthSource)
            {
                if (_healthSource is IStalkerMutableHealthSource mutable)
                {
                    mutable.SetHealth(remaining / HealthCapacity);
                    PublishStat(StalkerStatKind.Health);
                    return true;
                }
                if (!_warnedReadOnlyHealthSource)
                {
                    Debug.LogWarning("Anomaly damage requires the assigned health source to implement IStalkerMutableHealthSource. The authoritative health was not changed.", this);
                    _warnedReadOnlyHealthSource = true;
                }
                return false;
            }
            GetStat(StalkerStatKind.Health).SetValue(remaining);
            PublishStat(StalkerStatKind.Health);
            return true;
        }

        public void SetHealthSource(MonoBehaviour source)
        {
            _healthSource = source;
            _warnedReadOnlyHealthSource = false;
            PublishStat(StalkerStatKind.Health);
        }

        public void SetMaxHealth(float maximum)
        {
            GetStat(StalkerStatKind.Health).SetMaximum(maximum);
            PublishStat(StalkerStatKind.Health);
        }

        public void SetPsyHealth(float psyHealth)
        {
            GetStat(StalkerStatKind.PsyHealth).SetValue(psyHealth);
            PublishStat(StalkerStatKind.PsyHealth);
        }

        public void SetMaxPsyHealth(float maximum)
        {
            GetStat(StalkerStatKind.PsyHealth).SetMaximum(maximum);
            PublishStat(StalkerStatKind.PsyHealth);
        }

        public void SetMaxPower(float maximum)
        {
            GetStat(StalkerStatKind.Stamina).SetMaximum(maximum);
            PublishStat(StalkerStatKind.Stamina);
        }

        public void SetHunger(float hunger)
        {
            GetStat(StalkerStatKind.Hunger).SetValue(hunger);
            PublishStat(StalkerStatKind.Hunger);
        }

        public void SetThirst(float thirst)
        {
            GetStat(StalkerStatKind.Thirst).SetValue(thirst);
            PublishStat(StalkerStatKind.Thirst);
        }

        public void SetSleep(float sleep)
        {
            GetStat(StalkerStatKind.Sleep).SetValue(sleep);
            PublishStat(StalkerStatKind.Sleep);
        }

        private void EnsureStats()
        {
            if (_statsInitialized)
            {
                return;
            }
            ApplyConfiguration(GameConfig.Load());
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
            GetStat(StalkerStatKind.Stamina).SetValue(power);
            PublishStat(StalkerStatKind.Stamina);
        }

        public void SetFootstepSurface(StalkerSurfaceType surface)
        {
            _footstepSurface = surface;
            if (_profile.StaminaMod.EnableMaterialDrain)
            {
                _runModifiers[MATERIAL_SPEED_KEY] = GetMaterialSpeed();
            }
            else
            {
                _runModifiers.Remove(MATERIAL_SPEED_KEY);
            }

            RecalculateSpeeds();
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
            if (NormalizedPower < values.CantWalkPowerBegin)
            {
                _cantWalk = true;
            }
            else if (NormalizedPower > values.CantWalkPowerEnd)
            {
                _cantWalk = false;
            }

            return _cantWalk;
        }

        public bool IsCantSprint()
        {
            StalkerConditionValues values = _profile.Condition;
            if (NormalizedPower < values.CantSprintPowerBegin)
            {
                _cantSprint = true;
            }
            else if (NormalizedPower > values.CantSprintPowerEnd)
            {
                _cantSprint = false;
            }

            return _cantSprint;
        }

        public bool IsLimping()
        {
            StalkerConditionValues values = _profile.Condition;
            float health = Health / HealthCapacity;
            if (NormalizedPower < values.LimpingPowerBegin || health < values.LimpingHealthBegin)
            {
                _limping = true;
            }
            else if (NormalizedPower > values.LimpingPowerEnd && health > values.LimpingHealthEnd)
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
            NormalizedPower = Mathf.Clamp01(NormalizedPower - HitPowerEffect(power));
            PublishStat(StalkerStatKind.Stamina);
        }

        public void Tick(StalkerMoveFlags realFlags, bool airborne, float positionY, float deltaTime)
        {
            EnsureStats();
            if (_configuration != null && _configurationRevision != _configuration.Revision)
            {
                ApplyConfiguration(_configuration);
            }
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
                NormalizedPower = Mathf.Clamp01(NormalizedPower - values.StandPower * gameDelta);
            }

            float leakFactor = 1.0f + Mathf.Min(currentWeight, baseWeight) / baseWeight + Mathf.Max(0.0f, (currentWeight - baseWeight) / 10.0f);
            NormalizedMaxPower = Mathf.Clamp01(NormalizedMaxPower - values.MaxPowerLeakSpeed * gameDelta * leakFactor);
            NormalizedPower += values.SatietyPowerV * Satiety * gameDelta;
            NormalizedPower = Mathf.Clamp(NormalizedPower, 0.0f, NormalizedMaxPower);
            TickStaminaMod(realFlags, airborne, positionY, deltaTime);
            PublishStat(StalkerStatKind.Stamina);
            if (_healthSource != null)
            {
                PublishStat(StalkerStatKind.Health);
            }
        }

        private void PublishStat(StalkerStatKind kind)
        {
            int index = (int)kind;
            var current = ReadStat(kind);
            if (_publishedStats[index] != current)
            {
                _publishedStats[index] = current;
                StatChanged?.Invoke(kind);
            }
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
            NormalizedPower = Mathf.Clamp01(NormalizedPower - HitPowerEffect(power));
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
                    NormalizedPower = Mathf.Clamp(NormalizedPower - difference * coeff * values.UphillDrainModifier, 0.0f, NormalizedMaxPower);
                }

                if (values.EnableUphillSpeedModifier && !jumping && realFlags.Has(StalkerMoveFlags.AnyMove))
                {
                    float coeff = difference > 0.0f ? values.UphillSpeedCoeff : values.UphillSpeedCoeff * values.UphillSpeedDownhillReduction;
                    _runModifiers[HILL_SPEED_KEY] = 1.0f - difference * coeff * values.UphillSpeedModifierModifier;
                }

                if (values.EnableMaterialDrain && realFlags.Has(StalkerMoveFlags.AnyMove))
                {
                    NormalizedPower = Mathf.Clamp(NormalizedPower + GetMaterialPower() * MATERIAL_POWER_SCALE * values.MaterialDrainModifier, 0.0f, NormalizedMaxPower);
                }

                if (values.EnableStaminaSpeedModifier)
                {
                    float normalized = (NormalizedPower - values.PowerSpeedMin) / (values.PowerSpeedMax - values.PowerSpeedMin);
                    _runModifiers[STAMINA_SPEED_KEY] = NormalizedPower >= values.PowerSpeedMax ? 1.0f : Mathf.Pow(Mathf.Max(normalized, 0.0f), values.StaminaSpeedModifierModifier);
                }

                _lastPositionY = positionY;
                RecalculateSpeeds();
            }
        }

        private float GetMaterialSpeed()
        {
            switch (_footstepSurface)
            {
                case StalkerSurfaceType.Bush:
                case StalkerSurfaceType.BushSux:
                    return 0.85f;
                case StalkerSurfaceType.Water:
                    return 0.8f;
                case StalkerSurfaceType.Asphalt:
                    return 1.055f;
                case StalkerSurfaceType.Concrete:
                case StalkerSurfaceType.Stucco:
                    return 1.04f;
                case StalkerSurfaceType.Metal:
                case StalkerSurfaceType.MetalPipe:
                case StalkerSurfaceType.MetalPlate:
                case StalkerSurfaceType.Wood:
                case StalkerSurfaceType.WoodenBoard:
                case StalkerSurfaceType.FlooringTile:
                    return 1.025f;
                case StalkerSurfaceType.Earth:
                    return 0.98f;
                case StalkerSurfaceType.Grass:
                    return 0.97f;
                default:
                    return 1.0f;
            }
        }

        private float GetMaterialPower()
        {
            switch (_footstepSurface)
            {
                case StalkerSurfaceType.Bush:
                case StalkerSurfaceType.BushSux:
                    return -0.12f;
                case StalkerSurfaceType.Water:
                    return -0.15f;
                case StalkerSurfaceType.Asphalt:
                    return 0.035f;
                case StalkerSurfaceType.Concrete:
                case StalkerSurfaceType.Stucco:
                    return 0.0275f;
                case StalkerSurfaceType.Metal:
                case StalkerSurfaceType.MetalPipe:
                case StalkerSurfaceType.MetalPlate:
                case StalkerSurfaceType.Wood:
                case StalkerSurfaceType.WoodenBoard:
                case StalkerSurfaceType.FlooringTile:
                    return 0.015f;
                case StalkerSurfaceType.Grass:
                    return -0.02f;
                default:
                    return 0.0f;
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
