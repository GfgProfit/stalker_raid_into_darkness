using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.IconGeneration
{
    public sealed class ItemIconCacheProvider : IItemIconProvider
    {
        public bool TryGetIcon(in ItemIconRequest request, out Sprite icon)
        {
            return ItemIconCache.TryGet(request.Definition, request.Width, request.Height, request.ProfileType, request.VariantIds, out icon);
        }

        public void RequestIcon(in ItemIconRequest request, Action<Sprite> onReady, CancellationToken cancellationToken)
        {
            RequestAsync(request, onReady, cancellationToken).Forget();
        }

        private static async UniTaskVoid RequestAsync(ItemIconRequest request, Action<Sprite> onReady, CancellationToken cancellationToken)
        {
            try
            {
                Sprite sprite = await ItemIconCache.GetOrCreateAsync(request.Definition, request.Width, request.Height, request.ProfileType, request.VariantIds, cancellationToken);

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
                onReady?.Invoke(request.Definition != null ? request.Definition.FallbackIcon : null);
            }
        }
    }
}
