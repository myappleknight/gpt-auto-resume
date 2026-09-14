using GPTAutoResume.Automation;
using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class WorkSelectionViewModelTests
{
    [Fact]
    public void ChangingSelectionNotifiesCallerImmediately()
    {
        var store = new FakeSelectionStore();
        var callbacks = 0;
        bool? lastEnabled = null;
        var viewModel = new WorkSelectionViewModel(
            new WorkSelectionRecord("work-a", "Example Work", false, DateTimeOffset.Now),
            store,
            enabled =>
            {
                callbacks++;
                lastEnabled = enabled;
            });

        viewModel.IsSelected = true;

        Assert.True(store.Enabled);
        Assert.Equal(1, callbacks);
        Assert.True(lastEnabled);
    }

    private sealed class FakeSelectionStore : IWorkSelectionStore
    {
        public bool Enabled { get; private set; }

        public IReadOnlyList<WorkSelectionRecord> Load() => [];
        public void Save(IReadOnlyList<WorkSelectionRecord> records) { }
        public bool IsAutoResumeEnabled(ConversationTargetIdentity? identity) => Enabled;
        public void UpsertRecent(string displayTitle, ConversationTargetIdentity identity, DateTimeOffset seenAt) { }
        public void RenameDisplayName(string conversationIdentityHash, string displayTitle) { }
        public void SetEnabled(string conversationIdentityHash, bool enabled) => Enabled = enabled;
    }
}
