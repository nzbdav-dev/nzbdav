// ReSharper disable InconsistentNaming

namespace NzbWebDAV.Extensions;

public static class IEnumerableExtensions
{
    public static IEnumerable<List<T>> ToBatches<T>(this IEnumerable<T> items, int batchSize)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "Batch size must be greater than zero."
            );

        var batch = new List<T>(batchSize);
        foreach (var item in items)
        {
            batch.Add(item);
            if (batch.Count >= batchSize)
            {
                yield return batch;
                batch = new List<T>(batchSize);
            }
        }

        if (batch.Count > 0)
            yield return batch;
    }
}