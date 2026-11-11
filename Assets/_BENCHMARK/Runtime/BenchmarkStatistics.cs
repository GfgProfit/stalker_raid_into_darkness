using System;

namespace Rvm.Benchmark
{
    public static class BenchmarkStatistics
    {
        public static BenchmarkDistribution Distribution(double[] input)
        {
            var sorted = (double[])input.Clone();
            int count = 0;
            for (int i = 0; i < sorted.Length; i++)
            {
                if (!double.IsNaN(sorted[i]) && !double.IsInfinity(sorted[i]) && sorted[i] >= 0)
                {
                    sorted[count++] = sorted[i];
                }
            }
            Array.Sort(sorted, 0, count);
            var result = new BenchmarkDistribution { available = count > 0, samples = count };
            if (count == 0)
            {
                return result;
            }
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                sum += sorted[i];
            }
            result.mean = sum / count;
            result.min = sorted[0];
            result.max = sorted[count - 1];
            result.p50 = Percentile(sorted, count, 0.5);
            result.p90 = Percentile(sorted, count, 0.9);
            result.p95 = Percentile(sorted, count, 0.95);
            result.p99 = Percentile(sorted, count, 0.99);
            return result;
        }

        public static double Percentile(double[] sorted, int count, double fraction)
        {
            if (count <= 0 || count > sorted.Length || fraction < 0 || fraction > 1 || double.IsNaN(fraction))
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            double index = (count - 1) * fraction;
            int lower = (int)index;
            int upper = Math.Min(lower + 1, count - 1);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
        }

        public static BenchmarkFrameStats Frames(double[] milliseconds)
        {
            var sorted = new double[milliseconds.Length];
            int count = 0;
            for (int i = 0; i < milliseconds.Length; i++)
            {
                if (milliseconds[i] > 0 && !double.IsNaN(milliseconds[i]) && !double.IsInfinity(milliseconds[i]))
                {
                    sorted[count++] = milliseconds[i];
                }
            }
            Array.Resize(ref sorted, count);
            Array.Sort(sorted);
            var result = new BenchmarkFrameStats { frameTime = Distribution(sorted) };
            if (count == 0)
            {
                return result;
            }
            result.averageFps = 1000 / result.frameTime.mean;
            result.minFps = 1000 / result.frameTime.max;
            result.maxFps = 1000 / result.frameTime.min;
            result.low1Available = count >= 100;
            result.low01Available = count >= 1000;
            if (result.low1Available)
            {
                result.low1Fps = Low(sorted, 0.01);
            }
            if (result.low01Available)
            {
                result.low01Fps = Low(sorted, 0.001);
            }
            return result;
        }

        private static double Low(double[] sorted, double fraction)
        {
            int slowCount = Math.Max(1, (int)Math.Ceiling(sorted.Length * fraction));
            double sum = 0;
            for (int i = sorted.Length - slowCount; i < sorted.Length; i++)
            {
                sum += sorted[i];
            }
            return 1000 * slowCount / sum;
        }
    }
}
