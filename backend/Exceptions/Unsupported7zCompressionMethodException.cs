namespace NzbWebDAV.Exceptions;

// ReSharper disable once InconsistentNaming
public class Unsupported7zCompressionMethodException(string message) : NonRetryableDownloadException(message)
{
}