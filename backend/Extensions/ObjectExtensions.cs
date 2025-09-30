using System.Reflection;
using System.Text.Json;

namespace NzbWebDAV.Extensions;

public static class ObjectExtensions
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static object? GetReflectionProperty(this object obj, string propertyName)
    {
        var type = obj.GetType();
        var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return prop?.GetValue(obj);
    }

    public static string ToJson(this object obj)
    {
        return JsonSerializer.Serialize(obj);
    }

    public static string ToIndentedJson(this object obj)
    {
        return JsonSerializer.Serialize(obj, Indented);
    }
}