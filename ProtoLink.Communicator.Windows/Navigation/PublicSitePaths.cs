namespace ProtoLink.Communicator.Windows.Navigation;

/// <summary>Public Client SPA paths under <see cref="EntityLinks.DefaultPublicSiteBaseUrl"/>.</summary>
public static class PublicSitePaths
{
    public const string ForgotPassword = "forgot-password";

    /// <summary>Absolute URL for the web forgot-password page (MVP reset entry for native login).</summary>
    public static string BuildForgotPasswordUrl(string? publicSiteBaseUrl)
    {
        var baseUrl = (string.IsNullOrWhiteSpace(publicSiteBaseUrl)
            ? EntityLinks.DefaultPublicSiteBaseUrl
            : publicSiteBaseUrl.Trim()).TrimEnd('/');
        return $"{baseUrl}/{ForgotPassword}";
    }
}
