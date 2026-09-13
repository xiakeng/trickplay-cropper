using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Owns one authorized Frame Timeline request.
/// </summary>
internal sealed class TrickplayFrameTimeline : ITrickplayFrameTimeline
{
    private readonly IFrameTimelineContextResolver contextResolver;

    public TrickplayFrameTimeline(IFrameTimelineContextResolver contextResolver)
    {
        this.contextResolver = contextResolver;
    }

    /// <inheritdoc />
    public async Task<TrickplayFrameTimelineOutcome> GetAsync(
        Guid itemId,
        Guid? mediaSourceId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        try
        {
            FrameTimelineContextResolution resolution = await contextResolver
                .ResolveAsync(itemId, mediaSourceId, user, cancellationToken)
                .ConfigureAwait(false);
            return resolution switch
            {
                FrameTimelineContextResolution.Resolved resolved =>
                    new TrickplayFrameTimelineOutcome.Ok(
                        resolved.IntervalTicks,
                        resolved.FrameCount),
                FrameTimelineContextResolution.Unauthorized =>
                    new TrickplayFrameTimelineOutcome.Unauthorized(),
                FrameTimelineContextResolution.Forbidden =>
                    new TrickplayFrameTimelineOutcome.Forbidden(),
                FrameTimelineContextResolution.NotFound =>
                    new TrickplayFrameTimelineOutcome.NotFound(),
                _ => throw new InvalidOperationException(
                    $"Unknown Frame Timeline context {resolution.GetType().Name}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new TrickplayFrameTimelineOutcome.InternalError();
        }
    }
}
