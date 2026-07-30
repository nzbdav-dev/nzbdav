using System.Numerics;

namespace NzbWebDAV.Streams;

/// <summary>
/// Adapts BODY pipeline batch width from consumer readiness at segment boundaries.
/// Narrows 4→2→1 when prefetch starves; recovers gradually after sustained readiness.
/// </summary>
internal sealed class AdaptiveBodyBatchSizer(int maximumBatchSize)
{
    private const int ObservationWindow = 8;
    private const int StarvedBoundariesToNarrow = 2;
    private const int ReadyBoundariesToRecover = 16;

    private readonly int _maximum = Math.Max(1, maximumBatchSize);
    private int _current = Math.Max(1, maximumBatchSize);
    private byte _starvationWindow;
    private int _observations;
    private int _ready;

    public int Current => Volatile.Read(ref _current);

    public BatchSizeChange? Observe(bool readyWhenNeeded)
    {
        var current = Current;
        _starvationWindow = (byte)((_starvationWindow << 1) | (readyWhenNeeded ? 0 : 1));
        _observations = Math.Min(ObservationWindow, _observations + 1);

        if (readyWhenNeeded)
            _ready++;
        else
            _ready = 0;

        int? next = null;
        if (_observations == ObservationWindow
            && BitOperations.PopCount(_starvationWindow) >= StarvedBoundariesToNarrow)
        {
            next = Math.Max(1, (current + 1) / 2);
        }
        else if (_ready >= ReadyBoundariesToRecover)
        {
            next = Math.Min(_maximum, current * 2);
        }

        if (next is null) return null;
        _starvationWindow = 0;
        _observations = 0;
        _ready = 0;
        if (next == current) return null;
        Volatile.Write(ref _current, next.Value);
        return new BatchSizeChange(current, next.Value, readyWhenNeeded);
    }
}

internal readonly record struct BatchSizeChange(int Previous, int Current, bool ReadyWhenNeeded);
