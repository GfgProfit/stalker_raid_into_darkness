using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unity.Jobs;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace Rvm.Benchmark
{
    public sealed class BenchmarkNativeCullingObserver : IDisposable
    {
        private const int MAX_OBSERVED_GROUPS = 64;

        private static readonly FieldInfo BACKEND = typeof(XRayLevelInstancedRenderer).GetField("_nativeRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GROUP = BACKEND?.FieldType.GetField("_group", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CULLING = typeof(BatchRendererGroup).GetField("m_PerformCulling", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo HANDLE = typeof(BatchRendererGroup).GetField("m_GroupHandle", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Dictionary<BatchRendererGroup, ObservedGroup> _groups = new Dictionary<BatchRendererGroup, ObservedGroup>();
        private readonly object _sync = new object();
        private bool _disposed;
        private long _ticks;
        private long _maximum;
        private int _samples;
        private int _observedGroups;
        private string _unavailableReason;

        public bool Supported => BACKEND != null && GROUP != null && GROUP.FieldType == typeof(BatchRendererGroup) && CULLING != null && CULLING.FieldType == typeof(BatchRendererGroup.OnPerformCulling) && !CULLING.IsInitOnly;
        public int RestorationFailures { get; private set; }
        public string RestorationError { get; private set; }

        public void Attach(XRayLevelInstancedRenderer renderer)
        {
            if (_disposed || _unavailableReason != null) return;
            if (!Supported)
            {
                _unavailableReason = "Installed renderer/BatchRendererGroup callback metadata is unavailable or incompatible.";
                return;
            }
            try
            {
                object backend = renderer != null ? BACKEND.GetValue(renderer) : null;
                if (backend != null) Attach(GROUP.GetValue(backend) as BatchRendererGroup);
            }
            catch (Exception exception)
            {
                _unavailableReason = "Native group discovery failed: " + exception.GetType().Name + ": " + exception.Message;
            }
        }

        public void Attach(BatchRendererGroup group)
        {
            if (_disposed || _unavailableReason != null || group == null) return;
            if (!Supported)
            {
                _unavailableReason = "Installed renderer/BatchRendererGroup callback metadata is unavailable or incompatible.";
                return;
            }
            try
            {
                if (_groups.TryGetValue(group, out var previous))
                {
                    if (!ReferenceEquals(CULLING.GetValue(group), previous.Wrapper)) _unavailableReason = "A measured native culling callback changed after observer attachment.";
                    return;
                }
                if (_groups.Count >= MAX_OBSERVED_GROUPS)
                {
                    _unavailableReason = "Native culling observer group capacity was exceeded.";
                    return;
                }
                var original = CULLING.GetValue(group) as BatchRendererGroup.OnPerformCulling;
                if (original == null)
                {
                    _unavailableReason = "Native culling callback is unavailable.";
                    return;
                }
                var observed = new ObservedGroup(original, this);
                _groups.Add(group, observed);
                CULLING.SetValue(group, observed.Wrapper);
                _observedGroups++;
            }
            catch (Exception exception)
            {
                _unavailableReason = "Native culling callback attachment failed: " + exception.GetType().Name + ": " + exception.Message;
            }
        }

        public Measurement Capture()
        {
            lock (_sync)
            {
                string reason = _unavailableReason;
                if (!Supported) reason = "Installed renderer/BatchRendererGroup callback metadata is unavailable or incompatible.";
                else if (reason == null && _samples == 0) reason = "No wrapped native culling callbacks completed in this observed window.";
                return new Measurement(Supported, reason == null && _samples > 0, _samples, _ticks, _maximum, _observedGroups, reason);
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _ticks = 0;
                _maximum = 0;
                _samples = 0;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            foreach (var pair in _groups)
            {
                try
                {
                    object current = CULLING.GetValue(pair.Key);
                    if (ReferenceEquals(current, pair.Value.Wrapper)) CULLING.SetValue(pair.Key, pair.Value.Original);
                    else if (!ReferenceEquals(current, pair.Value.Original) && !IsDisposed(pair.Key)) throw new InvalidOperationException("Native culling callback changed before restoration.");
                }
                catch (Exception exception)
                {
                    if (IsDisposed(pair.Key)) continue;
                    RestorationFailures++;
                    RestorationError = exception.GetType().Name + ": " + exception.Message;
                }
            }
            _groups.Clear();
            if (RestorationFailures != 0) throw new InvalidOperationException("Native culling observer could not restore " + RestorationFailures + " active callback(s): " + RestorationError);
        }

        private static bool IsDisposed(BatchRendererGroup group)
        {
            return HANDLE != null && HANDLE.GetValue(group) is IntPtr handle && handle == IntPtr.Zero;
        }

        private void Record(long ticks)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _ticks += ticks;
                _maximum = Math.Max(_maximum, ticks);
                _samples++;
            }
        }

        private sealed class ObservedGroup
        {
            private readonly BenchmarkNativeCullingObserver _owner;

            public BatchRendererGroup.OnPerformCulling Original { get; }
            public BatchRendererGroup.OnPerformCulling Wrapper { get; }

            public ObservedGroup(BatchRendererGroup.OnPerformCulling original, BenchmarkNativeCullingObserver owner)
            {
                Original = original;
                _owner = owner;
                Wrapper = Invoke;
            }

            private JobHandle Invoke(BatchRendererGroup group, BatchCullingContext context, BatchCullingOutput output, IntPtr userContext)
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    return Original(group, context, output, userContext);
                }
                finally
                {
                    _owner.Record(Stopwatch.GetTimestamp() - start);
                }
            }
        }

        public readonly struct Measurement
        {
            public bool Supported { get; }
            public bool Available { get; }
            public int Samples { get; }
            public long Ticks { get; }
            public long MaximumTicks { get; }
            public int ObservedGroups { get; }
            public string UnavailableReason { get; }

            public Measurement(bool supported, bool available, int samples, long ticks, long maximumTicks, int observedGroups, string unavailableReason)
            {
                Supported = supported;
                Available = available;
                Samples = samples;
                Ticks = ticks;
                MaximumTicks = maximumTicks;
                ObservedGroups = observedGroups;
                UnavailableReason = unavailableReason;
            }
        }
    }
}
