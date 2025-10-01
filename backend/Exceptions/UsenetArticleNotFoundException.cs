namespace NzbWebDAV.Exceptions;

public class UsenetArticleNotFoundException(string segmentId)
    : NonRetryableDownloadException($"Article with message-id {segmentId} not found.")
{
}