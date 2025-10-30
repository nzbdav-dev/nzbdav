namespace NzbWebDAV.Database.Models;

public class QueueNzbContents
{
    public Guid Id { get; set; }
    public string NzbContents { get; set; } = null!;

    // navigation helpers
    public QueueItem? QueueItem { get; set; }
}