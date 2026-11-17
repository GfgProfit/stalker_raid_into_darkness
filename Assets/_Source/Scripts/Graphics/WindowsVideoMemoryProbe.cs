using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RaidIntoDarkness.GraphicsOptions
{
    public sealed class WindowsVideoMemoryProbe : IDisposable
    {
        private const uint ADAPTER_FLAG_SOFTWARE = 2;
        private const int ENUM_ADAPTERS_SLOT = 12;
        private const int GET_DESCRIPTION_SLOT = 10;
        private const int QUERY_MEMORY_SLOT = 14;
        private const uint PDH_FORMAT_BYTES = 0x1400;

        private static readonly Guid FACTORY_ID = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        private static readonly Guid ADAPTER_MEMORY_ID = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");

        private IntPtr _adapter;
        private IntPtr _counterQuery;
        private IntPtr _dedicatedUsageCounter;
        private QueryMemory _queryMemory;

        public ulong DedicatedCapacity { get; private set; }
        public ulong SharedCapacity { get; private set; }

        public static WindowsVideoMemoryProbe Open()
        {
            if (IntPtr.Size != 8 || Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
            {
                return null;
            }
            var probe = new WindowsVideoMemoryProbe();
            try
            {
                if (probe.OpenAdapter())
                {
                    return probe;
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is NotSupportedException)
            {
                probe.Dispose();
                return null;
            }
            probe.Dispose();
            return null;
        }

        public void Read(out ulong? dedicatedUsage, out ulong? processUsage, out ulong? processBudget)
        {
            dedicatedUsage = null;
            processUsage = null;
            processBudget = null;
            if (_dedicatedUsageCounter != IntPtr.Zero && PdhCollectQueryData(_counterQuery) == 0 && PdhGetFormattedCounterValue(_dedicatedUsageCounter, PDH_FORMAT_BYTES, IntPtr.Zero, out CounterValue counter) == 0 && counter.Status <= 1 && counter.Bytes >= 0)
            {
                dedicatedUsage = (ulong)counter.Bytes;
            }
            if (_queryMemory != null && _queryMemory(_adapter, 0, 0, out MemoryInfo memory) >= 0 && memory.Budget > 0)
            {
                processUsage = memory.CurrentUsage;
                processBudget = memory.Budget;
            }
        }

        public void Dispose()
        {
            _queryMemory = null;
            _dedicatedUsageCounter = IntPtr.Zero;
            if (_counterQuery != IntPtr.Zero)
            {
                PdhCloseQuery(_counterQuery);
                _counterQuery = IntPtr.Zero;
            }
            if (_adapter != IntPtr.Zero)
            {
                Marshal.Release(_adapter);
                _adapter = IntPtr.Zero;
            }
        }

        private bool OpenAdapter()
        {
            if (CreateDXGIFactory1(in FACTORY_ID, out IntPtr factory) < 0)
            {
                return false;
            }
            IntPtr selected = IntPtr.Zero;
            AdapterDescription description = default;
            int score = 0;
            bool ambiguous = false;
            try
            {
                var enumerate = GetMethod<EnumerateAdapters>(factory, ENUM_ADAPTERS_SLOT);
                for (uint index = 0; index < 64 && enumerate(factory, index, out IntPtr candidate) >= 0; index++)
                {
                    var describe = GetMethod<GetDescription>(candidate, GET_DESCRIPTION_SLOT);
                    if (describe(candidate, out AdapterDescription info) < 0)
                    {
                        Marshal.Release(candidate);
                        continue;
                    }
                    int candidateScore = Match(info);
                    if (candidateScore <= score)
                    {
                        ambiguous |= candidateScore > 0 && candidateScore == score;
                        Marshal.Release(candidate);
                        continue;
                    }
                    if (selected != IntPtr.Zero)
                    {
                        Marshal.Release(selected);
                    }
                    ambiguous = false;
                    selected = candidate;
                    description = info;
                    score = candidateScore;
                }
                if (selected == IntPtr.Zero || ambiguous)
                {
                    return false;
                }
                DedicatedCapacity = description.DedicatedVideoMemory.ToUInt64();
                SharedCapacity = description.SharedSystemMemory.ToUInt64();
                Guid memoryId = ADAPTER_MEMORY_ID;
                if (Marshal.QueryInterface(selected, ref memoryId, out _adapter) >= 0)
                {
                    _queryMemory = GetMethod<QueryMemory>(_adapter, QUERY_MEMORY_SLOT);
                }
                OpenDedicatedUsageCounter(description.AdapterLuidHigh, description.AdapterLuidLow);
                return true;
            }
            finally
            {
                if (selected != IntPtr.Zero)
                {
                    Marshal.Release(selected);
                }
                Marshal.Release(factory);
            }
        }

        private static int Match(AdapterDescription info)
        {
            if ((info.Flags & ADAPTER_FLAG_SOFTWARE) != 0)
            {
                return 0;
            }
            bool nameMatches = string.Equals(info.Description, SystemInfo.graphicsDeviceName, StringComparison.OrdinalIgnoreCase);
            if (SystemInfo.graphicsDeviceID == 0 || SystemInfo.graphicsDeviceVendorID == 0)
            {
                return nameMatches ? 1 : 0;
            }
            if (info.DeviceId != SystemInfo.graphicsDeviceID || info.VendorId != SystemInfo.graphicsDeviceVendorID)
            {
                return 0;
            }
            return nameMatches ? 2 : 1;
        }

        private void OpenDedicatedUsageCounter(int luidHigh, uint luidLow)
        {
            if (PdhOpenQuery(null, UIntPtr.Zero, out _counterQuery) != 0)
            {
                return;
            }
            string counter = $@"\GPU Adapter Memory(luid_0x{unchecked((uint)luidHigh):x8}_0x{luidLow:x8}_phys_0)\Dedicated Usage";
            if (PdhAddEnglishCounter(_counterQuery, counter, UIntPtr.Zero, out _dedicatedUsageCounter) != 0)
            {
                PdhCloseQuery(_counterQuery);
                _counterQuery = IntPtr.Zero;
                _dedicatedUsageCounter = IntPtr.Zero;
            }
        }

        private static T GetMethod<T>(IntPtr instance, int slot) where T : Delegate
        {
            IntPtr table = Marshal.ReadIntPtr(instance);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, slot * IntPtr.Size));
        }

        [DllImport("dxgi.dll", ExactSpelling = true)]
        private static extern int CreateDXGIFactory1(in Guid interfaceId, out IntPtr factory);

        [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PdhOpenQuery(string dataSource, UIntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PdhAddEnglishCounter(IntPtr query, string path, UIntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll", ExactSpelling = true)]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll", ExactSpelling = true)]
        private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out CounterValue value);

        [DllImport("pdh.dll", ExactSpelling = true)]
        private static extern uint PdhCloseQuery(IntPtr query);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumerateAdapters(IntPtr factory, uint index, out IntPtr adapter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDescription(IntPtr adapter, out AdapterDescription description);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryMemory(IntPtr adapter, uint nodeIndex, uint segment, out MemoryInfo info);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct AdapterDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
            internal uint VendorId;
            internal uint DeviceId;
            internal uint SubsystemId;
            internal uint Revision;
            internal UIntPtr DedicatedVideoMemory;
            internal UIntPtr DedicatedSystemMemory;
            internal UIntPtr SharedSystemMemory;
            internal uint AdapterLuidLow;
            internal int AdapterLuidHigh;
            internal uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryInfo
        {
            internal ulong Budget;
            internal ulong CurrentUsage;
            internal ulong AvailableForReservation;
            internal ulong CurrentReservation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CounterValue
        {
            internal uint Status;
            internal long Bytes;
        }
    }
}
