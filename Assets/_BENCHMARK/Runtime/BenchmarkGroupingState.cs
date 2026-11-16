using System;
using System.Collections.Generic;
using UnityEngine;
using XRay.Importer.Levels;

namespace Rvm.Benchmark
{
    public sealed class BenchmarkGroupingState : IDisposable
    {
        private readonly Dictionary<XRayLevelInstancedRenderer, GameConfig> _originals = new Dictionary<XRayLevelInstancedRenderer, GameConfig>();
        private readonly Dictionary<GameConfig, GameConfig> _copies = new Dictionary<GameConfig, GameConfig>();
        private bool _disposed;

        public BenchmarkGroupingState(XRayLevelInstancedRenderer[] renderers, string mode)
        {
            bool enabled = Enabled(mode);
            try
            {
                foreach (var renderer in renderers)
                {
                    var original = renderer.Configuration;
                    var source = original != null ? original : GameConfig.Load();
                    if (source == null) throw new InvalidOperationException("Production GameConfig is unavailable.");
                    if (!_copies.TryGetValue(source, out var copy))
                    {
                        copy = UnityEngine.Object.Instantiate(source);
                        copy.hideFlags = HideFlags.HideAndDontSave;
                        JsonUtility.FromJsonOverwrite("{\"_instanceSubmissionGroupingEnabled\":" + (enabled ? "true" : "false") + "}", copy);
                        _copies.Add(source, copy);
                    }
                    _originals.Add(renderer, original);
                    renderer.ApplyConfiguration(copy);
                    if (renderer.Configuration.InstanceSubmissionGroupingEnabled != enabled) throw new InvalidOperationException("Grouping override was not applied.");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static bool Enabled(string mode)
        {
            if (mode != "U" && mode != "G1") throw new ArgumentException("Location batching mode must be U or G1.");
            return mode == "G1";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var pair in _originals)
            {
                if (pair.Key != null) pair.Key.ApplyConfiguration(pair.Value);
            }
            foreach (var copy in _copies.Values)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(copy);
                else UnityEngine.Object.DestroyImmediate(copy);
            }
            _originals.Clear();
            _copies.Clear();
        }
    }
}
