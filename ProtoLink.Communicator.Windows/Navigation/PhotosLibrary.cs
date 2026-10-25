using System.Globalization;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>E7 Photos library pointer on Photos host (<c>photosfolder:{guid}</c>).</summary>
public static class PhotosLibrary
{
    public const string HostCode = "Photos";
    public const string FolderPrefix = "photosfolder:";

    /// <summary>Parse library CloudFolder id from Photos host string values.</summary>
    public static Guid? ParseFolderId(IEnumerable<EntityValue>? values)
    {
        if (values == null) return null;
        foreach (var v in values)
        {
            var raw = ValueAsString(v.Value);
            if (string.IsNullOrEmpty(raw)) continue;
            if (!raw.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var idPart = raw[FolderPrefix.Length..].Trim();
            if (Guid.TryParse(idPart, out var id) && id != Guid.Empty)
                return id;
        }
        return null;
    }

    /// <summary>Find Photos host among user children and return its library folder id.</summary>
    public static Guid? ResolveLibraryIdFromUserChildren(IEnumerable<GetEntitiesResult>? userChildren)
    {
        if (userChildren == null) return null;
        foreach (var e in userChildren)
        {
            if (!string.Equals(e.Code, HostCode, StringComparison.Ordinal)) continue;
            var id = ParseFolderId(e.Values);
            if (id != null) return id;
        }
        return null;
    }

    private static string? ValueAsString(object? value)
    {
        if (value == null) return null;
        if (value is string s) return s;
        if (value is Guid g) return g.ToString("D", CultureInfo.InvariantCulture);
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
