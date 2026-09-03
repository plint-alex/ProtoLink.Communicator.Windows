using System.IO;
using System.Windows;
using Newtonsoft.Json;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

public static class ThemeManager
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    private const string ThemeUriLight =
        "pack://application:,,,/ProtoLink.Communicator.Windows;component/Themes/Theme.Light.xaml";
    private const string ThemeUriDark =
        "pack://application:,,,/ProtoLink.Communicator.Windows;component/Themes/Theme.Dark.xaml";

    /// <summary>Resolves saved value to <see cref="Light"/> or <see cref="Dark"/>.</summary>
    public static string Normalize(string? themeName) =>
        string.Equals(themeName, Dark, StringComparison.OrdinalIgnoreCase) ? Dark : Light;

    public static void Apply(string? themeName)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        var name = Normalize(themeName);
        var uri = new Uri(name == Dark ? ThemeUriDark : ThemeUriLight, UriKind.Absolute);

        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var s = merged[i].Source?.OriginalString ?? "";
            if (s.Contains("Themes/Theme.Light", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Themes/Theme.Dark", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Themes/AppTheme", StringComparison.OrdinalIgnoreCase))
                merged.RemoveAt(i);
        }

        merged.Insert(0, new ResourceDictionary { Source = uri });
    }

    /// <summary>Apply theme from saved settings (call early in <see cref="Application.OnStartup"/>).</summary>
    public static void ApplyFromSettingsFile()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(appData, "ProtoLinkCommunicator", "settings.json");
            if (!File.Exists(path))
            {
                Apply(Light);
                return;
            }

            var json = File.ReadAllText(path);
            var s = JsonConvert.DeserializeObject<AppSettings>(json);
            Apply(s?.Theme);
        }
        catch
        {
            Apply(Light);
        }
    }
}
