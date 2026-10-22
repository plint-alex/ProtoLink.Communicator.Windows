using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

/// <summary>Durable local Recent list for Cloud (C6) — no BE required.</summary>
public interface ICloudRecentStore
{
    IReadOnlyList<CloudRecentEntry> Load();

    /// <summary>Record a forward open: dedupe, newest first, cap <see cref="CloudRecentList.DefaultCapacity"/>.</summary>
    IReadOnlyList<CloudRecentEntry> RecordOpen(CloudRecentEntry entry);

    IReadOnlyList<CloudRecentEntry> Remove(Guid entityId);

    /// <summary>Replace the stored list (e.g. after pruning gone/forbidden ids).</summary>
    IReadOnlyList<CloudRecentEntry> Replace(IReadOnlyList<CloudRecentEntry> entries);
}
