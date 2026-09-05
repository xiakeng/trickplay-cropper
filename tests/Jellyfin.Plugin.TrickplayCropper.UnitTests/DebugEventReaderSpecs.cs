using TrickplayCropper.IntegrationHarness;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class DebugEventReaderSpecs
{
    private static readonly DateTimeOffset since = new(2026, 9, 5, 1, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("1002", "TrickplayPreviewFrameSelected", "0", true)]
    [InlineData("1003", "TrickplayPreviewFrameSelected", "0", false)]
    [InlineData("1002", "Unknown", "0", false)]
    [InlineData("1002", "TrickplayPreviewFrameSelected", "-1", false)]
    [InlineData("1002", "TrickplayPreviewFrameSelected", "null", false)]
    public void RequiresAnExactStructuredEvent(string id, string name, string frame, bool expected)
    {
        string line = $$"""
            [2026-09-05 09:00:00.000 +08:00] [DBG] TrickplayDebug {"EventId":{{id}},"EventName":"{{name}}","FrameIndex":{{frame}},"SpriteIndex":0}
            """;
        Assert.Equal(expected, DebugEventReader.HasFrameSelection(line, since));
    }

    [Theory]
    [InlineData("[2026-09-04 09:00:00.000 +08:00] [DBG] TrickplayDebug {\"EventId\":1002,\"EventName\":\"TrickplayPreviewFrameSelected\",\"FrameIndex\":0,\"SpriteIndex\":0}")]
    [InlineData("[2026-09-05 09:00:00.000 +08:00] [DBG] Trickplay Preview selected FrameIndex 0 on SpriteIndex 0.")]
    [InlineData("[2026-09-05 09:00:00.000 +08:00] [DBG] TrickplayDebug {bad json}")]
    public void RejectsStaleFreeFormOrMalformedMessages(string line)
    {
        Assert.False(DebugEventReader.HasFrameSelection(line, since));
    }
}
