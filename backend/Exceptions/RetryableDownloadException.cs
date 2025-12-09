namespace NzbWebDAV.Exceptions;

public class RetryableDownloadException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
}