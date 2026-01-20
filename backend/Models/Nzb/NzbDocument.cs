using System.Xml;

namespace NzbWebDAV.Models.Nzb;

public class NzbDocument
{
    private static readonly XmlReaderSettings XmlSettings = new()
    {
        Async = true,
        DtdProcessing = DtdProcessing.Ignore
    };

    public Dictionary<string, string> Metadata { get; } = new();

    public List<NzbFile> Files { get; } = [];

    public static async Task<NzbDocument> LoadAsync(Stream stream)
    {
        var document = new NzbDocument();
        using var reader = XmlReader.Create(stream, XmlSettings);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            switch (reader.Name)
            {
                case "head":
                    await ReadHeadAsync(reader, document.Metadata).ConfigureAwait(false);
                    break;
                case "file":
                    var file = await ReadFileAsync(reader).ConfigureAwait(false);
                    document.Files.Add(file);
                    break;
            }
        }

        return document;
    }

    private static async Task ReadHeadAsync(XmlReader reader, Dictionary<string, string> metadata)
    {
        if (reader.IsEmptyElement)
            return;

        while (true)
        {
            if (reader is { NodeType: XmlNodeType.EndElement, Name: "head" })
                break;

            if (reader is { NodeType: XmlNodeType.Element, Name: "meta" })
            {
                var type = reader.GetAttribute("type") ?? string.Empty;
                var value = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                metadata.Add(type, value);

                // ReadElementContentAsStringAsync advances the reader - continue to check current position
                continue;
            }

            // Only read if we haven't processed an element that advanced us
            if (!await reader.ReadAsync().ConfigureAwait(false))
                break;
        }
    }

    private static async Task<NzbFile> ReadFileAsync(XmlReader reader)
    {
        var file = new NzbFile
        {
            Subject = reader.GetAttribute("subject") ?? string.Empty
        };

        if (reader.IsEmptyElement)
            return file;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            if (reader is { NodeType: XmlNodeType.EndElement, Name: "file" })
                break;

            if (reader is { NodeType: XmlNodeType.Element, Name: "segments" })
            {
                await ReadSegmentsAsync(reader, file).ConfigureAwait(false);
            }
        }

        return file;
    }

    private static async Task ReadSegmentsAsync(XmlReader reader, NzbFile file)
    {
        if (reader.IsEmptyElement)
            return;

        while (true)
        {
            if (reader is { NodeType: XmlNodeType.EndElement, Name: "segments" })
                break;

            if (reader is { NodeType: XmlNodeType.Element, Name: "segment" })
            {
                var bytesAttr = reader.GetAttribute("bytes");
                var segment = new NzbSegment
                {
                    Bytes = long.TryParse(bytesAttr, out var bytes) ? bytes : 0,
                    MessageId = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false)
                };
                file.Segments.Add(segment);

                // ReadElementContentAsStringAsync advances the reader - continue to check current position
                continue;
            }

            // Only read if we haven't processed an element that advanced us
            if (!await reader.ReadAsync().ConfigureAwait(false))
                break;
        }
    }
}