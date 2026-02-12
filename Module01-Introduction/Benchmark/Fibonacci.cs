using System.Collections.Generic;
using BenchmarkDotNet.Attributes;

namespace Dotnetos.AsyncExpert.Homework.Module01.Benchmark
{
    [DisassemblyDiagnoser(exportCombinedDisassemblyReport: true)]
    [MemoryDiagnoser]
    public class FibonacciCalc
    {
        [Benchmark(Baseline = true)]
        [ArgumentsSource(nameof(Data))]
        public ulong Recursive(ulong n)
        {
            if (n == 1 || n == 2) return 1;
            return Recursive(n - 2) + Recursive(n - 1);
        }

        [Benchmark]
        [ArgumentsSource(nameof(Data))]
        public ulong RecursiveWithMemoization(ulong n)
        {
            var cache = new Dictionary<ulong, ulong>();
            return FibMemo(n, cache);
        }

        private static ulong FibMemo(ulong n, Dictionary<ulong, ulong> cache)
        {
            if (n == 1 || n == 2) return 1;
            if (cache.TryGetValue(n, out var cached)) return cached;
            var result = FibMemo(n - 2, cache) + FibMemo(n - 1, cache);
            cache[n] = result;
            return result;
        }

        [Benchmark]
        [ArgumentsSource(nameof(Data))]
        public ulong Iterative(ulong n)
        {
            if (n == 1 || n == 2) return 1;
            ulong prev = 1, curr = 1;
            for (ulong i = 3; i <= n; i++)
            {
                var next = prev + curr;
                prev = curr;
                curr = next;
            }
            return curr;
        }

        public IEnumerable<ulong> Data()
        {
            yield return 15;
            yield return 35;
        }
    }
}
