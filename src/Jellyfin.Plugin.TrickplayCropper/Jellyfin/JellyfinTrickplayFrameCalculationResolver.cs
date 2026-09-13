using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Configuration;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves the current Jellyfin configuration and generated metadata for a Frame Index calculation.
/// </summary>
internal sealed class JellyfinTrickplayFrameCalculationResolver : ITrickplayFrameCalculationResolver
{
    private readonly TrickplayMetadataReader metadataReader;
    private readonly IServerConfigurationManager serverConfigurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinTrickplayFrameCalculationResolver"/> class.
    /// </summary>
    /// <param name="metadataReader">Reads generated Trickplay metadata authoritatively.</param>
    /// <param name="serverConfigurationManager">Reads the current Trickplay Resolution Targets.</param>
    public JellyfinTrickplayFrameCalculationResolver(
        TrickplayMetadataReader metadataReader,
        IServerConfigurationManager serverConfigurationManager)
    {
        this.metadataReader = metadataReader;
        this.serverConfigurationManager = serverConfigurationManager;
    }

    /// <inheritdoc />
    public Task<TrickplayFrameCalculationResolution> ResolveForPreviewAsync(
        PreviewQuery query,
        int? normalizationSourceWidth,
        CancellationToken cancellationToken)
    {
        var request = new CalculationRequest(query, normalizationSourceWidth);
        return ResolveAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TrickplayTimelineCalculationResolution> ResolveForTimelineAsync(
        Guid sourceVideoId,
        int? normalizationSourceWidth,
        CancellationToken cancellationToken)
    {
        int[]? configuredTargets = serverConfigurationManager.Configuration?
            .TrickplayOptions?
            .WidthResolutions?
            .ToArray();
        int? selectedResolution = SelectResolution(configuredTargets, normalizationSourceWidth);
        if (selectedResolution is null)
        {
            return new TrickplayTimelineCalculationResolution.NotFound(
                PreviewUnavailableReason.NoConfiguredTarget);
        }

        PreviewConfigurationDiagnostics configuration = CreateConfiguration(
            configuredTargets!,
            normalizationSourceWidth,
            selectedResolution.Value);
        try
        {
            TrickplayMetadataResolution metadata = await metadataReader
                .ReadAuthoritativeTimelineAsync(sourceVideoId, selectedResolution.Value, cancellationToken)
                .ConfigureAwait(false);
            return metadata switch
            {
                TrickplayMetadataResolution.Available available =>
                    new TrickplayTimelineCalculationResolution.Selected(available.Metadata),
                TrickplayMetadataResolution.NotFound notFound =>
                    new TrickplayTimelineCalculationResolution.NotFound(notFound.Reason),
                _ => throw new InvalidOperationException(
                    $"Unknown Trickplay metadata resolution {metadata.GetType().Name}."),
            };
        }
        catch (InvalidTrickplayMetadataException failure)
        {
            failure.Configuration = configuration with
            {
                GeneratedKeys = failure.Configuration?.GeneratedKeys,
            };
            throw;
        }
    }

    private async Task<TrickplayFrameCalculationResolution> ResolveAsync(
        CalculationRequest request,
        CancellationToken cancellationToken)
    {
        int[]? configuredTargets = serverConfigurationManager.Configuration?
            .TrickplayOptions?
            .WidthResolutions?
            .ToArray();
        int? selectedResolution = SelectResolution(configuredTargets, request.NormalizationSourceWidth);
        if (selectedResolution is null)
        {
            return new TrickplayFrameCalculationResolution.NotFound(
                PreviewUnavailableReason.NoConfiguredTarget);
        }

        PreviewConfigurationDiagnostics configuration = CreateConfiguration(
            configuredTargets!,
            request.NormalizationSourceWidth,
            selectedResolution.Value);
        try
        {
            TrickplayMetadataResolution metadata = await metadataReader.GetForPreviewAsync(
                request.Query.ResolvedMediaSourceId,
                selectedResolution.Value,
                cancellationToken).ConfigureAwait(false);
            return SelectFrame(request.Query, metadata);
        }
        catch (InvalidTrickplayMetadataException failure)
        {
            failure.Configuration = configuration with
            {
                GeneratedKeys = failure.Configuration?.GeneratedKeys,
            };
            throw;
        }
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

    private static PreviewConfigurationDiagnostics CreateConfiguration(
        int[] configuredTargets,
        int? normalizationSourceWidth,
        int selectedResolution)
    {
        return new PreviewConfigurationDiagnostics
        {
            ConfiguredTargets = configuredTargets,
            ChosenTarget = configuredTargets.Min(),
            SelectedResolution = selectedResolution,
            NormalizationSourceWidth = normalizationSourceWidth,
        };
    }

    private static TrickplayFrameCalculationResolution SelectFrame(
        PreviewQuery query,
        TrickplayMetadataResolution metadataResolution)
    {
        return metadataResolution switch
        {
            TrickplayMetadataResolution.Available available when query.FrameIndex >= 0
                && query.FrameIndex < available.Metadata.ThumbnailCount =>
                new TrickplayFrameCalculationResolution.Selected(available.Metadata, query.FrameIndex),
            TrickplayMetadataResolution.Available => new TrickplayFrameCalculationResolution.BadRequest(),
            TrickplayMetadataResolution.NotFound notFound => new TrickplayFrameCalculationResolution.NotFound(
                notFound.Reason),
            _ => throw new InvalidOperationException(
                $"Unknown Trickplay metadata resolution {metadataResolution.GetType().Name}."),
        };
    }

    private sealed record CalculationRequest(
        PreviewQuery Query,
        int? NormalizationSourceWidth);
}
