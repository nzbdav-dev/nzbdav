namespace NzbWebDAV.Api.Controllers.ListWebdavDirectory;

public class ListWebdavDirectoryResponse
{
    public List<DirectoryItem> Items { get; init; } = new();

    public class DirectoryItem
    {
        public string Name { get; init; } = null!;
        public bool IsDirectory { get; init; }
        public long? Size { get; init; }
        public Guid? NzbBlobId { get; init; }
    }
}