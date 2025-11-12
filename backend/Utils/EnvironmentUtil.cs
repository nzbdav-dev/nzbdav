namespace NzbWebDAV.Utils;

public static class EnvironmentUtil
{
    public static string GetVariable(string envVariable)
    {
        return Environment.GetEnvironmentVariable(envVariable) ??
               throw new Exception($"The environment variable `{envVariable}` must be set.");
    }

    public static long? GetLongVariable(string envVariable)
    {
        return long.TryParse(Environment.GetEnvironmentVariable(envVariable), out var longValue) ? longValue : null;
    }

    public static bool IsVariableTrue(string envVariable)
    {
        var value = Environment.GetEnvironmentVariable(envVariable)?.ToLower();
        return value is "y" or "yes" or "true";
    }
}