using System.IO;
using System.Text.Json;
using GPTAutoResume.Automation;

namespace GPTAutoResume.Core;

public sealed record WorkSelectionRecord(
    string ConversationIdentityHash,
    string DisplayTitle,
    bool AutoResumeEnabled,
    DateTimeOffset LastSeenAt,
    bool IsStale = false,
    bool IsDisplayNameCustom = false);

public interface IWorkSelectionStore
{
    IReadOnlyList<WorkSelectionRecord> Load();
    void Save(IReadOnlyList<WorkSelectionRecord> records);
    bool IsAutoResumeEnabled(ConversationTargetIdentity? identity);
    void UpsertRecent(string displayTitle, ConversationTargetIdentity identity, DateTimeOffset seenAt);
    void RenameDisplayName(string conversationIdentityHash, string displayTitle);
    void SetEnabled(string conversationIdentityHash, bool enabled);
}

public sealed class JsonWorkSelectionStore : IWorkSelectionStore
{
    private const int MaxRecords = 5;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public JsonWorkSelectionStore()
        : this(Path.Combine(LocalAppPaths.AppDataRoot, "work-selections.json"))
    {
    }

    public JsonWorkSelectionStore(string path)
    {
        _path = path;
    }

    public IReadOnlyList<WorkSelectionRecord> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            return CoalesceDuplicateIdentities(JsonSerializer.Deserialize<List<WorkSelectionRecord>>(File.ReadAllText(_path)) ?? []);
        }
        catch
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<WorkSelectionRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var trimmed = CoalesceDuplicateIdentities(records)
            .OrderByDescending(record => record.LastSeenAt)
            .Take(MaxRecords)
            .ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(trimmed, JsonOptions));
    }

    public bool IsAutoResumeEnabled(ConversationTargetIdentity? identity)
    {
        if (identity is null || !identity.HasUsableSignal)
        {
            return false;
        }

        var hash = BuildHash(identity);
        return Load().FirstOrDefault(record =>
            string.Equals(record.ConversationIdentityHash, hash, StringComparison.Ordinal)) is { AutoResumeEnabled: true, IsStale: false };
    }

    public void UpsertRecent(string displayTitle, ConversationTargetIdentity identity, DateTimeOffset seenAt)
    {
        if (!identity.HasUsableSignal)
        {
            return;
        }

        var hash = BuildHash(identity);
        var records = Load().ToList();
        var existing = records.FindIndex(record => string.Equals(record.ConversationIdentityHash, hash, StringComparison.Ordinal));
        var title = string.IsNullOrWhiteSpace(displayTitle) ? "Codex / Work" : displayTitle.Trim();
        if (existing >= 0)
        {
            var old = records[existing];
            records[existing] = old with
            {
                DisplayTitle = old.IsDisplayNameCustom ? old.DisplayTitle : title,
                LastSeenAt = seenAt,
                IsStale = false
            };
        }
        else
        {
            records.Add(new WorkSelectionRecord(hash, title, AutoResumeEnabled: false, seenAt));
        }

        Save(records);
    }

    public void SetEnabled(string conversationIdentityHash, bool enabled)
    {
        var records = Load().ToList();
        var index = records.FindIndex(record => string.Equals(record.ConversationIdentityHash, conversationIdentityHash, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        records[index] = records[index] with { AutoResumeEnabled = enabled };
        Save(records);
    }

    public void RenameDisplayName(string conversationIdentityHash, string displayTitle)
    {
        var trimmed = displayTitle.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        var records = Load().ToList();
        var index = records.FindIndex(record => string.Equals(record.ConversationIdentityHash, conversationIdentityHash, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        records[index] = records[index] with
        {
            DisplayTitle = trimmed,
            IsDisplayNameCustom = true
        };
        Save(records);
    }

    public static string BuildHash(ConversationTargetIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.ConversationTitleHash)
            || !string.IsNullOrWhiteSpace(identity.SelectedItemIdentity))
        {
            var stableRaw = string.Join("|",
                identity.SurfaceType,
                identity.ConversationTitleHash,
                identity.SelectedItemIdentity,
                identity.ContainerAutomationId);
            return ConversationTargetIdentity.Hash(stableRaw);
        }

        var raw = string.Join("|",
            identity.SurfaceType,
            identity.ConversationTitleHash,
            identity.SelectedItemIdentity,
            identity.ContainerAutomationId,
            identity.ContainerNameHash,
            identity.ComposerIdentity);
        return ConversationTargetIdentity.Hash(raw);
    }

    private static IReadOnlyList<WorkSelectionRecord> CoalesceDuplicateIdentities(IReadOnlyList<WorkSelectionRecord> records) =>
        records.GroupBy(record => record.ConversationIdentityHash, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(record => record.LastSeenAt).First())
            .ToList();
}

public sealed class AllowAllWorkSelectionStore : IWorkSelectionStore
{
    public IReadOnlyList<WorkSelectionRecord> Load() => [];
    public void Save(IReadOnlyList<WorkSelectionRecord> records) { }
    public bool IsAutoResumeEnabled(ConversationTargetIdentity? identity) => true;
    public void UpsertRecent(string displayTitle, ConversationTargetIdentity identity, DateTimeOffset seenAt) { }
    public void RenameDisplayName(string conversationIdentityHash, string displayTitle) { }
    public void SetEnabled(string conversationIdentityHash, bool enabled) { }
}
