using System.IO;
using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class LocalAppPathsTests
{
    [Fact]
    public void PossibleLimitDiagnosticsDirectoryUsesLocalAppData()
    {
        var path = LocalAppPaths.PossibleLimitDiagnosticsDirectory;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.True(Path.IsPathFullyQualified(path));
        Assert.Contains("GPTAutoResume", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("GPTAutoResume", "possible-limits"), path, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(localAppData, path, StringComparison.OrdinalIgnoreCase);
    }
}
