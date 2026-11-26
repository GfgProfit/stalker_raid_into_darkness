using System.Collections.Generic;
using UnityEngine;

namespace Inventory.IconGeneration
{
    [DisallowMultipleComponent]
    public sealed class ItemIconRendererOverride : MonoBehaviour, IItemIconVisualConfigurator
    {
        [SerializeField] private Renderer _renderer;
        [SerializeField] private bool _hide;
        [SerializeField] private Bounds _localBounds;

        public void ConfigureIconVisual(GameObject visualRoot, ItemDefinition definition, IReadOnlyList<string> effectiveVariantIds)
        {
            if (_renderer == null || (visualRoot.hideFlags & HideFlags.HideAndDontSave) != HideFlags.HideAndDontSave)
            {
                return;
            }

            if (_hide)
            {
                _renderer.enabled = false;
                return;
            }

            _renderer.localBounds = _localBounds;
        }
    }
}
