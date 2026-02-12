using System;
using System.Threading;

namespace Synchronization.Core
{
    public class NamedExclusiveScope : IDisposable
    {
        private readonly Mutex _mutex;

        public NamedExclusiveScope(string name, bool isSystemWide)
        {
            if (isSystemWide)
            {
                _mutex = new Mutex(false, $"Global\\{name}");
                if (!_mutex.WaitOne(0))
                {
                    _mutex.Dispose();
                    throw new InvalidOperationException($"Unable to get a global lock {name}.");
                }
            }
            else
            {
                _mutex = new Mutex(false, name);
                _mutex.WaitOne();
            }
        }

        public void Dispose()
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
