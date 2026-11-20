using System.Windows.Threading;
using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Utilities;

/// <summary>
/// Messenger contacts-list startup contract (#messenger-startup-priority / #contacts-responsiveness).
/// Paint loading chrome immediately; prefer Interactive/Input over Sync; never silent-empty while fetch runs.
/// </summary>
public static class MessengerContactsLoad
{
    /// <summary>
    /// Leave contacts Loading within this budget from load start (pair with WaitAsync on bag/kids HTTP).
    /// </summary>
    public static readonly TimeSpan InteractiveHttpTimeout = TimeSpan.FromSeconds(12);

    /// <summary>UI work for contacts list must not wait behind Background enrichment / Sync deferrals.</summary>
    public static DispatcherPriority ListPaintPriority { get; } = DispatcherPriority.Input;

    public const string LoadingStatus = "Loading…";
    public const string EmptyHint = "No contacts yet. Add someone in Settings.";
    public const string LoadFailedSoft = "Couldn't load contacts.";

    /// <summary>Show loading chrome while fetch is in flight (even if list still empty).</summary>
    public static bool ShouldShowLoading(bool isLoading) => isLoading;

    /// <summary>True empty bag after a finished load — never confuse with in-flight empty.</summary>
    public static bool ShouldShowEmpty(bool isLoading, int contactCount) =>
        !isLoading && contactCount <= 0;

    /// <summary>Extract contact user id from a UserContacts child entity (values or code).</summary>
    public static Guid? TryParseContactUserId(IReadOnlyList<EntityValue>? values, string? code)
    {
        if (values != null)
        {
            foreach (var ev in values)
            {
                if (ev.Type != "StringValue") continue;
                var s = ev.Value?.ToString();
                if (!string.IsNullOrEmpty(s)
                    && s.StartsWith("userid:", StringComparison.Ordinal)
                    && Guid.TryParse(s.AsSpan(7), out var uid)
                    && uid != Guid.Empty)
                    return uid;
            }
        }

        if (!string.IsNullOrEmpty(code) && Guid.TryParse(code, out var codeUid) && codeUid != Guid.Empty)
            return codeUid;
        return null;
    }
}
