namespace NzbWebDAV.Clients.Usenet.Models;

public class UsenetArticle
{
    public UsenetArticleHeaders? Headers { get; init; }
    public required IEnumerable<string> Body { get; init; }
}