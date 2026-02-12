using System.Threading;

namespace LowLevelExercises.Core
{
    /// <summary>
    /// A simple class for reporting a specific value and obtaining an average.
    /// Lock-free implementation using Interlocked and Volatile.
    /// </summary>
    public class AverageMetric
    {
        int sum = 0;
        int count = 0;

        public void Report(int value)
        {
            Interlocked.Add(ref sum, value);
            Interlocked.Increment(ref count);
        }

        public double Average
        {
            get
            {
                var currentCount = Volatile.Read(ref count);
                var currentSum = Volatile.Read(ref sum);
                return Calculate(currentCount, currentSum);
            }
        }

        static double Calculate(in int count, in int sum)
        {
            // DO NOT change the way calculation is done.

            if (count == 0)
            {
                return double.NaN;
            }

            return (double)sum / count;
        }
    }
}
