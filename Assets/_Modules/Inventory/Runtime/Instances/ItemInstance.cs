using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    public readonly struct ItemLocation : IEquatable<ItemLocation>
    {
        public ItemLocation(string containerId, Vector2Int position, bool rotated, string slotId)
        {
            ContainerId = containerId ?? string.Empty;
            Position = position;
            Rotated = rotated;
            SlotId = slotId ?? string.Empty;
        }

        public string ContainerId { get; }
        public Vector2Int Position { get; }
        public bool Rotated { get; }
        public string SlotId { get; }
        public bool IsPlaced => string.IsNullOrEmpty(ContainerId) == false;

        public static ItemLocation None => new(string.Empty, Vector2Int.zero, false, string.Empty);

        public bool Equals(ItemLocation other) => ContainerId == other.ContainerId && Position == other.Position && Rotated == other.Rotated && SlotId == other.SlotId;
        public override bool Equals(object obj) => obj is ItemLocation other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ContainerId, Position, Rotated, SlotId);
        public override string ToString() => IsPlaced ? $"{ContainerId}{(SlotId.Length > 0 ? "/" + SlotId : string.Empty)} {Position}{(Rotated ? " R" : string.Empty)}" : "none";
    }

    public sealed class ItemInstance
    {
        private readonly Dictionary<string, string> _state = new(StringComparer.Ordinal);
        private readonly List<string> _visualVariant = new();

        public ItemInstance(ItemDefinition definition, int amount, string instanceId = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            DefinitionId = definition.DefinitionId;
            InstanceId = string.IsNullOrEmpty(instanceId) ? Guid.NewGuid().ToString("N") : instanceId;
            Amount = Mathf.Clamp(amount, 1, definition.MaxStack);
            Location = ItemLocation.None;
        }

        public string InstanceId { get; }
        public string DefinitionId { get; }
        public ItemDefinition Definition { get; }
        public int Amount { get; internal set; }
        public ItemLocation Location { get; internal set; }
        public IReadOnlyDictionary<string, string> State => _state;
        public IReadOnlyList<string> VisualVariant => _visualVariant;
        public int MaxStack => Definition.MaxStack;
        public bool IsStackable => Definition.IsStackable;
        public bool IsFullStack => Amount >= MaxStack;
        public int FreeStackSpace => Mathf.Max(0, MaxStack - Amount);
        public IItemDefinitionResolver Resolver { get; internal set; }
        public float TotalWeight => UnitWeight * Amount;
        public string ContainerId => Location.ContainerId;

        public float UnitWeight
        {
            get
            {
                float weight = Definition.Weight;

                if (Definition.Components != null)
                {
                    for (int i = 0; i < Definition.Components.Count; i++)
                    {
                        weight += Definition.Components[i]?.GetAdditionalWeight(this, Resolver) ?? 0f;
                    }
                }

                return Mathf.Max(0f, weight);
            }
        }

        public event Action<ItemInstance> StateChanged;

        public Vector2Int GetSize(bool rotated)
        {
            Vector2Int size = Definition.Size;

            for (int i = 0; i < Definition.Components.Count; i++)
            {
                size += Definition.Components[i]?.GetSizeDelta(this, Resolver) ?? Vector2Int.zero;
            }

            size = Vector2Int.Max(Vector2Int.one, size);
            return rotated ? new Vector2Int(size.y, size.x) : size;
        }

        public bool CanStackWith(ItemInstance other) => other != null && other != this && IsStackable && other.DefinitionId == DefinitionId && HasSameState(other);

        public string GetState(string key, string fallback = null) => _state.TryGetValue(key, out string value) ? value : fallback;

        public void SetState(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (value == null)
            {
                if (_state.Remove(key))
                {
                    StateChanged?.Invoke(this);
                }

                return;
            }

            if (_state.TryGetValue(key, out string current) && current == value)
            {
                return;
            }

            _state[key] = value;
            StateChanged?.Invoke(this);
        }

        public void SetVisualVariant(IEnumerable<string> definitionIds)
        {
            _visualVariant.Clear();

            if (definitionIds != null)
            {
                foreach (string definitionId in definitionIds)
                {
                    if (string.IsNullOrEmpty(definitionId) == false && _visualVariant.Contains(definitionId) == false)
                    {
                        _visualVariant.Add(definitionId);
                    }
                }
            }

            _visualVariant.Sort(StringComparer.Ordinal);
            StateChanged?.Invoke(this);
        }

        internal void CopyStateFrom(ItemInstance source)
        {
            Resolver = source.Resolver;
            _state.Clear();

            foreach (KeyValuePair<string, string> pair in source._state)
            {
                _state[pair.Key] = pair.Value;
            }

            _visualVariant.Clear();
            _visualVariant.AddRange(source._visualVariant);
        }

        private bool HasSameState(ItemInstance other)
        {
            if (_state.Count != other._state.Count || _visualVariant.Count != other._visualVariant.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, string> pair in _state)
            {
                if (other._state.TryGetValue(pair.Key, out string value) == false || value != pair.Value)
                {
                    return false;
                }
            }

            for (int i = 0; i < _visualVariant.Count; i++)
            {
                if (_visualVariant[i] != other._visualVariant[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString() => $"{Definition.DisplayName} x{Amount} [{InstanceId}] @ {Location}";
    }
}
