using System.Text.Json.Serialization;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Database.Models;

public class DavCleanupItem
{
    public Guid Id { get; set; }
}