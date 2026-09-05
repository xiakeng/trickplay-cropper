using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Configuration;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves the current Jellyfin configuration and generated metadata for a Frame Index calculation.
/// </summary>
internal sealed class JellyfinTrickplayFrameCalculationResolver : ITrickplayFrameCalculationResolver
{
    private readonly TrickplayMetadataCache metadataCache;
    private readonly IServerConfigurationManager serverConfigurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinTrickplayFrameCalculationResolver"/> class.
    /// </summary>
    /// <param name="metadataCache">Resolves generated Trickplay metadata with request-appropriate freshness.</param>
    /// <param name="serverConfigurationManager">Reads the current Trickplay Resolution Targets.</param>
    public JellyfinTrickplayFrameCalculationResolver(
        TrickplayMetadataCache metadataCache,
        IServerConfigurationManager serverConfigurationManager)
    {
        this.metadataCache = metadataCache;
        this.serverConfigurationManager = serverConfigurationManager;
    }

    /// <inheritdoc />
    public Task<TrickplayFrameCalculationResolution> ResolveForPreviewAsync(
        PreviewQuery query,
        int? normalizationSourceWidth,
        CancellationToken cancellationToken)
    {
        var request = new CalculationRequest(query, normalizationSourceWidth, MetadataAccess.Preview);
        return ResolveAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TrickplayFrameCalculationResolution> ResolveForProbeAsync(
        PreviewQuery query,
        int? normalizationSourceWidth,
        CancellationToken cancellationToken)
    {
        var request = new CalculationRequest(query, normalizationSourceWidth, MetadataAccess.Probe);
        return ResolveAsync(request, cancellationToken);
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
            TrickplayMetadataResolution metadata = await ResolveMetadataAsync(
                request,
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

    private Task<TrickplayMetadataResolution> ResolveMetadataAsync(
        CalculationRequest request,
        int selectedResolution,
        CancellationToken cancellationToken)
    {
        return request.Access switch
        {
            MetadataAccess.Preview => metadataCache.GetForPreviewAsync(
                request.Query.ResolvedMediaSourceId,
                selectedResolution,
                cancellationToken),
            MetadataAccess.Probe => metadataCache.GetForProbeAsync(
                request.Query.ResolvedMediaSourceId,
                selectedResolution,
                cancellationToken),
            _ => throw new InvalidOperationException($"Unknown metadata access {request.Access}."),
        };
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
            TrickplayMetadataResolution.Available available => new TrickplayFrameCalculationResolution.Selected(
                available.Metadata,
                available.Metadata.SelectFrameIndex(query.PositionTicks)),
            TrickplayMetadataResolution.NotFound notFound => new TrickplayFrameCalculationResolution.NotFound(
                notFound.Reason),
            _ => throw new InvalidOperationException(
                $"Unknown Trickplay metadata resolution {metadataResolution.GetType().Name}."),
        };
    }

    private enum MetadataAccess
    {
        Preview,
        Probe,
    }

    private sealed record CalculationRequest(
        PreviewQuery Query,
        int? NormalizationSourceWidth,
        MetadataAccess Access);
}
