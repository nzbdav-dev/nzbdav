namespace NzbWebDAV.Utils;

public static class ExceptionUtil
{
    public static void Try(Action action)
    {
        try
        {
            action?.Invoke();
        }
        catch
        {
            // intentionally ignore
        }
    }
}