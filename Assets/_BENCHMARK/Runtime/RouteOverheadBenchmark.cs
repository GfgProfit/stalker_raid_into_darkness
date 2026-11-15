using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace Rvm.Benchmark
{
    internal static class RouteOverheadBenchmark
    {
        private const int ITERATIONS = 65536;
        private const int CAPACITY = 262144;

        internal static List<Sample> Measure()
        {
            var type = Type.GetType("Rvm.CleanReleaseRoute07.CleanReleaseRouteController, Rvm.CleanReleaseRoute07", true);
            var root = new GameObject("Benchmark Route Overhead");
            var component = (Behaviour)root.AddComponent(type);
            component.enabled = false;
            try
            {
                var samples = type.GetField("_samples", BindingFlags.Instance | BindingFlags.NonPublic);
                Type sampleType = samples.FieldType.GetElementType();
                long capacityBytes = CAPACITY * (long)System.Runtime.InteropServices.Marshal.SizeOf(sampleType);
                samples.SetValue(component, Array.CreateInstance(sampleType, CAPACITY));
                Field(type, "_running").SetValue(component, true);
                Field(type, "_started").SetValue(component, true);
                Field(type, "_position").SetValue(component, 0);
                var update = (Action)Delegate.CreateDelegate(typeof(Action), component, type.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));
                return new List<Sample> { Run(type, component, update, "Full1", true, capacityBytes), Run(type, component, update, "NoStore1", false, capacityBytes), Run(type, component, update, "NoStore2", false, capacityBytes), Run(type, component, update, "Full2", true, capacityBytes) };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Sample Run(Type type, object component, Action update, string name, bool timing, long capacityBytes)
        {
            var count = Field(type, "_count");
            Field(type, "_timing").SetValue(component, timing);
            Field(type, "_phaseStart").SetValue(component, Stopwatch.GetTimestamp());
            count.SetValue(component, 0);
            for (int index = 0; index < 256; index++) update();
            count.SetValue(component, 0);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int index = 0; index < ITERATIONS; index++) update();
            double microseconds = (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency / ITERATIONS;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            int stored = (int)count.GetValue(component);
            if (stored != (timing ? ITERATIONS : 0))
            {
                throw new InvalidOperationException("Route overhead fixture did not execute the expected Store path.");
            }
            return new Sample { sample = name, iterations = ITERATIONS, stored = stored, microsecondsPerUpdate = microseconds, allocatedBytes = allocated, timestampCapacityBytes = capacityBytes };
        }

        private static FieldInfo Field(Type type, string name)
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [Serializable]
        internal sealed class Sample
        {
            public string sample;
            public int iterations;
            public int stored;
            public double microsecondsPerUpdate;
            public long allocatedBytes;
            public long timestampCapacityBytes;
            public string scope = "Actual warmed route Update on an isolated disabled controller; no pose transitions, render callback, focus callback, output IO or GPU work. Full/NoStore include QPC and route checks; their difference is timestamp storage. Not a claim of zero disabled diagnostics cost.";
        }
    }
}
