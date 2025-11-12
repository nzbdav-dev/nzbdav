namespace NzbWebDAV.Utils;

public static class DebounceUtil
{
    public static Action<Action> CreateDebounce(TimeSpan timespan)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timespan, TimeSpan.Zero);
        var synchronizationLock = new object();
        DateTime lastInvocationTime = default;
        return actionToMaybeInvoke =>
        {
            var now = DateTime.Now;
            bool shouldInvoke;
            lock (synchronizationLock)
            {
                if (now - lastInvocationTime >= timespan)
                {
                    lastInvocationTime = now;
                    shouldInvoke = true;
                }
                else
                {
                    shouldInvoke = false;
                }
            }

            if (shouldInvoke)
                actionToMaybeInvoke?.Invoke();
        };
    }

    public static Action<Action> RunOnlyOnce()
    {
        var isAlreadyRan = false;
        return actionToMaybeInvoke =>
        {
            if (isAlreadyRan) return;
            isAlreadyRan = true;
            actionToMaybeInvoke?.Invoke();
        };
    }
}