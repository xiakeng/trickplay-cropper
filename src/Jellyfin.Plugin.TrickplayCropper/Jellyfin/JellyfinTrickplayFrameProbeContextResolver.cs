using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves a Trickplay Frame Probe through Jellyfin's user-independent Item and source APIs.
/// </summary>
internal sealed class JellyfinTrickplayFrameProbeContextResolver : ITrickplayFrameProbeContextResolver
{
    private readonly ITrickplayFrameCalculationResolver calculationResolver;
    private readonly TrickplaySourceFactsCache sourceFactsCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinTrickplayFrameProbeContextResolver"/> class.
    /// </summary>
    /// <param name="sourceFactsCache">Resolves user-independent source facts with bounded reuse.</param>
    /// <param name="calculationResolver">Performs the shared resolution and Frame Index calculation.</param>
    public JellyfinTrickplayFrameProbeContextResolver(
        TrickplaySourceFactsCache sourceFactsCache,
        ITrickplayFrameCalculationResolver calculationResolver)
    {
        this.sourceFactsCache = sourceFactsCache;
        this.calculationResolver = calculationResolver;
    }

    /// <inheritdoc />
    public async Task<TrickplayFrameCalculationResolution> ResolveAsync(
        PreviewQuery query,
        CancellationToken cancellationToken)
    {
        TrickplaySourceFactsResolution sourceFacts = await sourceFactsCache
            .GetForProbeAsync(query, cancellationToken)
            .ConfigureAwait(false);
        return sourceFacts switch
        {
            TrickplaySourceFactsResolution.Available available => await calculationResolver
                .ResolveForProbeAsync(query, available.NormalizationSourceWidth, cancellationToken)
                .ConfigureAwait(false),
            TrickplaySourceFactsResolution.NotFound => Concealed(),
            _ => throw new InvalidOperationException(
                $"Unknown Trickplay source-facts resolution {sourceFacts.GetType().Name}."),
        };
    }

    private static TrickplayFrameCalculationResolution.NotFound Concealed()
    {
        return new TrickplayFrameCalculationResolution.NotFound(PreviewUnavailableReason.Concealed);
    }
}
