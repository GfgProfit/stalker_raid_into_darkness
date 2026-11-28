using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal sealed class InventoryObservers<T> where T : Delegate
    {
        private Delegate _observers;
        private Delegate[] _snapshot = Array.Empty<Delegate>();

        public void Add(T observer) => SetObservers(Delegate.Combine(_observers, observer));
        public void Remove(T observer) => SetObservers(Delegate.Remove(_observers, observer));

        public void Publish<TNotification>(TNotification notification, Action<T, TNotification> invoke, Func<TNotification, string> describe)
        {
            Delegate[] snapshot = _snapshot;
            for (int i = 0; i < snapshot.Length; i++)
            {
                T observer = (T)snapshot[i];
                try
                {
                    invoke(observer, notification);
                }
                catch (Exception exception)
                {
                    Debug.LogException(new InvalidOperationException($"Inventory observer '{observer.Method.Name}' failed while delivering {describe(notification)}.", exception));
                }
            }
        }

        private void SetObservers(Delegate observers)
        {
            _observers = observers;
            _snapshot = observers?.GetInvocationList() ?? Array.Empty<Delegate>();
        }
    }
}
