namespace NzbWebDAV.Exceptions;

public class PasswordProtected7zException(string message) : NonRetryableDownloadException(message)
{
}