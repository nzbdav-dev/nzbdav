using System.Diagnostics;
using System.Reflection;

namespace NzbWebDAV.Utils;

public static class EmbeddedResourceUtil
{
    public static Stream GetStream(string resourcePath)
    {
        var assembly = Assembly.GetCallingAssembly();
        var fullResourcePath = GetFullResourcePath(resourcePath);
        return assembly.GetManifestResourceStream(fullResourcePath)!;
    }

    public static string ReadAllText(string resourcePath)
    {
        using var stream = GetStream(resourcePath);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static async Task<string> ReadAllTextAsync(string resourcePath)
    {
        await using var stream = GetStream(resourcePath);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static string GetFullResourcePath(string resourcePath)
    {
        if (GetAllResourcePaths().Any(x => x == resourcePath))
            return resourcePath;

        return new StackTrace()
            .GetFrames()
            .Select(x => x.GetMethod()!)
            .Select(x => x.ReflectedType!)
            .Where(x => !x.FullName!.StartsWith("NzbWebDAV.Utils.EmbeddedResourceUtil"))
            .First(x => !x.FullName!.StartsWith("System"))
            .Namespace + "." + resourcePath;
    }

    public static string[] GetAllResourcePaths()
    {
        return Assembly.GetCallingAssembly().GetManifestResourceNames();
    }
}