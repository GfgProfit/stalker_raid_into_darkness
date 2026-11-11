using System;
using System.Collections.Generic;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    public readonly struct AnomalyDamageResult
    {
        public AnomalyDamageResult(AnomalyHitEvent hit, AnomalyVariantConfig profile, float inputDamage, float protection, float appliedDamage)
        {
            Hit = hit;
            SourceSection = profile.SourceSection;
            DamageType = profile.Archetype.DamageType;
            Strength = profile.Strength;
            InputDamage = inputDamage;
            Protection = protection;
            AppliedDamage = appliedDamage;
        }

        public AnomalyHitEvent Hit { get; }
        public string SourceSection { get; }
        public AnomalyDamageType DamageType { get; }
        public AnomalyStrength Strength { get; }
        public float InputDamage { get; }
        public float Protection { get; }
        public float AppliedDamage { get; }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(StalkerActorCondition))]
    public sealed class AnomalyDamageReceiver : MonoBehaviour
    {
        [SerializeField] private StalkerActorCondition _condition;
        [SerializeField] private MonoBehaviour[] _protectionSources = Array.Empty<MonoBehaviour>();
        [SerializeField] private MonoBehaviour _authoritySource;

        private readonly Dictionary<int, AnomalyHitEvent> _latestHits = new Dictionary<int, AnomalyHitEvent>();
        private readonly List<MonoBehaviour> _resolvedProtectionSources = new List<MonoBehaviour>();
        private readonly List<MonoBehaviour> _registeredProtectionSources = new List<MonoBehaviour>();
        private readonly List<AnomalyEquipmentWearTarget> _wearTargets = new List<AnomalyEquipmentWearTarget>();
        private readonly List<float> _wearConditions = new List<float>();
        private readonly HashSet<string> _wearItemIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly AnomalyRadiationState _radiation = new AnomalyRadiationState();
        private GameConfig _configuration;
        private AnomalyBalanceConfig _balance;
        private bool _initialized;
        private bool _warnedInvalidProtection;
        private bool _isApplyingHit;

        public float Radiation => _radiation.Level;

        public event Action<AnomalyDamageResult> HitApplied;

        private void Awake()
        {
            EnsureInitialized();
            _configuration = GameConfig.Load();
        }

        private void Update()
        {
            AnomalyBalanceConfig balance = _configuration != null ? _configuration.AnomalyBalance : _balance;
            if (balance != null)
            {
                Tick(Time.deltaTime, balance);
            }
        }

        public void Initialize(StalkerActorCondition condition, params MonoBehaviour[] protectionSources)
        {
            _condition = condition;
            _protectionSources = protectionSources ?? Array.Empty<MonoBehaviour>();
            _initialized = false;
            EnsureInitialized();
        }

        public void RegisterProtectionSource(MonoBehaviour source)
        {
            if (source == null || !(source is IAnomalyProtectionSource) && !(source is IAnomalyEquipmentWearSource))
            {
                return;
            }
            if (!_registeredProtectionSources.Contains(source))
            {
                _registeredProtectionSources.Add(source);
            }
            EnsureInitialized();
            if (!_resolvedProtectionSources.Contains(source))
            {
                _resolvedProtectionSources.Add(source);
            }
        }

        public void UnregisterProtectionSource(MonoBehaviour source)
        {
            _registeredProtectionSources.Remove(source);
            _resolvedProtectionSources.Remove(source);
        }

        public void SetAuthoritySource(MonoBehaviour source)
        {
            _authoritySource = source;
        }

        public bool ApplyHit(AnomalyHitEvent hit, AnomalyVariantConfig profile, AnomalyBalanceConfig config)
        {
            EnsureInitialized();
            if (_isApplyingHit || !CanApplyDamage() || _condition == null || profile == null || config == null || !profile.TryValidate(out _) || !HasValidRates(config) || profile.Archetype.DamageType == AnomalyDamageType.None || profile.InputDamage <= 0f || IsRepeated(hit))
            {
                return false;
            }
            _isApplyingHit = true;
            AnomalyDamageResult result;
            try
            {
                CaptureEquipmentWear(profile.Archetype.DamageType, config, profile.Archetype.EquipmentWearPerHitPercent);
                float inputDamage = profile.InputDamage;
                float protection = GetProtection(profile.Archetype.DamageType, profile.Strength, config);
                float damage = inputDamage * (1f - protection);
                CalculateEquipmentWear(profile.Archetype.EquipmentWearPerHitPercent);
                if (!CanApplyDamage())
                {
                    return false;
                }
                float appliedDamage;
                switch (profile.Archetype.DamageType)
                {
                    case AnomalyDamageType.Radiation:
                        float previousRadiation = Radiation;
                        _radiation.AddDosePoints(damage);
                        appliedDamage = (Radiation - previousRadiation) * 100f;
                        break;
                    case AnomalyDamageType.Telepatic:
                        float previousPsyHealth = _condition.PsyHealth;
                        _condition.SetPsyHealth(Mathf.Max(0f, previousPsyHealth - damage));
                        appliedDamage = previousPsyHealth - _condition.PsyHealth;
                        break;
                    default:
                        float previousHealth = _condition.Health;
                        if (!_condition.TryApplyHealthDamage(damage))
                        {
                            return false;
                        }
                        appliedDamage = previousHealth - _condition.Health;
                        break;
                }
                _balance = config;
                _latestHits[hit.SourceId] = hit;
                result = new AnomalyDamageResult(hit, profile, inputDamage, protection, appliedDamage);
                for (int i = 0; i < _wearTargets.Count; i++)
                {
                    if (!CanApplyDamage())
                    {
                        break;
                    }
                    AnomalyEquipmentWearTarget target = _wearTargets[i];
                    target.Source.ApplyEquipmentWear(target, _wearConditions[i]);
                }
            }
            finally
            {
                _wearTargets.Clear();
                _wearConditions.Clear();
                _wearItemIds.Clear();
                _isApplyingHit = false;
            }
            HitApplied?.Invoke(result);
            return true;
        }

        private void CaptureEquipmentWear(AnomalyDamageType type, AnomalyBalanceConfig config, float baseWearPercent)
        {
            if (baseWearPercent <= 0f)
            {
                return;
            }
            foreach (MonoBehaviour source in _resolvedProtectionSources)
            {
                if (source != null && source is IAnomalyEquipmentWearSource provider)
                {
                    provider.CaptureEquipmentWear(type, config, _wearTargets);
                }
            }
        }

        private void CalculateEquipmentWear(float baseWearPercent)
        {
            int count = 0;
            for (int i = 0; i < _wearTargets.Count; i++)
            {
                AnomalyEquipmentWearTarget target = _wearTargets[i];
                if (target.Source == null || string.IsNullOrEmpty(target.ItemId) || !AnomalyEquipmentWearMath.TryCalculateRemainingCondition(target.Condition, baseWearPercent, target.AnomalyDurability, out float remainingCondition) || !_wearItemIds.Add(target.ItemId))
                {
                    continue;
                }
                _wearTargets[count++] = target;
                _wearConditions.Add(remainingCondition);
            }
            _wearTargets.RemoveRange(count, _wearTargets.Count - count);
        }

        private bool CanApplyDamage()
        {
            if (ReferenceEquals(_authoritySource, null))
            {
                return true;
            }
            return _authoritySource != null && _authoritySource.isActiveAndEnabled && _authoritySource is IAnomalyDamageAuthority authority && authority.IsAuthoritative;
        }

        public float GetProtection(AnomalyDamageType type, AnomalyStrength strength, AnomalyBalanceConfig config)
        {
            EnsureInitialized();
            RemoveDestroyedSources(_registeredProtectionSources);
            RemoveDestroyedSources(_resolvedProtectionSources);
            float remaining = 1f;
            foreach (MonoBehaviour source in _resolvedProtectionSources)
            {
                if (source != null && source is IAnomalyProtectionSource provider)
                {
                    float protection = provider.GetAnomalyProtection(type, strength, config);
                    if (!float.IsFinite(protection))
                    {
                        if (!_warnedInvalidProtection)
                        {
                            Debug.LogWarning("Anomaly protection sources must return a finite fraction. The invalid protection value was ignored.", source);
                            _warnedInvalidProtection = true;
                        }
                        continue;
                    }
                    remaining *= 1f - Mathf.Clamp01(protection);
                }
            }
            return 1f - remaining;
        }

        private static void RemoveDestroyedSources(List<MonoBehaviour> sources)
        {
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                if (sources[i] == null)
                {
                    sources.RemoveAt(i);
                }
            }
        }

        public void Tick(float deltaTime, AnomalyBalanceConfig config)
        {
            EnsureInitialized();
            if (_isApplyingHit || !CanApplyDamage() || _condition == null || config == null || !HasValidRates(config) || !float.IsFinite(deltaTime) || deltaTime <= 0f)
            {
                return;
            }
            float damage = _radiation.Tick(deltaTime, config.RadiationDecayRate, config.RadiationHealthRate);
            if (damage > 0f)
            {
                _condition.TryApplyHealthDamage(damage);
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }
            if (_condition == null)
            {
                _condition = GetComponent<StalkerActorCondition>();
            }
            _resolvedProtectionSources.Clear();
            MonoBehaviour[] sources = _protectionSources != null && _protectionSources.Length > 0 ? _protectionSources : GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour source in sources)
            {
                if (source != null && (source is IAnomalyProtectionSource || source is IAnomalyEquipmentWearSource) && !_resolvedProtectionSources.Contains(source))
                {
                    _resolvedProtectionSources.Add(source);
                }
            }
            foreach (MonoBehaviour source in _registeredProtectionSources)
            {
                if (source != null && !_resolvedProtectionSources.Contains(source))
                {
                    _resolvedProtectionSources.Add(source);
                }
            }
            _initialized = true;
        }

        private bool IsRepeated(AnomalyHitEvent hit)
        {
            if (!_latestHits.TryGetValue(hit.SourceId, out AnomalyHitEvent previous))
            {
                return false;
            }
            int generationDifference = unchecked((int)(hit.Generation - previous.Generation));
            return generationDifference < 0 || generationDifference == 0 && hit.Sequence <= previous.Sequence;
        }

        private static bool HasValidRates(AnomalyBalanceConfig config)
        {
            return float.IsFinite(config.RadiationDecayRate) && config.RadiationDecayRate >= 0f && float.IsFinite(config.RadiationHealthRate) && config.RadiationHealthRate >= 0f;
        }
    }
}
