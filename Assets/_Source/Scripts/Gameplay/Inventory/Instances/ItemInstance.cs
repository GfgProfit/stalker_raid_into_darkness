using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    public sealed class ItemInstance
    {
        private readonly Dictionary<string, string> _state = new(StringComparer.Ordinal);
        private readonly List<string> _visualVariant = new();
        private ReadOnlyDictionary<string, string> _stateView;
        private ReadOnlyCollection<string> _visualVariantView;
        private Dictionary<WeaponAddonKind, ItemInstance> _mountedWeaponAttachments;

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
        public IReadOnlyDictionary<string, string> State => _stateView ??= new ReadOnlyDictionary<string, string>(_state);
        public IReadOnlyList<string> VisualVariant => _visualVariantView ??= _visualVariant.AsReadOnly();
        public int MaxStack => Definition.MaxStack;
        public bool IsStackable => Definition.IsStackable;
        public bool IsFullStack => Amount >= MaxStack;
        public int FreeStackSpace => Mathf.Max(0, MaxStack - Amount);
        public IItemDefinitionResolver Resolver { get; internal set; }
        public float TotalWeight => UnitWeight * Amount;
        public string ContainerId => Location.ContainerId;

        internal InventoryModel Owner { get; set; }

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
            RequireDetached();
            WriteState(key, value);
        }

        internal bool CommitStateValue(InventoryModel owner, string key, string value)
        {
            if (owner == null || Owner != owner)
            {
                throw new InvalidOperationException($"Item '{InstanceId}' can only be changed by its owning inventory.");
            }

            return WriteState(key, value);
        }

        private bool WriteState(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (value == null)
            {
                return _state.Remove(key);
            }

            if (_state.TryGetValue(key, out string current) && current == value)
            {
                return false;
            }

            _state[key] = value;
            return true;
        }

        public void SetVisualVariant(IEnumerable<string> definitionIds)
        {
            RequireDetached();
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
        }

        internal ItemDefinition GetMountedWeaponAttachmentDefinition(WeaponAddonKind kind) => GetMountedWeaponAttachment(kind)?.Definition;

        internal ItemInstance CreateWeaponAttachmentDraft(WeaponAddonKind kind) => GetMountedWeaponAttachment(kind)?.CreateDraft();

        internal void SetMountedWeaponAttachment(WeaponAddonKind kind, ItemInstance attachment)
        {
            RequireDetached();
            if (attachment == null && _mountedWeaponAttachments == null)
            {
                return;
            }

            var updated = _mountedWeaponAttachments != null ? new Dictionary<WeaponAddonKind, ItemInstance>(_mountedWeaponAttachments) : new Dictionary<WeaponAddonKind, ItemInstance>();
            if (attachment == null)
            {
                updated.Remove(kind);
            }
            else
            {
                var mounted = new ItemInstance(attachment.Definition, 1, attachment.Amount == 1 ? attachment.InstanceId : null);
                mounted.CopyStateFrom(attachment);
                updated[kind] = mounted;
            }

            _mountedWeaponAttachments = updated.Count > 0 ? updated : null;
        }

        private ItemInstance GetMountedWeaponAttachment(WeaponAddonKind kind) => _mountedWeaponAttachments != null && _mountedWeaponAttachments.TryGetValue(kind, out ItemInstance attached) ? attached : null;

        internal ItemInstance CreateDraft()
        {
            var draft = new ItemInstance(Definition, Amount, InstanceId);
            draft.CopyStateFrom(this);
            return draft;
        }

        internal void CopyStateFrom(ItemInstance source)
        {
            Resolver = source.Resolver;
            _mountedWeaponAttachments = source._mountedWeaponAttachments;
            _state.Clear();

            foreach (KeyValuePair<string, string> pair in source._state)
            {
                _state[pair.Key] = pair.Value;
            }

            _visualVariant.Clear();
            _visualVariant.AddRange(source._visualVariant);
        }

        private void RequireDetached()
        {
            if (Owner != null)
            {
                throw new InvalidOperationException($"Item '{InstanceId}' belongs to an inventory. Change it through an inventory command.");
            }
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

            return HasSameMountedWeaponAttachments(other);
        }

        private bool HasSameMountedWeaponAttachments(ItemInstance other)
        {
            if (ReferenceEquals(_mountedWeaponAttachments, other._mountedWeaponAttachments))
            {
                return true;
            }

            if (_mountedWeaponAttachments == null || other._mountedWeaponAttachments == null || _mountedWeaponAttachments.Count != other._mountedWeaponAttachments.Count)
            {
                return false;
            }

            foreach (KeyValuePair<WeaponAddonKind, ItemInstance> pair in _mountedWeaponAttachments)
            {
                ItemInstance attachment = pair.Value;
                if (other._mountedWeaponAttachments.TryGetValue(pair.Key, out ItemInstance otherAttachment) == false || otherAttachment.InstanceId != attachment.InstanceId || otherAttachment.DefinitionId != attachment.DefinitionId || attachment.HasSameState(otherAttachment) == false)
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString() => $"{Definition.DisplayName} x{Amount} [{InstanceId}] @ {Location}";
    }
}
