using RaidIntoDarkness.Inventory.Application;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
    internal sealed class ItemIconCacheProvider : IItemIconProvider
    {
        private readonly ItemIconCache _cache;

        public ItemIconCacheProvider(ItemIconCache cache) => _cache = cache;

        public bool TryGetIcon(in ItemIconRequest request, out Sprite icon)
        {
            return _cache.TryGet(request.Definition, request.Width, request.Height, request.ProfileType, request.VariantIds, out icon);
        }

        public void RequestIcon(in ItemIconRequest request, Action<Sprite> onReady, CancellationToken cancellationToken)
        {
            RequestAsync(request, onReady, cancellationToken).Forget();
        }

        private async UniTaskVoid RequestAsync(ItemIconRequest request, Action<Sprite> onReady, CancellationToken cancellationToken)
        {
            try
            {
                Sprite sprite = await _cache.GetOrCreateAsync(request.Definition, request.Width, request.Height, request.ProfileType, request.VariantIds, cancellationToken);

                if (cancellationToken.IsCancellationRequested == false)
                {
                    onReady?.Invoke(sprite);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (cancellationToken.IsCancellationRequested == false)
                {
                    onReady?.Invoke(request.Definition != null ? request.Definition.FallbackIcon : null);
                }
            }
        }
    }
}
