using System;

namespace Rvm.Benchmark
{
    internal static class AllocationCounterControl
    {
        private const int PAYLOAD_BYTES = 65536;

        internal static Result Measure()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var allocation = new byte[PAYLOAD_BYTES];
            allocation[PAYLOAD_BYTES - 1] = 1;
            long observed = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(allocation);
            return new Result { threadId = Environment.CurrentManagedThreadId, payloadBytes = PAYLOAD_BYTES, observedBytes = observed, valid = observed >= PAYLOAD_BYTES };
        }

        [Serializable]
        internal sealed class Result
        {
            public int threadId;
            public int payloadBytes;
            public long observedBytes;
            public bool valid;
        }
    }
}
