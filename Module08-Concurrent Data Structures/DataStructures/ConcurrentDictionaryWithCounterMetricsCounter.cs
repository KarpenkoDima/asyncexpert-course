using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DataStructures
{
    public class ConcurrentDictionaryWithCounterMetricsCounter : IMetricsCounter
    {
        private readonly ConcurrentDictionary<string, AtomicCounter> _dictionary = new ConcurrentDictionary<string, AtomicCounter>();

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
        {
            foreach (var kvp in _dictionary)
            {
                yield return new KeyValuePair<string, int>(kvp.Key, kvp.Value.Count);
            }
        }

        public void Increment(string key)
        {
            var counter = _dictionary.GetOrAdd(key, _ => new AtomicCounter());
            counter.Increment();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public class AtomicCounter
        {
            int value;

            public void Increment() => Interlocked.Increment(ref value);

            public int Count => Volatile.Read(ref value);
        }
    }
}
