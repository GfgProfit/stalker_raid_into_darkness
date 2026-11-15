using System;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    public sealed class AnomalyParticlePlayback
    {
        private readonly ParticleSystem[] _systems;
        private readonly int _rootIndex;
        private readonly int[] _stopIndices;
        private readonly float[] _stopTimes;
        private readonly float _duration;
        private readonly bool _bindingsValid;

        private float _elapsed;
        private float _delayRemaining;
        private bool _playing;
        private bool _started;

        public GameObject Root { get; }
        public bool IsValid => Root != null && _bindingsValid && _systems[_rootIndex] != null;

        public AnomalyParticlePlayback(GameObject root, int rootIndex, int[] stopIndices, float[] stopTimes, float duration)
        {
            Root = root;
            _systems = root != null ? root.GetComponentsInChildren<ParticleSystem>(true) : Array.Empty<ParticleSystem>();
            _rootIndex = rootIndex;
            _stopIndices = stopIndices ?? Array.Empty<int>();
            _stopTimes = stopTimes ?? Array.Empty<float>();
            _duration = Mathf.Max(0f, duration);
            _bindingsValid = _rootIndex >= 0 && _rootIndex < _systems.Length && _stopIndices.Length == _stopTimes.Length;
            if (_bindingsValid)
            {
                for (int index = 0; index < _stopIndices.Length; index++)
                {
                    if (_stopIndices[index] < 0 || _stopIndices[index] >= _systems.Length || _stopTimes[index] < 0f || float.IsNaN(_stopTimes[index]) || float.IsInfinity(_stopTimes[index]))
                    {
                        _bindingsValid = false;
                        break;
                    }
                }
            }
        }

        public bool Play(float delay = 0f)
        {
            if (IsValid == false)
            {
                return false;
            }

            Stop();
            _elapsed = 0f;
            _delayRemaining = Mathf.Max(0f, delay);
            _playing = true;
            _started = _delayRemaining <= 0f;
            AnomalyVisual09Events.Record(_systems[_rootIndex], 7, _started ? 1 : 0, _delayRemaining);
            Root.SetActive(_started);
            if (_started)
            {
                AnomalyVisual09Events.Record(_systems[_rootIndex], 2);
                _systems[_rootIndex].Play(true);
            }
            return true;
        }

        public bool Tick(float deltaTime)
        {
            if (_playing == false || Root == null)
            {
                return false;
            }

            float activeDelta = Mathf.Max(0f, deltaTime);
            if (_started == false)
            {
                activeDelta = Mathf.Max(0f, activeDelta - _delayRemaining);
                _delayRemaining = Mathf.Max(0f, _delayRemaining - Mathf.Max(0f, deltaTime));
                if (_delayRemaining > 0f)
                {
                    return true;
                }
                _started = true;
                Root.SetActive(true);
                AnomalyVisual09Events.Record(_systems[_rootIndex], 2);
                _systems[_rootIndex].Play(true);
            }
            float previous = _elapsed;
            _elapsed += activeDelta;
            for (int index = 0; index < _stopIndices.Length; index++)
            {
                if (previous < _stopTimes[index] && _elapsed >= _stopTimes[index])
                {
                    ParticleSystem system = _systems[_stopIndices[index]];
                    if (system != null)
                    {
                        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
            }
            if (_duration > 0f && previous < _duration && _elapsed >= _duration)
            {
                foreach (ParticleSystem system in _systems)
                {
                    if (system != null)
                    {
                        system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
            }
            bool alive = false;
            foreach (ParticleSystem system in _systems)
            {
                alive |= system != null && system.IsAlive(false);
            }
            _playing = alive;
            return alive;
        }

        public void Stop()
        {
            foreach (ParticleSystem system in _systems)
            {
                if (system != null)
                {
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
            _playing = false;
            _started = false;
            _delayRemaining = 0f;
        }
    }
}
