using System.Collections.Generic;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AnomalyPlayerReaction))]
    public sealed class AnomalyDamageSource : MonoBehaviour, IXRayAnomalyDamageSource
    {
        private const double TIME_EPSILON = 0.000001d;

        private static uint _nextGeneration;
        private static int _nextSourceId;

        [SerializeField] private AnomalyVariantConfig _variant;
        [SerializeField] private string _sourceSection;

        private readonly List<StalkerPlayerController> _players = new List<StalkerPlayerController>();
        private readonly Dictionary<StalkerPlayerController, AnomalyDamageReceiver> _receivers = new Dictionary<StalkerPlayerController, AnomalyDamageReceiver>();
        private readonly List<StalkerPlayerController> _expiredPlayers = new List<StalkerPlayerController>();

        private AnomalyPlayerReaction _reaction;
        private AnomalyBalanceConfig _balance;
        private uint _generation;
        private int _sourceId;
        private ulong _sequence;
        private double _elapsed;

        public AnomalyVariantConfig Variant => _variant;

        public void ConfigureDamage(AnomalyVariantConfig variant)
        {
            _variant = variant;
            _sourceSection = variant != null ? variant.SourceSection : string.Empty;
            _elapsed = 0f;
        }

        private void Awake()
        {
            _reaction = GetComponent<AnomalyPlayerReaction>();
        }

        private void OnEnable()
        {
            _generation = ++_nextGeneration;
            if (_sourceId == 0)
            {
                _sourceId = ++_nextSourceId;
            }
            _sequence = 0;
            _elapsed = 0f;
            if (_reaction == null)
            {
                _reaction = GetComponent<AnomalyPlayerReaction>();
            }
            _reaction.HitConfirmed += OnHitConfirmed;
            GameConfig config = GameConfig.Load();
            _balance = config != null ? config.AnomalyBalance : null;
            if (_balance != null && string.IsNullOrEmpty(_sourceSection) == false)
            {
                _variant = _balance.FindVariant(_sourceSection);
            }
        }

        private void OnDisable()
        {
            if (_reaction != null)
            {
                _reaction.HitConfirmed -= OnHitConfirmed;
            }
            _receivers.Clear();
            _expiredPlayers.Clear();
            _players.Clear();
            _elapsed = 0f;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Tick(float deltaTime)
        {
            PruneReceivers();
            if (!isActiveAndEnabled || _variant == null || _balance == null || !_variant.Continuous || !float.IsFinite(deltaTime) || deltaTime <= 0f)
            {
                return;
            }
            _reaction.CollectActivePlayers(_players);
            if (_players.Count == 0)
            {
                _elapsed = 0f;
                return;
            }
            float interval = _variant.HitInterval;
            if (!float.IsFinite(interval) || interval <= 0f)
            {
                return;
            }
            _elapsed += deltaTime;
            while (_elapsed + TIME_EPSILON >= interval)
            {
                _elapsed = System.Math.Max(0d, _elapsed - interval);
                var hit = new AnomalyHitEvent(_sourceId, _generation, ++_sequence);
                for (int i = 0; i < _players.Count; i++)
                {
                    ApplyHit(_players[i], hit);
                }
            }
        }

        private void OnHitConfirmed(StalkerPlayerController player, ulong sequence)
        {
            if (!isActiveAndEnabled || _variant == null || _balance == null || _variant.Continuous)
            {
                return;
            }
            ApplyHit(player, new AnomalyHitEvent(_sourceId, _generation, sequence));
        }

        private void ApplyHit(StalkerPlayerController player, AnomalyHitEvent hit)
        {
            if (!_receivers.TryGetValue(player, out AnomalyDamageReceiver receiver) || receiver == null)
            {
                receiver = player.GetComponent<AnomalyDamageReceiver>();
                _receivers[player] = receiver;
            }
            if (receiver != null)
            {
                receiver.ApplyHit(hit, _variant, _balance);
            }
        }

        private void PruneReceivers()
        {
            _expiredPlayers.Clear();
            foreach (KeyValuePair<StalkerPlayerController, AnomalyDamageReceiver> entry in _receivers)
            {
                if (entry.Key == null || entry.Value == null)
                {
                    _expiredPlayers.Add(entry.Key);
                }
            }
            for (int i = 0; i < _expiredPlayers.Count; i++)
            {
                _receivers.Remove(_expiredPlayers[i]);
            }
        }
    }
}
