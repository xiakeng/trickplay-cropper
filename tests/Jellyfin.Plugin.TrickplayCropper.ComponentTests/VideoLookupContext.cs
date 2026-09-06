using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed class VideoLookupContext
{
    public required Video AlternateVideo { get; init; }

    public required Video LogicalVideo { get; init; }

    public required PreviewScenario Scenario { get; init; }

    public required User User { get; init; }
}
