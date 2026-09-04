using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Answers the Trickplay Frame Probe from the shared Preview context alone.
/// </summary>
internal sealed class TrickplayFrameProbe : ITrickplayFrameProbe
{
    private readonly IPreviewContextResolver contextResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayFrameProbe"/> class.
    /// </summary>
    /// <param name="contextResolver">The shared Preview context resolver.</param>
    public TrickplayFrameProbe(IPreviewContextResolver contextResolver)
    {
        this.contextResolver = contextResolver;
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
                PreviewContextResolution.NotFound => new TrickplayFrameProbeOutcome.NotFound(),
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
}
