#if UNITY_EDITOR || CLEAN_RELEASE_ROUTE07
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Rvm.CleanReleaseRoute07
{
    public sealed class PixDiagnosticSession : IDisposable
    {
        private const int CAPACITY = 16384;
        private const int EVENT_ID = 6006;

        private readonly Stamp[] _cpu = new Stamp[CAPACITY];
        private readonly Stamp[] _gpu = new Stamp[CAPACITY];
        private readonly long[] _callBefore = new long[CAPACITY];
        private readonly long[] _callAfter = new long[CAPACITY];
        private readonly string _output;
        private readonly Camera _camera;
        private readonly CommandBuffer _commands;
        private readonly IntPtr _callback;
        private readonly Metadata _metadata;
        private int _count;
        private int _position = -1;
        private long _phaseStart;
        private bool _captureRequested;
        private bool _disposed;

        public bool Overflow { get; private set; }

        public PixDiagnosticSession(string output, string route, Camera camera, string[] args)
        {
            _output = output;
            _camera = camera;
            int capturePosition = ParseCapturePosition(args);
            int routeKey = 17;
            for (int i = 0; i < route.Length; i++) routeKey = unchecked(routeKey * 31 + route[i]);
            int state = Native.RC06Initialize(routeKey);
            _callback = Native.RC06RenderCallback();
            _commands = new CommandBuffer { name = "RC06 diagnostic frame identity" };
            _metadata = new Metadata { instrumented = true, eligibleForFpsComparison = false, measurementClass = "pix-diagnostic", route = route, routeKey = routeKey, buildGuid = Application.buildGUID, stopwatchFrequency = Stopwatch.Frequency, qpcFrequency = Native.RC06Frequency(), initializationState = state, gpuCapturerLoaded = (state & 1) != 0, attachedReported = (state & 2) != 0, unityD3D12Interface = (state & 4) != 0, capturePosition = capturePosition, captureFile = capturePosition < 0 ? null : Path.Combine(output, "structural.wpix"), captureRequested = false };
            if (_metadata.qpcFrequency <= 0 || _callback == IntPtr.Zero || !_metadata.unityD3D12Interface || capturePosition >= 0 && !_metadata.gpuCapturerLoaded)
            {
                Native.RC06Stop();
                _commands.Release();
                throw new InvalidOperationException("PIX bridge/clock/D3D12 interface is unavailable, or structural capture was requested without an external PIX launch before device creation.");
            }
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            File.WriteAllText(Path.Combine(output, "pix-diagnostic.json"), JsonUtility.ToJson(_metadata, true));
        }

        public static int ParseCapturePosition(string[] args)
        {
            int index = Array.IndexOf(args, "-benchmark-pix-capture-position");
            if (index < 0) return -1;
            if (index + 1 >= args.Length || !int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int position) || position < 1 || position > 4) throw new ArgumentException("PIX capture position must be 1..4.");
            return position - 1;
        }

        public void Frame(long timestamp, int position, long phaseStart)
        {
            _position = position;
            _phaseStart = phaseStart;
            Record(timestamp, 0);
            if (!_captureRequested && position == _metadata.capturePosition && position >= 0 && (timestamp - _phaseStart) / (double)Stopwatch.Frequency >= 1.5)
            {
                _captureRequested = true;
                Record(Stopwatch.GetTimestamp(), 3);
                _metadata.captureRequestFrame = Time.frameCount;
                _metadata.captureRequestStopwatch = Stopwatch.GetTimestamp();
                _metadata.captureHresult = Native.RC06Capture(_metadata.captureFile);
                _metadata.captureRequested = true;
                Record(Stopwatch.GetTimestamp(), 4);
                if (_metadata.captureHresult < 0) throw new InvalidOperationException("PIXGpuCaptureNextFrames failed: 0x" + _metadata.captureHresult.ToString("X8"));
            }
        }

        private void Record(long timestamp, int kind)
        {
            if (_count >= CAPACITY)
            {
                Overflow = true;
                return;
            }
            _callBefore[_count] = Stopwatch.GetTimestamp();
            int result = Native.RC06Stamp(Time.frameCount, _position, kind, timestamp, out _cpu[_count]);
            _callAfter[_count] = Stopwatch.GetTimestamp();
            if (result != 0)
            {
                Overflow = true;
                return;
            }
            _count++;
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            Marker(context, camera, 1);
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            Marker(context, camera, 2);
        }

        private void Marker(ScriptableRenderContext context, Camera camera, int kind)
        {
            if (camera != _camera || _disposed) return;
            IntPtr data = Native.RC06PrepareGpu(Time.frameCount, _position, kind, Stopwatch.GetTimestamp());
            if (data == IntPtr.Zero)
            {
                Overflow = true;
                return;
            }
            _commands.Clear();
            _commands.IssuePluginEventAndData(_callback, EVENT_ID, data);
            context.ExecuteCommandBuffer(_commands);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            Record(Stopwatch.GetTimestamp(), 5);
            Native.RC06Stop();
            int gpuCount = Native.RC06ReadGpu(_gpu, CAPACITY);
            _commands.Release();
            _metadata.overflow = Overflow;
            _metadata.cpuSamples = _count;
            _metadata.gpuRecords = gpuCount;
            Write("clock-sync.csv", _cpu, _count);
            Write("gpu-frame-markers.csv", _gpu, gpuCount);
            File.WriteAllText(Path.Combine(_output, "pix-diagnostic.json"), JsonUtility.ToJson(_metadata, true));
        }

        private void Write(string name, Stamp[] samples, int count)
        {
            using (var writer = new StreamWriter(Path.Combine(_output, name)))
            {
                writer.WriteLine("sequence,unity_frame,position,kind,stopwatch,qpc_before,qpc_after,pid,tid,recorded,call_before_stopwatch,call_after_stopwatch");
                for (int i = 0; i < count; i++)
                {
                    Stamp s = samples[i];
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}", s.Sequence, s.Frame, s.Position, s.Kind, s.Stopwatch, s.Before, s.After, s.Process, s.Thread, s.Recorded, samples == _cpu ? _callBefore[i] : 0, samples == _cpu ? _callAfter[i] : 0));
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Stamp
        {
            public long Before;
            public long After;
            public long Stopwatch;
            public int Sequence;
            public int Frame;
            public int Position;
            public int Kind;
            public uint Process;
            public uint Thread;
            public int Recorded;
            public int Reserved;
        }

        [Serializable]
        private sealed class Metadata
        {
            public bool instrumented;
            public bool eligibleForFpsComparison;
            public string measurementClass;
            public string route;
            public int routeKey;
            public string buildGuid;
            public long stopwatchFrequency;
            public long qpcFrequency;
            public int initializationState;
            public bool gpuCapturerLoaded;
            public bool attachedReported;
            public bool unityD3D12Interface;
            public int capturePosition;
            public string captureFile;
            public bool captureRequested;
            public int captureRequestFrame;
            public long captureRequestStopwatch;
            public int captureHresult;
            public bool overflow;
            public int cpuSamples;
            public int gpuRecords;
        }

        private static class Native
        {
            [DllImport("RenderCostPixBridge", CallingConvention = CallingConvention.Cdecl)] internal static extern int RC06Initialize(int route);
            [DllImport("RenderCostPixBridge", CallingConvention = CallingConvention.Cdecl)] internal static extern void RC06Stop();
            [DllImport("RenderCostPixBridge", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr RC06RenderCallback();
            [DllImport("RenderCostPixBridge", CallingConvention = CallingConvention.Cdecl)] internal static extern long RC06Frequency();
            [DllImport("RenderCostPixBridge", CallingConvention = CallingConvention.Cdecl)] internal static extern int RC06Stamp(int frame, int position, int kind, long stopwatch, out Stamp stamp);
            [DllImport("RenderCostPixBridge", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr RC06PrepareGpu(int frame, int position, int kind, long stopwatch);
            [DllImport("RenderCostPixBridge", CallingConvention = CallingConvention.Cdecl)] internal static extern int RC06ReadGpu([Out] Stamp[] output, int capacity);
            [DllImport("RenderCostPixBridge", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] internal static extern int RC06Capture(string filename);
        }
    }
}
#endif
