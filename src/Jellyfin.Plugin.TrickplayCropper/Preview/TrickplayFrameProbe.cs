using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Answers the Trickplay Frame Probe from the shared Preview context alone.
/// </summary>
internal sealed class TrickplayFrameProbe : ITrickplayFrameProbe
{
    private readonly IPreviewContextResolver contextResolver;
    private readonly ILogger<TrickplayFrameProbe> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayFrameProbe"/> class.
    /// </summary>
    /// <param name="contextResolver">The shared Preview context resolver.</param>
    /// <param name="logger">Records the stable Debug Preview decision protocol.</param>
    public TrickplayFrameProbe(
        IPreviewContextResolver contextResolver,
        ILogger<TrickplayFrameProbe> logger)
    {
        this.contextResolver = contextResolver;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrickplayFrameProbeOutcome> ProbeAsync(
        PreviewQuery query,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        try
        {
            PreviewContextResolution contextResolution = await contextResolver
                .ResolveAsync(query, user, cancellationToken)
                .ConfigureAwait(false);
            return contextResolution switch
            {
                PreviewContextResolution.Resolved resolved =>
                    new TrickplayFrameProbeOutcome.Success(resolved.Context.FrameIndex),
                PreviewContextResolution.BadRequest => new TrickplayFrameProbeOutcome.BadRequest(),
                PreviewContextResolution.Unauthorized => new TrickplayFrameProbeOutcome.Unauthorized(),
                PreviewContextResolution.Forbidden => new TrickplayFrameProbeOutcome.Forbidden(),
                PreviewContextResolution.NotFound notFound => MapUnavailable(notFound.Reason),
                _ => throw new InvalidOperationException(
                    $"Unknown preview context resolution {contextResolution.GetType().Name}."),
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
        if (reason != PreviewUnavailableReason.Concealed)
        {
            PreviewDebugProtocol.LogUnavailable(logger, reason);
        }

        return new TrickplayFrameProbeOutcome.NotFound();
    }
}
