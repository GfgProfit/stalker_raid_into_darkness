using RaidIntoDarkness.Inventory.Application;
using System;
using System.Threading;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventoryItemIconBinding
    {
        private readonly Action<Sprite> _apply;

        private CancellationTokenSource _iconCancellation;
        private int _iconVersion;
        private ItemIconRequest _currentRequest;
        private bool _hasRequest;
        private IItemIconProvider _provider;

        public InventoryItemIconBinding(Action<Sprite> apply) => _apply = apply;

        public bool IsLoading { get; private set; }
        public bool HasRequest => _hasRequest;

        public void Request(ItemIconRequest request, IItemIconProvider provider, bool hasVisibleIcon)
        {
            if (_hasRequest && ReferenceEquals(provider, _provider) && SameRequest(request, _currentRequest) && (hasVisibleIcon || IsLoading))
            {
                return;
            }

            Cancel();
            var variantIds = new string[request.VariantIds.Count];

            for (int i = 0; i < variantIds.Length; i++)
            {
                variantIds[i] = request.VariantIds[i];
            }

            request = new ItemIconRequest(request.Definition, variantIds, request.Width, request.Height, request.ProfileType);
            _currentRequest = request;
            _hasRequest = true;
            _provider = provider;

            if (provider == null)
            {
                _apply(request.Definition.FallbackIcon);
                return;
            }

            if (provider.TryGetIcon(request, out Sprite cached))
            {
                _apply(cached);
                return;
            }

            _apply(request.Definition.FallbackIcon);
            IsLoading = true;
            int version = ++_iconVersion;
            _iconCancellation = new CancellationTokenSource();
            provider.RequestIcon(request, sprite => OnIconReady(version, sprite), _iconCancellation.Token);
        }

        private void OnIconReady(int version, Sprite sprite)
        {
            if (version != _iconVersion)
            {
                return;
            }

            IsLoading = false;
            _apply(sprite != null ? sprite : _currentRequest.Definition.FallbackIcon);
        }

        public void Reset()
        {
            Cancel();
            _hasRequest = false;
            _provider = null;
            _currentRequest = default;
        }

        public void Cancel()
        {
            _hasRequest &= IsLoading == false;
            IsLoading = false;
            _iconVersion++;

            if (_iconCancellation != null)
            {
                _iconCancellation.Cancel();
                _iconCancellation.Dispose();
                _iconCancellation = null;
            }
        }

        private static bool SameRequest(ItemIconRequest left, ItemIconRequest right)
        {
            if (left.Definition != right.Definition || left.Width != right.Width || left.Height != right.Height || left.ProfileType != right.ProfileType || left.VariantIds.Count != right.VariantIds.Count)
            {
                return false;
            }

            for (int i = 0; i < left.VariantIds.Count; i++)
            {
                if (left.VariantIds[i] != right.VariantIds[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
