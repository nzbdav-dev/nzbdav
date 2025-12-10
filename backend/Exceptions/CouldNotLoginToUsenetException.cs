namespace NzbWebDAV.Exceptions;

public class CouldNotLoginToUsenetException(string message, Exception? innerException = null)
    : RetryableDownloadException(message, innerException)
{
}