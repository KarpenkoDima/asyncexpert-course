using System;
using System.Runtime.CompilerServices;

namespace AwaitableExercises.Core
{
    public static class BoolExtensions
    {
        public static BoolAwaiter GetAwaiter(this bool value)
        {
            return new BoolAwaiter(value);
        }
    }

    public class BoolAwaiter : INotifyCompletion
    {
        private readonly bool _value;

        public BoolAwaiter(bool value)
        {
            _value = value;
        }

        public bool IsCompleted => true;

        public bool GetResult() => _value;

        public void OnCompleted(Action continuation)
        {
            continuation();
        }
    }
}
