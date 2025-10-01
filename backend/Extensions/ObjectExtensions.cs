using System.Reflection;
using System.Text.Json;

namespace NzbWebDAV.Extensions;

public static class ObjectExtensions
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private const BindingFlags BindingAttr = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static object? GetReflectionProperty(this object obj, string propertyName)
    {
        var type = obj.GetType();
        var prop = type.GetProperty(propertyName, BindingAttr);
        return prop?.GetValue(obj);
    }

    public static object? GetReflectionField(this object obj, string fieldName)
    {
        var type = obj.GetType();
        var prop = type.GetField(fieldName, BindingAttr);
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