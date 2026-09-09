using GPTAutoResume.Automation;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class CodexThreadProviderTests
{
    [Fact]
    public void ParsesThreadListMetadataWithoutTurns()
    {
        var threads = CodexAppServerThreadProvider.ParseThreadListResult("""
            {
              "data": [
                {
                  "id": "thr_a",
                  "sessionId": "sess_a",
                  "name": "Example Developer API",
                  "preview": "private preview text",
                  "sourceKind": "appServer",
                  "createdAt": 1788847139,
                  "updatedAt": 1788849999,
                  "status": { "type": "notLoaded" },
                  "turns": [{ "should": "be ignored" }]
                }
              ],
              "nextCursor": null
            }
            """);

        var thread = Assert.Single(threads);
        Assert.Equal("thr_a", thread.Id);
        Assert.Equal("sess_a", thread.SessionId);
        Assert.Equal("Example Developer API", thread.Name);
        Assert.Equal("appServer", thread.SourceKind);
        Assert.Equal("notLoaded", thread.StatusType);
        Assert.Equal(1788847139, thread.CreatedAt!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void ParsesLoadedThreadIds()
    {
        var ids = CodexAppServerThreadProvider.ParseLoadedListResult("""
            { "data": ["thr_a", "thr_b", 123, null, ""] }
            """);

        Assert.Equal(["thr_a", "thr_b"], ids);
    }
}
