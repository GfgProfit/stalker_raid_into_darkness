using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Rvm.Benchmark
{
    public static class BenchmarkClock
    {
        private static readonly bool _windowsQpc;

        public static long Frequency { get; }
        public static bool WindowsQpc => _windowsQpc;

        static BenchmarkClock()
        {
            bool windows = Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer;
            if (windows && QueryPerformanceFrequency(out long frequency) && frequency > 0)
            {
                _windowsQpc = true;
                Frequency = frequency;
            }
            else
            {
                Frequency = Stopwatch.Frequency;
            }
        }

        public static long GetTimestamp()
        {
            if (!_windowsQpc)
            {
                return Stopwatch.GetTimestamp();
            }
            if (!QueryPerformanceCounter(out long value))
            {
                throw new InvalidOperationException("Windows QPC failed during benchmark.");
            }
            return value;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceCounter(out long value);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceFrequency(out long value);
    }
}
