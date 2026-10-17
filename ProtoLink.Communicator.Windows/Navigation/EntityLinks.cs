namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>Public entity URL, same format the web client copies: <c>{PublicSiteBaseUrl}/{id:D}</c>.</summary>
public static class EntityLinks
{
    public const string DefaultPublicSiteBaseUrl = "https://protolink.ru/";

    public static string Build(string? publicSiteBaseUrl, Guid entityId, string? uiLanguage = null)
    {
        var baseUrl = (string.IsNullOrWhiteSpace(publicSiteBaseUrl) ? DefaultPublicSiteBaseUrl : publicSiteBaseUrl.Trim()).TrimEnd('/');
        var url = $"{baseUrl}/{entityId:D}";
        return string.IsNullOrWhiteSpace(uiLanguage) ? url : $"{url}?lang={Uri.EscapeDataString(uiLanguage.Trim())}";
    }
}
