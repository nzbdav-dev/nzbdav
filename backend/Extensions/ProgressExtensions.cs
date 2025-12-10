namespace NzbWebDAV.Extensions;

public static class ProgressExtensions
{
    public static Progress<int> ToPercentage(this IProgress<int>? progress, int total)
    {
        return new Progress<int>(x => progress?.Report(x * 100 / total));
    }

    public static Progress<int> Scale(this IProgress<int>? progress, int numerator, int denominator)
    {
        return new Progress<int>(x => progress?.Report(x * numerator / denominator));
    }

    public static Progress<int> Offset(this IProgress<int>? progress, int offset)
    {
        return new Progress<int>(x => progress?.Report(x + offset));
    }

    public static MultiProgress ToMultiProgress(this IProgress<int>? progress, int total)
    {
        return new MultiProgress(progress, total);
    }

    public class MultiProgress(IProgress<int>? progress, int total)
    {
        private int _numerator;
        private readonly int _denominator = 100 * total;
        private readonly Lock _lock = new();

        public Progress<int> SubProgress
        {
            get
            {
                var previous = 0;
                return new Progress<int>(x =>
                {
                    int? current;
                    lock (_lock)
                    {
                        _numerator -= previous;
                        _numerator += x;
                        current = _numerator;
                    }

                    previous = x;
                    progress?.Report(current!.Value * 100 / _denominator);
                });
            }
        }
    }
}