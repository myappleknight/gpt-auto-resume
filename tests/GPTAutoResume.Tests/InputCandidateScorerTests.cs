using GPTAutoResume.Automation;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class InputCandidateScorerTests
{
    [Fact]
    public void ProseMirrorIsStrongSignalButNotRequired()
    {
        var candidate = Candidate(
            className: "SomeNewEditor",
            automationId: "chat-message-input",
            name: "Message ChatGPT",
            hasValuePattern: true);

        var score = InputCandidateScorer.Score(candidate);

        Assert.True(score.IsSafe);
        Assert.True(score.Confidence >= 72);
    }

    [Fact]
    public void ProseMirrorClassAloneIsNotEnough()
    {
        var candidate = Candidate(className: "ProseMirror", isKeyboardFocusable: false, hasValuePattern: false, hasTextPattern: false);

        var score = InputCandidateScorer.Score(candidate);

        Assert.False(score.IsSafe);
    }

    [Fact]
    public void GenericEditWithoutIdentitySignalIsNotSafe()
    {
        var candidate = Candidate(className: "", automationId: "", name: "", hasValuePattern: true);

        var score = InputCandidateScorer.Score(candidate);

        Assert.False(score.IsSafe);
    }

    [Fact]
    public void NotepadLikeEditOutsideTargetWindowIsNotSafe()
    {
        var candidate = Candidate(
            className: "Edit",
            automationId: "15",
            name: "Text Editor",
            hasValuePattern: true,
            isInsideTargetWindow: false);

        var score = InputCandidateScorer.Score(candidate);

        Assert.False(score.IsSafe);
    }

    private static InputCandidateInfo Candidate(
        string controlType = "ControlType.Edit",
        string className = "ProseMirror",
        string automationId = "",
        string name = "",
        bool isEnabled = true,
        bool isKeyboardFocusable = true,
        bool hasValuePattern = false,
        bool hasTextPattern = true,
        bool isInsideTargetWindow = true,
        int depthFromRoot = 8) =>
        new(controlType, className, automationId, name, isEnabled, isKeyboardFocusable, hasValuePattern, hasTextPattern, isInsideTargetWindow, depthFromRoot);
}
