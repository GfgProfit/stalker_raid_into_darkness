using RaidIntoDarkness.Inventory.Domain;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory.Infrastructure.IconGeneration;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    [DisallowMultipleComponent]
    public sealed class WeaponScopeVisuals : MonoBehaviour, IItemIconVisualConfigurator
    {
        public const string SCOPE_GROUP = "scope";

        [SerializeField] private GameObject _defaultScope;

        public GameObject DefaultScope => _defaultScope;

        public void ConfigureIconVisual(GameObject visualRoot, ItemDefinition definition, IReadOnlyList<string> effectiveVariantIds)
        {
            if (_defaultScope == null)
            {
                return;
            }

            bool installed = false;

            foreach (ItemIconVariantPart part in visualRoot.GetComponentsInChildren<ItemIconVariantPart>(true))
            {
                if (part.Group == SCOPE_GROUP && part.IsInstalled(effectiveVariantIds) && part.gameObject != _defaultScope && part.transform.IsChildOf(_defaultScope.transform) == false && _defaultScope.transform.IsChildOf(part.transform) == false)
                {
                    installed = true;
                    break;
                }
            }

            _defaultScope.SetActive(installed == false);
        }
    }
}
