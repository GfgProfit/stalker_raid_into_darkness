using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
    public sealed class ItemIconVariantPart : MonoBehaviour, IItemIconVisualConfigurator
    {
        [SerializeField] private ItemDefinition _part;
        [SerializeField] private string _group;
        [SerializeField] private GameObject[] _installedObjects = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] _defaultObjects = Array.Empty<GameObject>();
        [SerializeField] private ItemDefinition[] _requiredParts = Array.Empty<ItemDefinition>();
        [SerializeField] private ItemDefinition[] _incompatibleParts = Array.Empty<ItemDefinition>();
        [SerializeField] private Vector2Int _sizeDelta;

        public ItemDefinition Part => _part;
        public string PartId => _part == null ? string.Empty : _part.DefinitionId;
        public string Group => string.IsNullOrEmpty(_group) ? PartId : _group;
        public IReadOnlyList<ItemDefinition> RequiredParts => _requiredParts;
        public IReadOnlyList<ItemDefinition> IncompatibleParts => _incompatibleParts;
        public Vector2Int SizeDelta => _sizeDelta;

        public bool IsInstalled(IReadOnlyList<string> variantIds) => Contains(variantIds, PartId) && ConfigurationSatisfied(variantIds);

        public bool ConfigurationSatisfied(IReadOnlyList<string> variantIds) => RequirementsSatisfied(variantIds) && IncompatibilitiesSatisfied(variantIds);

        public bool RequirementsSatisfied(IReadOnlyList<string> variantIds)
        {
            bool hasRequirement = false;
            int count = _requiredParts?.Length ?? 0;

            for (int i = 0; i < count; i++)
            {
                if (_requiredParts[i] == null)
                {
                    continue;
                }

                hasRequirement = true;

                if (Contains(variantIds, _requiredParts[i].DefinitionId))
                {
                    return true;
                }
            }

            return hasRequirement == false;
        }

        public bool IncompatibilitiesSatisfied(IReadOnlyList<string> variantIds)
        {
            if (_incompatibleParts != null)
            {
                for (int i = 0; i < _incompatibleParts.Length; i++)
                {
                    if (_incompatibleParts[i] != null && Contains(variantIds, _incompatibleParts[i].DefinitionId))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public void ApplyDefaultVisualState()
        {
            SetActive(_installedObjects, false);
            SetActive(_defaultObjects, true);
        }

        public void ApplyInstalledVisualState()
        {
            SetActive(_installedObjects, true);
            SetActive(_defaultObjects, false);
        }

        public void ConfigureIconVisual(GameObject visualRoot, ItemDefinition definition, IReadOnlyList<string> effectiveVariantIds)
        {
            if (IsInstalled(effectiveVariantIds))
            {
                ApplyInstalledVisualState();
            }
            else
            {
                ApplyDefaultVisualState();
            }
        }

        public static void ApplyAll(IReadOnlyList<ItemIconVariantPart> parts, IReadOnlyList<string> variantIds)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i]?.ApplyDefaultVisualState();
            }

            for (int i = 0; i < parts.Count; i++)
            {
                ItemIconVariantPart part = parts[i];

                if (part != null && part.IsInstalled(variantIds))
                {
                    part.ApplyInstalledVisualState();
                }
            }
        }

        public void SetData(ItemDefinition part, string group, GameObject[] installedObjects, GameObject[] defaultObjects)
        {
            _part = part;
            _group = group;
            _installedObjects = installedObjects ?? Array.Empty<GameObject>();
            _defaultObjects = defaultObjects ?? Array.Empty<GameObject>();
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            if (ids == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetActive(GameObject[] objects, bool active)
        {
            if (objects == null)
            {
                return;
            }

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null && objects[i].activeSelf != active)
                {
                    objects[i].SetActive(active);
                }
            }
        }
    }
}
