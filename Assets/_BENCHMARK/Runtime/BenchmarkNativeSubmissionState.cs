using System;
using System.Collections.Generic;
using XRay.Importer.Levels;

namespace Rvm.Benchmark
{
    public sealed class BenchmarkNativeSubmissionState : IDisposable
    {
        private readonly Dictionary<XRayLevelInstancedRenderer, bool?> _originals = new Dictionary<XRayLevelInstancedRenderer, bool?>();
        private bool _disposed;

        public string Mode { get; }
        public IEnumerable<XRayLevelInstancedRenderer> Renderers => _originals.Keys;

        public BenchmarkNativeSubmissionState(XRayLevelInstancedRenderer[] renderers, string mode)
        {
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));
            bool enabled = Enabled(mode);
            Mode = mode;
            try
            {
                foreach (var renderer in renderers)
                {
                    if (renderer == null || _originals.ContainsKey(renderer)) continue;
                    _originals.Add(renderer, renderer.NativeSubmissionOverride);
                    renderer.NativeSubmissionOverride = enabled;
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
            if (mode != "A" && mode != "B") throw new ArgumentException("Native submission mode must be A or B.");
            return mode == "B";
        }

        public static string RequestedMode(string[] args)
        {
            int index = Array.IndexOf(args, "-instance-native-submission");
            if (index < 0) return null;
            if (index + 1 >= args.Length || Array.LastIndexOf(args, "-instance-native-submission") != index) throw new ArgumentException("One native submission mode A or B is required.");
            if (Array.IndexOf(args, "-clean-route") < 0 && Array.IndexOf(args, "-clean-route-no-timing") < 0 && Array.IndexOf(args, "-clean-route-config-check") < 0) throw new ArgumentException("A native submission override requires an explicit benchmark route session.");
            Enabled(args[index + 1]);
            return args[index + 1];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var pair in _originals)
            {
                if (pair.Key != null) pair.Key.NativeSubmissionOverride = pair.Value;
            }
            _originals.Clear();
        }
    }
}
