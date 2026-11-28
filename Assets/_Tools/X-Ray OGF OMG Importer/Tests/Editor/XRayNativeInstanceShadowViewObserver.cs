using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    internal sealed class XRayNativeInstanceShadowViewObserver : IDisposable
    {
        private static readonly FieldInfo BACKEND = typeof(XRayLevelInstancedRenderer).GetField("_nativeRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GROUP = typeof(XRayInstanceNativeRenderer).GetField("_group", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CULLING = typeof(BatchRendererGroup).GetField("m_PerformCulling", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly XRayLevelInstancedRenderer _renderer;
        private readonly Dictionary<BatchRendererGroup, BatchRendererGroup.OnPerformCulling> _originals = new Dictionary<BatchRendererGroup, BatchRendererGroup.OnPerformCulling>();
        private readonly Dictionary<BatchRendererGroup, BatchRendererGroup.OnPerformCulling> _wrappers = new Dictionary<BatchRendererGroup, BatchRendererGroup.OnPerformCulling>();

        public int MaximumLightSplits { get; private set; }

        public XRayNativeInstanceShadowViewObserver(XRayLevelInstancedRenderer renderer)
        {
            _renderer = renderer;
            RenderPipelineManager.beginCameraRendering += Attach;
        }

        public void Reset()
        {
            MaximumLightSplits = 0;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= Attach;
            foreach (KeyValuePair<BatchRendererGroup, BatchRendererGroup.OnPerformCulling> entry in _originals)
            {
                object current = CULLING.GetValue(entry.Key);
                if (ReferenceEquals(current, _wrappers[entry.Key])) CULLING.SetValue(entry.Key, entry.Value);
                else if (!ReferenceEquals(current, entry.Value)) throw new InvalidOperationException("The native fixture shadow observer could not restore its callback.");
            }
            _originals.Clear();
            _wrappers.Clear();
        }

        private void Attach(ScriptableRenderContext context, Camera camera)
        {
            if (_renderer == null) return;
            object backend = BACKEND.GetValue(_renderer);
            var group = backend != null ? GROUP.GetValue(backend) as BatchRendererGroup : null;
            if (group == null || _originals.ContainsKey(group)) return;
            Assert.That(CULLING, Is.Not.Null);
            var original = CULLING.GetValue(group) as BatchRendererGroup.OnPerformCulling;
            Assert.That(original, Is.Not.Null);
            BatchRendererGroup.OnPerformCulling wrapper = (rendererGroup, cullingContext, output, userContext) =>
            {
                if (cullingContext.viewType == BatchCullingViewType.Light) MaximumLightSplits = Math.Max(MaximumLightSplits, cullingContext.cullingSplits.Length);
                return original(rendererGroup, cullingContext, output, userContext);
            };
            _originals.Add(group, original);
            _wrappers.Add(group, wrapper);
            CULLING.SetValue(group, wrapper);
        }
    }
}
