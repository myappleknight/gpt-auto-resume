using GPTAutoResume.Automation;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class ResumeSenderTests
{
    private readonly TargetWindow _target = new(42, 100, "ChatGPT", "ChatGPT");

    [Fact]
    public void FocusClassDoesNotChangeComposerIdentityButOtherClassesDo()
    {
        Assert.Equal(ComposerText.StableClassName("ProseMirror"), ComposerText.StableClassName("ProseMirror ProseMirror-focused"));
        Assert.NotEqual(ComposerText.StableClassName("OtherEditor"), ComposerText.StableClassName("ProseMirror"));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "Work with ChatGPT\n")]
    public void PlaceholderRequiresStructuralEvidence(bool placeholder, string expected)
    {
        Assert.Equal(expected, ComposerText.Normalize("Work with ChatGPT\n", "Work with ChatGPT", placeholder));
        Assert.Equal("actual draft", ComposerText.Normalize("actual draft", "Work with ChatGPT", placeholder));
        Assert.Null(ComposerText.Normalize(null, "Work with ChatGPT", placeholder));
    }

    [Fact]
    public void ProseMirrorPlaceholderIsTreatedAsEmptyDraft()
    {
        Assert.Equal("", ComposerText.Normalize("\r\n與 ChatGPT 一起工作", "與 ChatGPT 一起工作", false, "ProseMirror ProseMirror-focused"));
    }

    [Theory]
    [InlineData("test", true)]
    [InlineData("other draft", false)]
    public void TestCleanupOnlyClearsExactAuthorizedMessage(string draft, bool expected)
    {
        var host = new FakeResumeHost { ForegroundWindow = 100, Text = draft };
        Assert.Equal(expected, new ResumeSender(host).TryClearMatchingDraft(_target, "test", () => true));
        Assert.Equal(expected ? "" : draft, host.Text);
        Assert.Equal(0, host.SendEnterCount);
    }

    [Fact]
    public void VerifiedInsertionWritesConfiguredTextWithoutEnter()
    {
        var host = new FakeResumeHost { ForegroundWindow = 100 };
        Assert.True(new ResumeSender(host).TrySend(_target, "custom message", false, false, () => true));
        Assert.Equal("custom message", host.Text);
        Assert.Equal(1, host.SetTextCount);
        Assert.Equal(0, host.SendEnterCount);
    }

    [Fact]
    public void ChangedConversationBlocksInsertion()
    {
        var host = new FakeResumeHost { ForegroundWindow = 100 };
        Assert.False(new ResumeSender(host).TrySend(_target, "continue", false, false, () => false));
        Assert.Equal(0, host.SetTextCount);
        Assert.Equal(0, host.FallbackTypeTextCount);
    }

    [Fact]
    public void ExistingDraftIsNotOverwritten()
    {
        var host = new FakeResumeHost { ForegroundWindow = 100, Text = "existing draft" };
        Assert.False(new ResumeSender(host).TrySend(_target, "continue", false, false));
        Assert.Equal(0, host.SetTextCount);
    }

    [Fact]
    public void ExistingAuthorizedResumeDraftCanBeSubmitted()
    {
        var host = new FakeResumeHost { ForegroundWindow = 100, Text = "請繼續" };

        Assert.True(new ResumeSender(host).TrySend(_target, "請繼續", false, true));

        Assert.Equal(0, host.SetTextCount);
        Assert.Equal(1, host.SendEnterCount);
    }

    [Fact]
    public void ExistingDifferentDraftStillBlocksSubmission()
    {
        var host = new FakeResumeHost { ForegroundWindow = 100, Text = "請繼續完成測試" };

        Assert.False(new ResumeSender(host).TrySend(_target, "請繼續", false, true));

        Assert.Equal(0, host.SetTextCount);
        Assert.Equal(0, host.SendEnterCount);
    }

    [Fact]
    public void ProviderReturningWithoutInsertingIsNotSuccess()
    {
        var host = new FakeResumeHost { ForegroundWindow = 100, IgnoreValue = true };
        Assert.False(new ResumeSender(host).TrySend(_target, "continue", false, true));
        Assert.Equal(0, host.SendEnterCount);
    }

    [Fact]
    public void ProviderReturningWithoutInsertingFallsBackToPaste()
    {
        var host = new FakeResumeHost { ForegroundWindow = 100, IgnoreValue = true, FallbackWritesText = true };

        Assert.True(new ResumeSender(host).TrySend(_target, "請繼續", false, true));

        Assert.Equal(1, host.SetTextCount);
        Assert.Equal(1, host.FallbackTypeTextCount);
        Assert.Equal(1, host.SendEnterCount);
        Assert.Equal("請繼續", host.Text);
    }

    [Fact]
    public void ForegroundChangeBeforeInputAbortsWithoutTyping()
    {
        var host = new FakeResumeHost
        {
            ForegroundWindow = 100,
            ChangeForegroundAfterFocus = 200
        };
        var sender = new ResumeSender(host);

        var sent = sender.TrySend(_target, "請繼續", dryRun: false, sendEnter: false);

        Assert.False(sent);
        Assert.Equal(0, host.SetTextCount);
        Assert.Equal(0, host.FallbackTypeTextCount);
        Assert.Equal(0, host.SendEnterCount);
    }

    [Fact]
    public void ForegroundChangeBeforeEnterAbortsWithoutSubmitting()
    {
        var host = new FakeResumeHost
        {
            ForegroundWindow = 100,
            ChangeForegroundAfterSetText = 200
        };
        var sender = new ResumeSender(host);

        var sent = sender.TrySend(_target, "請繼續", dryRun: false, sendEnter: true);

        Assert.False(sent);
        Assert.Equal(1, host.SetTextCount);
        Assert.Equal(0, host.SendEnterCount);
    }

    private sealed class FakeResumeHost : IResumeInteractionHost
    {
        public string Text { get; set; } = "";
        public bool IgnoreValue { get; set; }
        public bool FallbackWritesText { get; set; }
        public nint ForegroundWindow { get; set; }
        public nint? ChangeForegroundAfterFocus { get; set; }
        public nint? ChangeForegroundAfterSetText { get; set; }
        public int SetTextCount { get; private set; }
        public int FallbackTypeTextCount { get; private set; }
        public int SendEnterCount { get; private set; }

        public bool IsStillValid(TargetWindow target) => true;

        public void ActivateTargetWindow(nint hwnd)
        {
            ForegroundWindow = hwnd;
        }

        public nint GetForegroundWindow() => ForegroundWindow;

        public IChatInputController? FindChatInput(nint hwnd) => new FakeChatInput(this);

        public void FallbackTypeText(string text)
        {
            FallbackTypeTextCount++;
            if (FallbackWritesText) Text = text;
        }

        public void SendEnter()
        {
            SendEnterCount++;
        }

        private sealed class FakeChatInput(FakeResumeHost host) : IChatInputController
        {
            public string? ReadText() => host.Text;
            public bool SupportsValuePattern => true;

            public void SetFocus()
            {
                if (host.ChangeForegroundAfterFocus is not null)
                {
                    host.ForegroundWindow = host.ChangeForegroundAfterFocus.Value;
                }
            }

            public void SetValue(string text)
            {
                host.SetTextCount++;
                if (!host.IgnoreValue) host.Text = text;
                if (host.ChangeForegroundAfterSetText is not null)
                {
                    host.ForegroundWindow = host.ChangeForegroundAfterSetText.Value;
                }
            }
        }
    }
}
