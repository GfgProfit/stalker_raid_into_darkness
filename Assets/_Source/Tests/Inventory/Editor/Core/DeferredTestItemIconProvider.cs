using System;
using System.Threading;
using RaidIntoDarkness.Inventory.Application;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Core.Tests
{
    internal sealed class DeferredTestItemIconProvider : IItemIconProvider
    {
        private Action<Sprite> _completion;

        public CancellationToken Cancellation { get; private set; }

        public bool TryGetIcon(in ItemIconRequest request, out Sprite icon)
        {
            icon = null;
            return false;
        }

        public void RequestIcon(in ItemIconRequest request, Action<Sprite> onReady, CancellationToken cancellationToken)
        {
            _completion = onReady;
            Cancellation = cancellationToken;
        }

        public void Complete(Sprite sprite) => _completion(sprite);
    }
}

