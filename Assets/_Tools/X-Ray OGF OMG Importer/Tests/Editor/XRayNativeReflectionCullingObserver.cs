using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    internal sealed class XRayNativeReflectionCullingObserver : IDisposable
    {
        private static readonly FieldInfo BACKEND = typeof(XRayLevelInstancedRenderer).GetField("_nativeRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GROUP = typeof(XRayInstanceNativeRenderer).GetField("_group", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CULLING = typeof(BatchRendererGroup).GetField("m_PerformCulling", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<XRayNativeReflectionObservedCullingGroup> _groups = new List<XRayNativeReflectionObservedCullingGroup>();
        private readonly List<XRayNativeReflectionCullingViewMetadata> _views = new List<XRayNativeReflectionCullingViewMetadata>();
        private readonly object _sync = new object();
        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

        private string _recordError;

        public void Attach(XRayLevelInstancedRenderer renderer, string label)
        {
            object backend = BACKEND.GetValue(renderer);
            if (backend == null) return;
            var group = GROUP.GetValue(backend) as BatchRendererGroup;
            if (group == null) return;
            foreach (XRayNativeReflectionObservedCullingGroup observed in _groups) if (ReferenceEquals(observed.Group, group)) return;
            Assert.That(CULLING, Is.Not.Null);
            Assert.That(CULLING.FieldType, Is.EqualTo(typeof(BatchRendererGroup.OnPerformCulling)));
            Assert.That(CULLING.IsInitOnly, Is.False);
            var original = CULLING.GetValue(group) as BatchRendererGroup.OnPerformCulling;
            Assert.That(original, Is.Not.Null);
            var entry = new XRayNativeReflectionObservedCullingGroup(this, renderer, group, original, label);
            _groups.Add(entry);
            CULLING.SetValue(group, entry.Wrapper);
        }

        public XRayNativeReflectionCullingMetadata Snapshot()
        {
            lock (_sync)
            {
                return new XRayNativeReflectionCullingMetadata { error = _recordError, views = _views.ToArray() };
            }
        }

        public void Dispose()
        {
            Exception failure = null;
            foreach (XRayNativeReflectionObservedCullingGroup observed in _groups)
            {
                try
                {
                    object current = CULLING.GetValue(observed.Group);
                    if (ReferenceEquals(current, observed.Wrapper)) CULLING.SetValue(observed.Group, observed.Original);
                    else if (!ReferenceEquals(current, observed.Original)) throw new InvalidOperationException("The owned fixture culling callback changed during observation.");
                }
                catch (Exception exception)
                {
                    failure = failure ?? exception;
                }
            }
            _groups.Clear();
            if (failure != null) throw new InvalidOperationException("The fixture culling callback could not be restored.", failure);
        }

        internal int OwnerThreadId => _ownerThreadId;

        internal void RecordFirstError(string message) => Interlocked.CompareExchange(ref _recordError, message, null);

        internal void Record(BatchCullingViewType viewType, Camera ownerCamera, int shadowCommands)
        {
            try
            {
                var record = new XRayNativeReflectionCullingViewMetadata { ownerCameraEntityId = ownerCamera != null ? ownerCamera.GetEntityId().ToString() : null, ownerCameraType = ownerCamera != null ? ownerCamera.cameraType.ToString() : null, shadowCommands = shadowCommands, viewType = viewType.ToString() };
                lock (_sync)
                {
                    _views.Add(record);
                }
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    _recordError = _recordError ?? exception.ToString();
                }
            }
        }
    }
}
