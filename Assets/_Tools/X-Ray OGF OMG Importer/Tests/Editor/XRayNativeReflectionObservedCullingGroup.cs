using System;
using System.Reflection;
using System.Threading;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    internal sealed class XRayNativeReflectionObservedCullingGroup
    {
        private static readonly PropertyInfo SHADOW_CAMERA_OWNER = typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDShadowCullingCameraScope")?.GetProperty("CurrentCamera", BindingFlags.Public | BindingFlags.Static);

        private readonly XRayNativeReflectionCullingObserver _owner;
        private readonly XRayLevelInstancedRenderer _renderer;
        private readonly string _label;
        private int _entered;

        public BatchRendererGroup Group { get; }
        public BatchRendererGroup.OnPerformCulling Original { get; }
        public BatchRendererGroup.OnPerformCulling Wrapper { get; }

        public XRayNativeReflectionObservedCullingGroup(XRayNativeReflectionCullingObserver owner, XRayLevelInstancedRenderer renderer, BatchRendererGroup group, BatchRendererGroup.OnPerformCulling original, string label)
        {
            _owner = owner;
            _renderer = renderer;
            _label = label;
            Group = group;
            Original = original;
            Wrapper = Invoke;
        }

        private JobHandle Invoke(BatchRendererGroup group, BatchCullingContext context, BatchCullingOutput output, IntPtr userContext)
        {
            int threadId = Environment.CurrentManagedThreadId;
            int entryDepth = Interlocked.Increment(ref _entered);
            if (threadId != _owner.OwnerThreadId || entryDepth != 1) _owner.RecordFirstError("Unexpected BRG culling execution: ownerThread=" + _owner.OwnerThreadId + "; callbackThread=" + threadId + "; entryDepth=" + entryDepth + "; renderer=" + _label + "; view=" + context.viewType);
            int shadowCommands = _renderer.NativeShadowCommands;
            Camera ownerCamera = null;
            try
            {
                if (context.viewType == BatchCullingViewType.Light)
                {
                    try
                    {
                        ownerCamera = SHADOW_CAMERA_OWNER?.GetValue(null) as Camera;
                    }
                    catch (Exception exception)
                    {
                        _owner.RecordFirstError(exception.ToString());
                    }
                }
                return Original(group, context, output, userContext);
            }
            finally
            {
                try
                {
                    _owner.Record(context.viewType, ownerCamera, _renderer.NativeShadowCommands - shadowCommands);
                }
                finally
                {
                    Interlocked.Decrement(ref _entered);
                }
            }
        }
    }
}
