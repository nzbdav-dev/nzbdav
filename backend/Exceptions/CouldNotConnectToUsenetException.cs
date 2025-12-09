namespace NzbWebDAV.Exceptions;

public class CouldNotConnectToUsenetException(string message, Exception? innerException = null)
    : RetryableDownloadException(message, innerException)
{
}