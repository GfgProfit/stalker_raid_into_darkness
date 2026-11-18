using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Rvm.CleanReleaseRoute07;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace Rvm.Benchmark.Tests
{
    public sealed class NativeCullingObserverTests
    {
        [Test]
        public void InstalledMetadataIsSupportedAndMissingSamplesRemainUnavailable()
        {
            using (var observer = new BenchmarkNativeCullingObserver())
            {
                var measurement = observer.Capture();
                Assert.That(measurement.Supported, Is.True);
                Assert.That(measurement.Available, Is.False);
                Assert.That(measurement.Samples, Is.EqualTo(0));
                Assert.That(measurement.UnavailableReason, Is.Not.Empty);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WrapperPreservesOriginalCallbackAndRestoresItAfterSuccessOrFailure(bool fail)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("BatchRendererGroup requires a graphics device.");
            int calls = 0;
            IntPtr expectedContext = new IntPtr(123);
            BatchRendererGroup expectedGroup = null;
            BatchRendererGroup.OnPerformCulling original = (group, context, output, userContext) =>
            {
                Assert.That(group, Is.SameAs(expectedGroup));
                Assert.That(userContext, Is.EqualTo(expectedContext));
                calls++;
                Thread.SpinWait(1024);
                if (fail) throw new InvalidOperationException("Native callback positive control");
                return default(JobHandle);
            };
            using (var group = new BatchRendererGroup(original, IntPtr.Zero))
            {
                expectedGroup = group;
                var field = typeof(BatchRendererGroup).GetField("m_PerformCulling", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                var observer = new BenchmarkNativeCullingObserver();
                try
                {
                    observer.Attach(group);
                    var measured = (BatchRendererGroup.OnPerformCulling)field.GetValue(group);
                    Assert.That(measured, Is.Not.SameAs(original));
                    if (fail) Assert.Throws<InvalidOperationException>(() => measured(group, default(BatchCullingContext), default(BatchCullingOutput), expectedContext));
                    else Assert.That(measured(group, default(BatchCullingContext), default(BatchCullingOutput), expectedContext), Is.EqualTo(default(JobHandle)));
                    var snapshot = observer.Capture();
                    Assert.That(calls, Is.EqualTo(1));
                    Assert.That(snapshot.Available, Is.True);
                    Assert.That(snapshot.Samples, Is.EqualTo(1));
                    Assert.That(snapshot.Ticks, Is.GreaterThan(0));
                    Assert.That(snapshot.MaximumTicks, Is.EqualTo(snapshot.Ticks));
                    observer.Reset();
                    Assert.That(observer.Capture().Available, Is.False);
                    Assert.That(observer.Capture().Samples, Is.EqualTo(0));
                }
                finally
                {
                    observer.Dispose();
                }
                Assert.That(field.GetValue(group), Is.SameAs(original));
                Assert.That(observer.RestorationFailures, Is.EqualTo(0));
                observer.Dispose();
                Assert.That(field.GetValue(group), Is.SameAs(original));
            }
        }

        [Test]
        public void DisposedNativeGroupCanBeSafelyRestored()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("BatchRendererGroup requires a graphics device.");
            var observer = new BenchmarkNativeCullingObserver();
            var group = new BatchRendererGroup((rendererGroup, context, output, userContext) => default(JobHandle), IntPtr.Zero);
            try
            {
                observer.Attach(group);
                group.Dispose();
                group = null;
                Assert.DoesNotThrow(() => observer.Dispose());
                Assert.That(observer.RestorationFailures, Is.EqualTo(0));
            }
            finally
            {
                observer.Dispose();
                group?.Dispose();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RouteCompletionAndAbortRestoreTechnicalCullingCallback(bool abort)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("BatchRendererGroup requires a graphics device.");
            var root = new GameObject("Native culling observer lifecycle");
            BatchRendererGroup.OnPerformCulling original = (group, context, output, userContext) => default(JobHandle);
            using (var group = new BatchRendererGroup(original, IntPtr.Zero))
            {
                var observer = new BenchmarkNativeCullingObserver();
                try
                {
                    observer.Attach(group);
                    var technical = root.AddComponent<LocationBatchingBenchmark>();
                    var renderer = root.AddComponent<XRayLevelInstancedRenderer>();
                    var signature = typeof(Action<ScriptableRenderContext, Camera>);
                    var originalBegin = (Action<ScriptableRenderContext, Camera>)Delegate.CreateDelegate(signature, renderer, typeof(XRayLevelInstancedRenderer).GetMethod("OnBeginCameraRendering", BindingFlags.Instance | BindingFlags.NonPublic));
                    var measuredType = typeof(LocationBatchingBenchmark).GetNestedType("MeasuredCallback", BindingFlags.NonPublic);
                    object measured = Activator.CreateInstance(measuredType, new object[] { originalBegin, renderer, observer });
                    var wrappedBegin = (Action<ScriptableRenderContext, Camera>)Delegate.CreateDelegate(signature, measured, measuredType.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public));
                    var record = (Action<ScriptableRenderContext, Camera>)Delegate.CreateDelegate(signature, technical, typeof(LocationBatchingBenchmark).GetMethod("Record", BindingFlags.Instance | BindingFlags.NonPublic));
                    var callbacks = (IList)typeof(LocationBatchingBenchmark).GetField("_callbacks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(technical);
                    callbacks.Add(measured);
                    RenderPipelineManager.beginCameraRendering -= originalBegin;
                    RenderPipelineManager.beginCameraRendering += wrappedBegin;
                    RenderPipelineManager.endCameraRendering += record;
                    typeof(LocationBatchingBenchmark).GetField("_nativeCulling", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(technical, observer);
                    typeof(LocationBatchingBenchmark).GetField("_technicalObserver", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, technical);
                    Assert.That(ContainsCallback("beginCameraRendering", measured, "Invoke"), Is.True);
                    Assert.That(ContainsCallback("endCameraRendering", technical, "Record"), Is.True);
                    Assert.That(ContainsCallback("beginCameraRendering", renderer, "OnBeginCameraRendering"), Is.False);
                    var controller = root.AddComponent<CleanReleaseRouteController>();
                    controller.ConfigureProfiles(root.AddComponent<Camera>(), new BenchmarkScenario[0], "native-observer-test", null);
                    if (abort) controller.Abort();
                    else typeof(CleanReleaseRouteController).GetMethod("RestoreState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                    var field = typeof(BatchRendererGroup).GetField("m_PerformCulling", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(field.GetValue(group), Is.SameAs(original));
                    Assert.That(observer.RestorationFailures, Is.EqualTo(0));
                    Assert.That(technical.enabled, Is.False);
                    Assert.That(callbacks.Count, Is.EqualTo(0));
                    Assert.That(ContainsCallback("beginCameraRendering", measured, "Invoke"), Is.False);
                    Assert.That(ContainsCallback("endCameraRendering", technical, "Record"), Is.False);
                    Assert.That(ContainsCallback("beginCameraRendering", renderer, "OnBeginCameraRendering"), Is.True);
                    Assert.That(typeof(LocationBatchingBenchmark).GetField("_technicalObserver", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), Is.Null);
                }
                finally
                {
                    LocationBatchingBenchmark.RestoreMode();
                    observer.Dispose();
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static bool ContainsCallback(string name, object target, string method)
        {
            var field = typeof(RenderPipelineManager).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var callbacks = field.GetValue(null) as Delegate;
            if (callbacks == null) return false;
            foreach (Delegate callback in callbacks.GetInvocationList())
            {
                if (ReferenceEquals(callback.Target, target) && callback.Method.Name == method) return true;
            }
            return false;
        }
    }
}
