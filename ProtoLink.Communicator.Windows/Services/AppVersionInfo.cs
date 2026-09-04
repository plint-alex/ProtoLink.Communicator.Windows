using System.Reflection;

namespace ProtoLink.Communicator.Windows.Services;

public static class AppVersionInfo
{
    public static string Current
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return TruncateRevision(informational);
            return assembly.GetName().Version?.ToString(3) ?? "Unknown";
        }
    }

    public static string TruncateRevision(string version)
    {
        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }
}
