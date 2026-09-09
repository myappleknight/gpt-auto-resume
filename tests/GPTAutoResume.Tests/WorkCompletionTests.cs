using GPTAutoResume.Automation;
using Xunit;

namespace GPTAutoResume.Tests;

public class WorkCompletionTests
{
    [Fact]
    public void NativeContinueButtonInComposerProvesStoppedIncompleteReply()
    {
        var nodes = SimpleReply();
        nodes[7] = N(7, 5, UiAutomationWorkCompletionProvider.Token("ControlType.Button", "繼續", "", "按鈕"));
        var evidence = WorkCompletionAnalyzer.Analyze(nodes, true);
        Assert.Equal(LastTurnState.IncompleteStopped, evidence.LastTurnState);
        Assert.Equal(FooterPresence.Absent, evidence.Footer);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("running")]
    [InlineData("completed")]
    [InlineData("partial-footer")]
    [InlineData("partial-tree")]
    public void NativeContinueRequiresReliableComposerAndMissingCompletion(string scenario)
    {
        var nodes = SimpleReply();
        nodes[7] = N(7, scenario == "body" ? 0 : 5, CompletionToken.Continue) with
        { Visible = scenario != "hidden", Enabled = scenario != "disabled" };
        if (scenario == "running") nodes.Add(N(20, 5, CompletionToken.Running));
        if (scenario is "completed" or "partial-footer") nodes.Insert(5, N(21, 0, CompletionToken.Copy));
        if (scenario == "completed")
        {
            nodes.Insert(5, N(22, 0, CompletionToken.Rate));
            nodes.Insert(5, N(23, 0, CompletionToken.Branch));
        }
        Assert.False(WorkCompletionAnalyzer.Analyze(nodes, scenario != "partial-tree").IsIncomplete);
    }

    [Fact]
    public void ContinueTextInConversationCannotBecomeNativeAction()
    {
        Assert.Equal(CompletionToken.Content,
            UiAutomationWorkCompletionProvider.Token("ControlType.Text", "繼續", "", "文字"));
    }

    [Theory]
    [InlineData(false, FooterPresence.Present, LastTurnState.Running)]
    [InlineData(false, FooterPresence.Unknown, LastTurnState.Running)]
    [InlineData(true, FooterPresence.Present, LastTurnState.Completed)]
    [InlineData(true, FooterPresence.Absent, LastTurnState.IncompleteStopped)]
    [InlineData(true, FooterPresence.Unknown, LastTurnState.Unknown)]
    [InlineData(null, FooterPresence.Absent, LastTurnState.Unknown)]
    [InlineData(null, FooterPresence.Present, LastTurnState.Unknown)]
    public void FourStatesRequirePositiveStopAndCompletionEvidence(bool? stopped, FooterPresence footer, LastTurnState expected)
    {
        Assert.Equal(expected, new AssistantCompletionEvidence("reply", stopped, footer, "TEST").LastTurnState);
    }

    [Fact]
    public void MissingReplyIdentityCannotProduceKnownTurnState()
    {
        Assert.Equal(LastTurnState.Unknown, new AssistantCompletionEvidence("", true, FooterPresence.Absent, "TEST").LastTurnState);
    }

    [Theory]
    [InlineData("複製", "ControlType.Button", CompletionToken.Copy)]
    [InlineData("評價回覆", "ControlType.Button", CompletionToken.Rate)]
    [InlineData("從此處分支對話", "ControlType.Button", CompletionToken.Branch)]
    [InlineData("複製", "ControlType.Text", CompletionToken.Content)]
    [InlineData("評價回覆", "ControlType.Text", CompletionToken.Content)]
    [InlineData("從此處分支對話", "ControlType.Text", CompletionToken.Content)]
    [InlineData("Copy", "ControlType.Button", CompletionToken.Copy)]
    [InlineData("Rate response", "ControlType.Button", CompletionToken.Rate)]
    [InlineData("コピー", "ControlType.Button", CompletionToken.Copy)]
    [InlineData("停止", "ControlType.Button", CompletionToken.Running)]
    [InlineData("你已達使用上限。請稍後再試一次。", "ControlType.Text", CompletionToken.LimitInterruption)]
    [InlineData("你的 Codex 和工作使用量已用完", "ControlType.Text", CompletionToken.LimitInterruption)]
    [InlineData("You're out of Codex and Work usage", "ControlType.Text", CompletionToken.LimitInterruption)]
    public void WordingWithoutActualButtonRoleIsNotFooter(string name, string type, CompletionToken expected)
    {
        Assert.Equal(expected, UiAutomationWorkCompletionProvider.Token(type, name, "", ""));
    }

    [Fact]
    public void RunningOverridesIdleAndCompletedControls()
    {
        var nodes = SimpleReply();
        nodes.Add(N(20, 5, CompletionToken.Running));
        Assert.False(WorkCompletionAnalyzer.Analyze(nodes, true).Stopped);
    }

    [Fact]
    public void DisabledOrHiddenStopStillBlocksIdleInference()
    {
        var nodes = SimpleReply();
        nodes.Add(N(20, 5, CompletionToken.Running) with { Enabled = false, Visible = false });
        Assert.False(WorkCompletionAnalyzer.Analyze(nodes, true).Stopped);
    }

    [Fact]
    public void VoiceButtonAloneDoesNotProveStopped()
    {
        Assert.Equal(CompletionToken.Other, UiAutomationWorkCompletionProvider.Token(
            "ControlType.Button", "開始語音對話", "", "按鈕"));
    }

    [Fact]
    public void TruncatedTreeMissingStatusOrNewerUserIsUnknown()
    {
        var nodes = SimpleReply();
        Assert.Null(WorkCompletionAnalyzer.Analyze(nodes, false).Stopped);
        var missingStatus = nodes.Where(n => n.Token != CompletionToken.Status).ToArray();
        Assert.Equal(FooterPresence.Unknown, WorkCompletionAnalyzer.Analyze(missingStatus, true).Footer);
        nodes.Insert(5, N(20, 0, CompletionToken.UserHeading));
        Assert.Equal("NO_LAST_ASSISTANT_AFTER_USER", WorkCompletionAnalyzer.Analyze(nodes, true).Reason);
    }

    [Fact]
    public void OffscreenLastReplyAndUnknownIdleStateCannotQualify()
    {
        var nodes = SimpleReply();
        nodes[4] = nodes[4] with { Visible = false };
        Assert.Equal("LAST_REPLY_NOT_VISIBLE", WorkCompletionAnalyzer.Analyze(nodes, true).Reason);
        nodes = SimpleReply();
        nodes.RemoveAt(7);
        Assert.Null(WorkCompletionAnalyzer.Analyze(nodes, true).Stopped);
    }

    [Fact]
    public void HiddenExistingFooterStillPreventsResume()
    {
        var nodes = SimpleReply();
        nodes.Insert(6, N(20, 0, CompletionToken.Copy) with { Visible = false });
        nodes.Insert(7, N(21, 0, CompletionToken.Rate) with { Visible = false });
        nodes.Insert(8, N(22, 0, CompletionToken.Branch) with { Visible = false });
        Assert.Equal(FooterPresence.Present, WorkCompletionAnalyzer.Analyze(nodes, true).Footer);
    }

    [Fact]
    public void NoKnownFooterStructureIsUnknownNotIncomplete()
    {
        Assert.Equal(FooterPresence.Unknown, WorkCompletionAnalyzer.Analyze(SimpleReply(), true).Footer);
    }

    [Fact]
    public void LatestLimitInterruptionWordingWithUnknownFooterDoesNotQualifyAsIncomplete()
    {
        var nodes = SimpleReply();
        nodes.Insert(5, N(20, 0, CompletionToken.LimitInterruption));
        var result = WorkCompletionAnalyzer.Analyze(nodes, true);
        Assert.True(result.Stopped);
        Assert.Equal(FooterPresence.Unknown, result.Footer);
        Assert.False(result.IsIncomplete);
        Assert.Equal(LastTurnState.Unknown, result.LastTurnState);
        Assert.Equal("FOOTER_ABSENCE_UNVERIFIED", result.Reason);
    }

    [Fact]
    public void LatestLimitInterruptionWordingDoesNotProveStoppedWhenSendControlIsNotExposed()
    {
        var nodes = SimpleReply();
        nodes.RemoveAll(n => n.Token == CompletionToken.Idle);
        nodes.RemoveAll(n => n.Token == CompletionToken.Content);
        nodes.Insert(5, N(20, 0, CompletionToken.LimitInterruption));
        var result = WorkCompletionAnalyzer.Analyze(nodes, true);
        Assert.Null(result.Stopped);
        Assert.Equal(FooterPresence.Unknown, result.Footer);
        Assert.False(result.IsIncomplete);
    }

    [Fact]
    public void LatestLimitInterruptionDoesNotOverrideRunningOrCompletedState()
    {
        var running = SimpleReply();
        running.Insert(5, N(20, 0, CompletionToken.LimitInterruption));
        running.Add(N(21, 5, CompletionToken.Running));
        Assert.Equal(LastTurnState.Running, WorkCompletionAnalyzer.Analyze(running, true).LastTurnState);

        var completed = SimpleReply();
        completed.Insert(5, N(20, 0, CompletionToken.LimitInterruption));
        completed.Insert(6, N(21, 0, CompletionToken.Copy));
        completed.Insert(7, N(22, 0, CompletionToken.Rate));
        completed.Insert(8, N(23, 0, CompletionToken.Branch));
        var result = WorkCompletionAnalyzer.Analyze(completed, true);
        Assert.Equal(FooterPresence.Present, result.Footer);
        Assert.Equal(LastTurnState.Completed, result.LastTurnState);
    }

    private static List<CompletionNode> SimpleReply() =>
    [N(0, -1), N(1, 0, CompletionToken.UserHeading), N(2, 0, CompletionToken.AssistantHeading),
        N(3, 0, CompletionToken.Status), N(4, 0, CompletionToken.Content), N(5, 0),
        N(6, 5, CompletionToken.Composer), N(7, 5, CompletionToken.Idle)];

    [Fact]
    public void FooterOnlyBelongsToLastAssistantReply()
    {
        var nodes = new List<CompletionNode>
        {
            N(0, -1), N(1, 0, CompletionToken.UserHeading),
            N(2, 0, CompletionToken.AssistantHeading), N(3, 0, CompletionToken.Status),
            N(4, 0, CompletionToken.Copy), N(5, 0, CompletionToken.Rate), N(6, 0, CompletionToken.Branch),
            N(7, 0, CompletionToken.UserHeading), N(8, 0, CompletionToken.AssistantHeading),
            N(9, 0, CompletionToken.Status), N(10, 0, CompletionToken.Content),
            N(11, 0), N(12, 11, CompletionToken.Composer), N(13, 11, CompletionToken.Idle)
        };
        var result = WorkCompletionAnalyzer.Analyze(nodes, true);
        Assert.Equal(FooterPresence.Unknown, result.Footer);
        Assert.True(result.Stopped);
        Assert.NotEmpty(result.AssistantReplyIdentity);
        nodes.Insert(11, N(14, 0, CompletionToken.Copy));
        nodes.Insert(12, N(15, 0, CompletionToken.Rate));
        nodes.Insert(13, N(16, 0, CompletionToken.Branch));
        Assert.Equal(FooterPresence.Present, WorkCompletionAnalyzer.Analyze(nodes, true).Footer);
    }

    [Fact]
    public void LimitInterruptionInOldReplyDoesNotQualifyLatestReply()
    {
        var nodes = new List<CompletionNode>
        {
            N(0, -1), N(1, 0, CompletionToken.UserHeading),
            N(2, 0, CompletionToken.AssistantHeading), N(3, 0, CompletionToken.Status),
            N(4, 0, CompletionToken.LimitInterruption), N(5, 0, CompletionToken.UserHeading),
            N(6, 0, CompletionToken.AssistantHeading), N(7, 0, CompletionToken.Status),
            N(8, 0, CompletionToken.Content), N(9, 0),
            N(10, 9, CompletionToken.Composer), N(11, 9, CompletionToken.Idle)
        };

        var result = WorkCompletionAnalyzer.Analyze(nodes, true);
        Assert.Equal(FooterPresence.Unknown, result.Footer);
        Assert.False(result.IsIncomplete);
        Assert.Equal(LastTurnState.Unknown, result.LastTurnState);
    }

    private static CompletionNode N(int id, int parent, CompletionToken token = CompletionToken.Other) =>
        new(id, parent, token, true, true, "digest-" + id);
}
