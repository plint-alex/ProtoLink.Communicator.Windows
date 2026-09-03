using System.Collections.Generic;

namespace ProtoLink.Communicator.Windows.Services;

public static class MimeTypes
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
    };

    public static string GetMimeType(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName);
        return Map.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
    }
}
