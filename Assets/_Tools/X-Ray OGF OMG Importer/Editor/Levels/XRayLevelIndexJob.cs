using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelIndexJob
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly Action<XRayLevelIndexJob> _finished;
        private Task _task;
        private int _progressId;
        private volatile float _progress;
        private volatile string _stage = "Queued";

        private XRayLevelIndexJob(Action<XRayLevelIndexJob> finished)
        {
            _finished = finished;
        }

        public float Progress => _progress;
        public string Stage => _stage;
        public bool IsDone { get; private set; }
        public XRayAssetIndex Index { get; private set; }
        public string Error { get; private set; }
        public bool Cancelled { get; private set; }

        public static XRayLevelIndexJob Start(XRaySourcePlan plan, string path, Action<XRayLevelIndexJob> finished)
        {
            var job = new XRayLevelIndexJob(finished);
            job._progressId = UnityEditor.Progress.Start("X-Ray MO2 Virtual Index", plan.ProfileName, UnityEditor.Progress.Options.None);
            UnityEditor.Progress.RegisterCancelCallback(job._progressId, () =>
            {
                job.Cancel();
                return true;
            });
            CancellationToken token = job._cancellation.Token;
            job._task = Task.Run(() =>
            {
                XRayAssetIndex index = XRayAssetIndex.Build(plan, token, (value, stage) =>
                {
                    job._progress = value * 0.95f;
                    job._stage = stage;
                });
                token.ThrowIfCancellationRequested();
                job._stage = "Saving index";
                index.Save(path);
                return index;
            }, token).ContinueWith(task =>
            {
                if (task.IsCanceled || (task.Exception != null && task.Exception.InnerException is OperationCanceledException))
                {
                    job.Cancelled = true;
                }
                else if (task.Exception != null)
                {
                    job.Error = task.Exception.InnerException?.Message ?? task.Exception.Message;
                }
                else
                {
                    job.Index = task.Result;
                }
            });
            EditorApplication.update += job.Poll;
            return job;
        }

        public void Cancel()
        {
            if (IsDone) return;
            _cancellation.Cancel();
        }

        private void Poll()
        {
            if (!_task.IsCompleted)
            {
                UnityEditor.Progress.Report(_progressId, _progress, _stage);
                return;
            }

            EditorApplication.update -= Poll;
            IsDone = true;
            UnityEditor.Progress.Finish(_progressId, Index != null ? UnityEditor.Progress.Status.Succeeded : Cancelled ? UnityEditor.Progress.Status.Canceled : UnityEditor.Progress.Status.Failed);
            if (Error != null)
            {
                Debug.LogError("[X-Ray Level] MO2 virtual index failed: " + Error);
            }
            else if (Cancelled)
            {
                Debug.LogWarning("[X-Ray Level] MO2 virtual index build was cancelled; the previous index is kept.");
            }
            else
            {
                XRayAssetIndexStatistics statistics = Index.Statistics;
                Debug.Log($"[X-Ray Level] MO2 virtual index: {statistics.FileCount} files, {statistics.ConflictCount} conflicts, {statistics.LoadedArchiveCount} archives ({statistics.ArchiveEntryCount} entries), {statistics.BuildMilliseconds} ms.");
            }

            _cancellation.Dispose();
            _finished?.Invoke(this);
        }
    }

    public sealed class XRayLevelProbeJob
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly Action _finished;
        private Task _task;

        private XRayLevelProbeJob(Action finished)
        {
            _finished = finished;
        }

        public bool IsDone { get; private set; }

        public static XRayLevelProbeJob Start(XRayLevelGamedataInfo info, Action finished)
        {
            var job = new XRayLevelProbeJob(finished);
            CancellationToken token = job._cancellation.Token;
            job._task = Task.Run(() =>
            {
                foreach (XRayLevelEntry entry in info.Levels)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    entry.Probe = XRayLevelProbe.Run(info.Effective, entry);
                }
            }, token);
            EditorApplication.update += job.Poll;
            return job;
        }

        public void Cancel()
        {
            if (IsDone) return;
            _cancellation.Cancel();
        }

        private void Poll()
        {
            if (!_task.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= Poll;
            IsDone = true;
            if (_task.Exception != null)
            {
                Debug.LogError("[X-Ray Level] Level format checks failed: " + _task.Exception.InnerException);
            }

            _cancellation.Dispose();
            _finished?.Invoke();
        }
    }
}
