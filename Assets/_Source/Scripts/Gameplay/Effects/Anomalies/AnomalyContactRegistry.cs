using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class AnomalyContactRegistry : MonoBehaviour
    {
        private readonly List<Registration> _registrations = new List<Registration>();
        private int _nextToken;

        public bool TryGetOwner(string key, Transform anchor, out AnomalyContactEffects owner)
        {
            int index = Find(key, anchor);
            owner = index >= 0 ? _registrations[index].Owner : null;
            return owner != null;
        }

        public bool IsPlaying(string key, Transform anchor)
        {
            int index = Find(key, anchor);
            if (index < 0)
            {
                return false;
            }
            Registration registration = _registrations[index];
            if (registration.Owner.IsRegistrationPlaying(this, registration.Token))
            {
                return true;
            }
            Forget(registration.Token, registration.Owner);
            return false;
        }

        public void Stop(string key)
        {
            for (int index = _registrations.Count - 1; index >= 0; index--)
            {
                Registration registration = _registrations[index];
                if (string.Equals(registration.Key, key, StringComparison.Ordinal))
                {
                    _registrations.RemoveAt(index);
                    if (registration.Owner != null)
                    {
                        registration.Owner.StopRegistration(this, registration.Token);
                    }
                }
            }
        }

        internal int Register(string key, Transform anchor, AnomalyContactEffects owner)
        {
            if (isActiveAndEnabled == false || string.IsNullOrEmpty(key) || anchor == null || anchor.IsChildOf(transform) == false || owner == null || Find(key, anchor) >= 0)
            {
                return 0;
            }
            int token = unchecked(++_nextToken);
            if (token == 0)
            {
                token = unchecked(++_nextToken);
            }
            _registrations.Add(new Registration(token, key, anchor, owner));
            return token;
        }

        internal void Forget(int token, AnomalyContactEffects owner)
        {
            for (int index = _registrations.Count - 1; index >= 0; index--)
            {
                Registration registration = _registrations[index];
                if (registration.Token == token && registration.Owner == owner)
                {
                    _registrations.RemoveAt(index);
                    return;
                }
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            Clear();
        }

        private void Clear()
        {
            while (_registrations.Count > 0)
            {
                int index = _registrations.Count - 1;
                Registration registration = _registrations[index];
                _registrations.RemoveAt(index);
                if (registration.Owner != null)
                {
                    registration.Owner.StopRegistration(this, registration.Token);
                }
            }
        }

        private int Find(string key, Transform anchor)
        {
            for (int index = _registrations.Count - 1; index >= 0; index--)
            {
                Registration registration = _registrations[index];
                if (registration.Owner == null || registration.Owner.isActiveAndEnabled == false || registration.Anchor == null)
                {
                    _registrations.RemoveAt(index);
                }
                else if (registration.Anchor == anchor && string.Equals(registration.Key, key, StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return -1;
        }

        private readonly struct Registration
        {
            public int Token { get; }
            public string Key { get; }
            public Transform Anchor { get; }
            public AnomalyContactEffects Owner { get; }

            public Registration(int token, string key, Transform anchor, AnomalyContactEffects owner)
            {
                Token = token;
                Key = key;
                Anchor = anchor;
                Owner = owner;
            }
        }
    }
}
