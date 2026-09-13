using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class AppConfigDefaultsTests
{
    [Fact]
    public void NewInstallDefaultsEnableRealActiveWorkAutoResume()
    {
        var config = new AppConfig();

        Assert.True(config.AutoResume);
        Assert.Equal(ResumePolicy.AutomaticForegroundResume, config.ResumePolicy);
        Assert.False(config.DryRun);
        Assert.True(config.SendEnter);
        Assert.True(config.AllowRealSubmit);
        Assert.True(config.RequireWorkSelection);
    }
}
