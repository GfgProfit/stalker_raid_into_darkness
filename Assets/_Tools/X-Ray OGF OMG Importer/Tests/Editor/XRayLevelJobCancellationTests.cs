using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayLevelJobCancellationTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(false)]
        [TestCase(true)]
        public void CompletionDisposesBeforeReentrantAndRepeatedCancellation(bool indexJob)
        {
            object job = null;
            CancellationTokenSource cancellation = null;
            int callbacks = 0;
            int progressId = -1;
            try
            {
                job = Create(indexJob, () =>
                {
                    callbacks++;
                    Assert.IsTrue(IsDone(job));
                    Assert.Throws<ObjectDisposedException>(() => cancellation.Token.ThrowIfCancellationRequested());
                    Cancel(job);
                }, out cancellation, out progressId);
                SetTask(job, Task.CompletedTask);
                ExpectCancelledCompletion(job);
                Assert.DoesNotThrow(() => Poll(job));
                Assert.IsTrue(IsDone(job));
                Assert.AreEqual(1, callbacks);
                Assert.Throws<ObjectDisposedException>(() => cancellation.Token.ThrowIfCancellationRequested());
                Assert.DoesNotThrow(() => Cancel(job));
                Assert.DoesNotThrow(() => Cancel(job));
            }
            finally
            {
                cancellation?.Dispose();
                if (progressId >= 0 && Progress.Exists(progressId)) Progress.Remove(progressId);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActiveCancellationSignalsCapturedTokenWithoutDisposingBeforeCompletion(bool indexJob)
        {
            int callbacks = 0;
            object job = Create(indexJob, () => callbacks++, out CancellationTokenSource cancellation, out int progressId);
            try
            {
                var completion = new TaskCompletionSource<bool>();
                SetTask(job, completion.Task);
                CancellationToken token = cancellation.Token;
                Assert.DoesNotThrow(() => Cancel(job));
                Assert.DoesNotThrow(() => Cancel(job));
                Assert.IsTrue(token.IsCancellationRequested);
                Assert.AreEqual(token, cancellation.Token);
                Assert.DoesNotThrow(() => Poll(job));
                Assert.IsFalse(IsDone(job));
                Assert.AreEqual(0, callbacks);
                Assert.AreEqual(token, cancellation.Token);
                completion.SetResult(true);
                ExpectCancelledCompletion(job);
                Assert.DoesNotThrow(() => Poll(job));
                Assert.IsTrue(IsDone(job));
                Assert.AreEqual(1, callbacks);
                Assert.IsTrue(token.IsCancellationRequested);
                Assert.Throws<ObjectDisposedException>(() => cancellation.Token.ThrowIfCancellationRequested());
                Assert.DoesNotThrow(() => Cancel(job));
            }
            finally
            {
                cancellation.Dispose();
                if (progressId >= 0 && Progress.Exists(progressId)) Progress.Remove(progressId);
            }
        }

        private static object Create(bool indexJob, Action finished, out CancellationTokenSource cancellation, out int progressId)
        {
            Type type = indexJob ? typeof(XRayLevelIndexJob) : typeof(XRayLevelProbeJob);
            Type callbackType = indexJob ? typeof(Action<XRayLevelIndexJob>) : typeof(Action);
            ConstructorInfo constructor = type.GetConstructor(PRIVATE_INSTANCE, null, new[] { callbackType }, null);
            Assert.IsNotNull(constructor);
            object callback = indexJob ? (object)new Action<XRayLevelIndexJob>(_ => finished()) : finished;
            object job = constructor.Invoke(new[] { callback });
            cancellation = (CancellationTokenSource)type.GetField("_cancellation", PRIVATE_INSTANCE).GetValue(job);
            progressId = -1;
            if (indexJob)
            {
                progressId = Progress.Start("X-Ray cancellation regression", "Synthetic task completion", Progress.Options.None);
                type.GetField("_progressId", PRIVATE_INSTANCE).SetValue(job, progressId);
            }
            return job;
        }

        private static void SetTask(object job, Task task)
        {
            job.GetType().GetField("_task", PRIVATE_INSTANCE).SetValue(job, task);
        }

        private static void ExpectCancelledCompletion(object job)
        {
            if (!(job is XRayLevelIndexJob)) return;
            typeof(XRayLevelIndexJob).GetProperty(nameof(XRayLevelIndexJob.Cancelled)).SetValue(job, true);
            LogAssert.Expect(LogType.Warning, "[X-Ray Level] MO2 virtual index build was cancelled; the previous index is kept.");
        }

        private static bool IsDone(object job)
        {
            return job is XRayLevelIndexJob index ? index.IsDone : ((XRayLevelProbeJob)job).IsDone;
        }

        private static void Cancel(object job)
        {
            if (job is XRayLevelIndexJob index) index.Cancel();
            else ((XRayLevelProbeJob)job).Cancel();
        }

        private static void Poll(object job)
        {
            job.GetType().GetMethod("Poll", PRIVATE_INSTANCE).Invoke(job, null);
        }
    }
}
