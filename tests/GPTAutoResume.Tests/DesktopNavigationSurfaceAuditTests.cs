using GPTAutoResume.Automation;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class DesktopNavigationSurfaceAuditTests
{
    [Fact]
    public void ProtocolRelevanceIncludesChatGptOpenAiAndCodex()
    {
        Assert.True(ProtocolRegistrationAudit.LooksRelevant("chatgpt", "", ""));
        Assert.True(ProtocolRegistrationAudit.LooksRelevant("something", "OpenAI Desktop", ""));
        Assert.True(ProtocolRegistrationAudit.LooksRelevant("something", "", @"C:\Tools\codex.exe ""%1"""));
    }

    [Fact]
    public void UnrelatedProtocolIsIgnored()
    {
        Assert.False(ProtocolRegistrationAudit.LooksRelevant("mailto", "URL:MailTo Protocol", "outlook.exe"));
    }
}
