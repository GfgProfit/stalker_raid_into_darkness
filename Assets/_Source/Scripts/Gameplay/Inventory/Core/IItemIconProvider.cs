using System;
using System.Threading;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    public interface IItemIconProvider
    {
        bool TryGetIcon(in ItemIconRequest request, out Sprite icon);
        void RequestIcon(in ItemIconRequest request, Action<Sprite> onReady, CancellationToken cancellationToken);
    }
}
