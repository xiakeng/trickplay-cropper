using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Answers the Trickplay Frame Probe from user-independent source facts and shared calculation rules.
/// </summary>
internal sealed class TrickplayFrameProbe : ITrickplayFrameProbe
{
    private readonly ITrickplayFrameProbeContextResolver contextResolver;
    private readonly ILogger<TrickplayFrameProbe> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayFrameProbe"/> class.
    /// </summary>
    /// <param name="contextResolver">The user-independent probe context resolver.</param>
    /// <param name="logger">Records the stable Debug Preview decision protocol.</param>
    public TrickplayFrameProbe(
        ITrickplayFrameProbeContextResolver contextResolver,
        ILogger<TrickplayFrameProbe> logger)
    {
        this.contextResolver = contextResolver;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrickplayFrameProbeOutcome> ProbeAsync(
        PreviewQuery query,
        CancellationToken cancellationToken)
    {
        if (query.PositionTicks < 0)
        {
            return new TrickplayFrameProbeOutcome.BadRequest();
        }

        try
        {
            TrickplayFrameCalculationResolution calculation = await contextResolver
                .ResolveAsync(query, cancellationToken)
                .ConfigureAwait(false);
            return calculation switch
            {
                TrickplayFrameCalculationResolution.Selected selected =>
                    new TrickplayFrameProbeOutcome.Success(selected.FrameIndex),
                TrickplayFrameCalculationResolution.NotFound notFound => MapUnavailable(notFound.Reason),
                _ => throw new InvalidOperationException(
                    $"Unknown Trickplay Frame calculation {calculation.GetType().Name}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new TrickplayFrameProbeOutcome.InternalError();
        }
    }

    private TrickplayFrameProbeOutcome.NotFound MapUnavailable(PreviewUnavailableReason reason)
    {
        PreviewDebugProtocol.LogUnavailable(logger, reason);
        return new TrickplayFrameProbeOutcome.NotFound();
    }
}
