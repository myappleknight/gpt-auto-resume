using GPTAutoResume.Automation;
using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class WorkSelectionStoreTests
{
    [Fact]
    public void AppRestartKeepsSelectionByIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-{Guid.NewGuid():N}", "work-selections.json");
        var identity = Identity("work-a");
        var first = new JsonWorkSelectionStore(path);

        first.UpsertRecent("Same title", identity, DateTimeOffset.Now);
        first.SetEnabled(JsonWorkSelectionStore.BuildHash(identity), true);

        var second = new JsonWorkSelectionStore(path);

        Assert.True(second.IsAutoResumeEnabled(identity));
    }

    [Fact]
    public void SameTitleDifferentIdentityIsNotSelected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-{Guid.NewGuid():N}", "work-selections.json");
        var selected = Identity("work-a");
        var different = Identity("work-b");
        var store = new JsonWorkSelectionStore(path);

        store.UpsertRecent("修正 WordPress 外掛", selected, DateTimeOffset.Now);
        store.UpsertRecent("修正 WordPress 外掛", different, DateTimeOffset.Now);
        store.SetEnabled(JsonWorkSelectionStore.BuildHash(selected), true);

        Assert.True(store.IsAutoResumeEnabled(selected));
        Assert.False(store.IsAutoResumeEnabled(different));
    }

    [Fact]
    public void KeepsOnlyFiveMostRecentWorks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-{Guid.NewGuid():N}", "work-selections.json");
        var store = new JsonWorkSelectionStore(path);
        var now = DateTimeOffset.Now;

        for (var i = 0; i < 7; i++)
        {
            store.UpsertRecent($"Work {i}", Identity($"work-{i}"), now.AddMinutes(i));
        }

        var records = store.Load();

        Assert.Equal(5, records.Count);
        Assert.DoesNotContain(records, record => record.DisplayTitle == "Work 0");
        Assert.DoesNotContain(records, record => record.DisplayTitle == "Work 1");
    }

    [Fact]
    public void RenameDisplayNameDoesNotChangeIdentitySelection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-{Guid.NewGuid():N}", "work-selections.json");
        var identity = Identity("work-a");
        var store = new JsonWorkSelectionStore(path);

        store.UpsertRecent("未命名 Work", identity, DateTimeOffset.Now);
        store.SetEnabled(JsonWorkSelectionStore.BuildHash(identity), true);
        store.RenameDisplayName(JsonWorkSelectionStore.BuildHash(identity), "Example Developer API");

        var record = Assert.Single(store.Load());
        Assert.Equal("Example Developer API", record.DisplayTitle);
        Assert.True(record.IsDisplayNameCustom);
        Assert.True(store.IsAutoResumeEnabled(identity));
    }

    [Fact]
    public void SameComposerDifferentConversationTitleKeepsSeparateWorks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-{Guid.NewGuid():N}", "work-selections.json");
        var store = new JsonWorkSelectionStore(path);

        store.UpsertRecent("Work A", Identity("work-a"), DateTimeOffset.Now);
        store.UpsertRecent("Work B", Identity("work-b"), DateTimeOffset.Now.AddMinutes(1));

        var records = store.Load();

        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record.DisplayTitle == "Work A");
        Assert.Contains(records, record => record.DisplayTitle == "Work B");
    }

    [Fact]
    public void UpsertRecentPreservesCustomDisplayName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-{Guid.NewGuid():N}", "work-selections.json");
        var identity = Identity("work-a");
        var store = new JsonWorkSelectionStore(path);

        store.UpsertRecent("Auto detected title", identity, DateTimeOffset.Now);
        store.RenameDisplayName(JsonWorkSelectionStore.BuildHash(identity), "My Work Name");
        store.UpsertRecent("Different auto title", identity, DateTimeOffset.Now.AddMinutes(1));

        var record = Assert.Single(store.Load());
        Assert.Equal("My Work Name", record.DisplayTitle);
        Assert.True(record.IsDisplayNameCustom);
    }

    [Fact]
    public void ConversationTitleHashIgnoresVolatileComposerIdentity()
    {
        var first = new ConversationTargetIdentity(
            SurfaceType: "ControlType.Document",
            ConversationTitleHash: ConversationTargetIdentity.Hash("same-work"),
            SelectedItemIdentity: "",
            ContainerAutomationId: "RootWebArea",
            ContainerNameHash: ConversationTargetIdentity.Hash("ChatGPT"),
            ComposerIdentity: ConversationTargetIdentity.Hash("ProseMirror-focused"));
        var second = first with
        {
            ComposerIdentity = ConversationTargetIdentity.Hash("ProseMirror")
        };

        Assert.Equal(JsonWorkSelectionStore.BuildHash(first), JsonWorkSelectionStore.BuildHash(second));
    }

    [Fact]
    public void TruncatedTitlesNeverTransferPermissionAcrossIdentities()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-{Guid.NewGuid():N}", "work-selections.json");
        var store = new JsonWorkSelectionStore(path);
        var now = DateTimeOffset.Now;

        store.Save([
            new WorkSelectionRecord("old-hash", "Long truncated Work title…", true, now),
            new WorkSelectionRecord("new-hash", "Long truncated Work title…", false, now.AddMinutes(1)),
            new WorkSelectionRecord("other", "Example service app", false, now.AddMinutes(2))
        ]);

        var records = store.Load();

        Assert.Equal(3, records.Count);
        Assert.True(Assert.Single(records, record => record.ConversationIdentityHash == "old-hash").AutoResumeEnabled);
        Assert.False(Assert.Single(records, record => record.ConversationIdentityHash == "new-hash").AutoResumeEnabled);
    }

    [Fact]
    public void UniqueEnabledTitleTransfersPermissionToRefreshedIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-{Guid.NewGuid():N}", "work-selections.json");
        var store = new JsonWorkSelectionStore(path);
        var now = DateTimeOffset.Now;
        var oldIdentity = Identity("old-volatile-title");
        var refreshedIdentity = Identity("stable-header-title");

        store.UpsertRecent("升級 Facebook Token 前台工具", oldIdentity, now);
        store.SetEnabled(JsonWorkSelectionStore.BuildHash(oldIdentity), true);
        store.UpsertRecent("升級 Facebook Token 前台工具", refreshedIdentity, now.AddMinutes(1));

        Assert.True(store.IsAutoResumeEnabled(refreshedIdentity));
        Assert.False(store.IsAutoResumeEnabled(oldIdentity));
        Assert.Contains(store.Load(), record => record.ConversationIdentityHash == JsonWorkSelectionStore.BuildHash(oldIdentity)
            && record.IsStale);
    }

    [Fact]
    public void AmbiguousTitleDoesNotTransferPermissionToNewIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gpt-auto-resume-{Guid.NewGuid():N}", "work-selections.json");
        var store = new JsonWorkSelectionStore(path);
        var now = DateTimeOffset.Now;
        var enabled = Identity("enabled");
        var disabled = Identity("disabled");
        var refreshed = Identity("refreshed");

        store.Save([
            new WorkSelectionRecord(JsonWorkSelectionStore.BuildHash(enabled), "Same Work", true, now),
            new WorkSelectionRecord(JsonWorkSelectionStore.BuildHash(disabled), "Same Work", false, now.AddSeconds(1))
        ]);
        store.UpsertRecent("Same Work", refreshed, now.AddSeconds(2));

        Assert.False(store.IsAutoResumeEnabled(refreshed));
        Assert.True(store.IsAutoResumeEnabled(enabled));
    }

    private static ConversationTargetIdentity Identity(string value) => new(
        SurfaceType: "ControlType.Document",
        ConversationTitleHash: ConversationTargetIdentity.Hash(value),
        SelectedItemIdentity: "",
        ContainerAutomationId: "RootWebArea",
        ContainerNameHash: "",
        ComposerIdentity: ConversationTargetIdentity.Hash("composer"));
}
