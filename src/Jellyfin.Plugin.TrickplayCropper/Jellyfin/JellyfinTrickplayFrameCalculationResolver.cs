using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Trickplay;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves the current Jellyfin configuration and generated metadata for a Frame Index calculation.
/// </summary>
internal sealed class JellyfinTrickplayFrameCalculationResolver : ITrickplayFrameCalculationResolver
{
    private readonly ITrickplayManager trickplayManager;
    private readonly IServerConfigurationManager serverConfigurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinTrickplayFrameCalculationResolver"/> class.
    /// </summary>
    /// <param name="trickplayManager">Reads generated Trickplay metadata.</param>
    /// <param name="serverConfigurationManager">Reads the current Trickplay Resolution Targets.</param>
    public JellyfinTrickplayFrameCalculationResolver(
        ITrickplayManager trickplayManager,
        IServerConfigurationManager serverConfigurationManager)
    {
        this.trickplayManager = trickplayManager;
        this.serverConfigurationManager = serverConfigurationManager;
    }

    /// <inheritdoc />
    public async Task<TrickplayFrameCalculationResolution> ResolveAsync(
        PreviewQuery query,
        int? normalizationSourceWidth)
    {
        int[]? configuredTargets = serverConfigurationManager.Configuration?
            .TrickplayOptions?
            .WidthResolutions?
            .ToArray();
        int? selectedResolution = SelectResolution(configuredTargets, normalizationSourceWidth);
        if (selectedResolution is null)
        {
            return new TrickplayFrameCalculationResolution.NotFound(
                PreviewUnavailableReason.NoConfiguredTarget);
        }

        Dictionary<int, TrickplayInfo> resolutions = await trickplayManager
            .GetTrickplayResolutions(query.ResolvedMediaSourceId)
            .ConfigureAwait(false);
        var observation = new CalculationObservation(
            configuredTargets!,
            normalizationSourceWidth,
            selectedResolution.Value,
            resolutions);
        return SelectFrame(query, observation);
    }

    private static int? SelectResolution(int[]? configuredTargets, int? normalizationSourceWidth)
    {
        try
        {
            return TrickplayResolutionSelector.Select(configuredTargets, normalizationSourceWidth);
        }
        catch (InvalidTrickplayConfigurationException failure)
        {
            failure.Configuration = new PreviewConfigurationDiagnostics
            {
                ConfiguredTargets = configuredTargets,
                NormalizationSourceWidth = normalizationSourceWidth,
            };
            throw;
        }
    }

    private static TrickplayFrameCalculationResolution SelectFrame(
        PreviewQuery query,
        CalculationObservation observation)
    {
        if (observation.Resolutions.Count == 0)
        {
            return new TrickplayFrameCalculationResolution.NotFound(
                PreviewUnavailableReason.NoGeneratedMetadata);
        }

        if (!observation.Resolutions.TryGetValue(observation.SelectedResolution, out TrickplayInfo? info))
        {
            return new TrickplayFrameCalculationResolution.NotFound(
                PreviewUnavailableReason.SelectedResolutionMissing);
        }

        if (info.ThumbnailCount <= 0)
        {
            return new TrickplayFrameCalculationResolution.NotFound(
                PreviewUnavailableReason.NoThumbnails);
        }

        var metadata = new TrickplayMetadata(
            info.Width,
            info.Height,
            info.Interval,
            info.TileWidth,
            info.TileHeight,
            info.ThumbnailCount);
        var configuration = new PreviewConfigurationDiagnostics
        {
            ConfiguredTargets = observation.ConfiguredTargets,
            ChosenTarget = observation.ConfiguredTargets.Min(),
            SelectedResolution = observation.SelectedResolution,
            NormalizationSourceWidth = observation.NormalizationSourceWidth,
        };
        ValidateMetadata(metadata, observation, configuration);
        return new TrickplayFrameCalculationResolution.Selected(
            metadata,
            metadata.SelectFrameIndex(query.PositionTicks));
    }

    private static void ValidateMetadata(
        TrickplayMetadata metadata,
        CalculationObservation observation,
        PreviewConfigurationDiagnostics configuration)
    {
        try
        {
            metadata.Validate();
            if (metadata.FrameWidth != observation.SelectedResolution)
            {
                throw new InvalidTrickplayMetadataException(
                    metadata,
                    "FrameWidthMatchesResolutionKey",
                    metadata.FrameWidth);
            }
        }
        catch (InvalidTrickplayMetadataException failure)
        {
            failure.Configuration = configuration with
            {
                GeneratedKeys = observation.Resolutions.Keys.Order().ToArray(),
            };
            throw;
        }
    }

    private sealed record CalculationObservation(
        int[] ConfiguredTargets,
        int? NormalizationSourceWidth,
        int SelectedResolution,
        IReadOnlyDictionary<int, TrickplayInfo> Resolutions);
}
