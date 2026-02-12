using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DataStructures
{
    public class ConcurrentDictionaryOnlyMetricsCounter : IMetricsCounter
    {
        private readonly ConcurrentDictionary<string, int> _dictionary = new ConcurrentDictionary<string, int>();

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        public void Increment(string key)
        {
            _dictionary.AddOrUpdate(key, 1, (_, oldValue) => oldValue + 1);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
