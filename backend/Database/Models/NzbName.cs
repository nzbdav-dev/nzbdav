namespace NzbWebDAV.Database.Models;

public class NzbName
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = null!;
}
