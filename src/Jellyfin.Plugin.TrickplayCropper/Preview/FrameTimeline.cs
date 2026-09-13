using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Resolves the authorized calculation inputs for one playback Frame Timeline.
/// </summary>
internal interface IFrameTimeline
{
    Task<FrameTimelineOutcome> GetAsync(
        FrameTimelineQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}

/// <summary>
/// Serves calculation data without entering the Preview representation path.
/// </summary>
internal sealed class FrameTimeline : IFrameTimeline
{
    private readonly JellyfinPreviewContextResolver contextResolver;

    public FrameTimeline(JellyfinPreviewContextResolver contextResolver)
    {
        this.contextResolver = contextResolver;
    }

    public async Task<FrameTimelineOutcome> GetAsync(
        FrameTimelineQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        try
        {
            FrameTimelineContextResolution resolution = await contextResolver
                .ResolveTimelineAsync(query, principal, cancellationToken)
                .ConfigureAwait(false);
            return resolution switch
            {
                FrameTimelineContextResolution.Resolved resolved =>
                    CreateSuccess(resolved.Metadata),
                FrameTimelineContextResolution.BadRequest => new FrameTimelineOutcome.BadRequest(),
                FrameTimelineContextResolution.Unauthorized => new FrameTimelineOutcome.Unauthorized(),
                FrameTimelineContextResolution.Forbidden => new FrameTimelineOutcome.Forbidden(),
                FrameTimelineContextResolution.NotFound => new FrameTimelineOutcome.NotFound(),
                _ => throw new InvalidOperationException(
                    $"Unknown Frame Timeline resolution {resolution.GetType().Name}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new FrameTimelineOutcome.InternalError();
        }
    }

    private static FrameTimelineOutcome.Success CreateSuccess(TrickplayMetadata metadata)
    {
        long intervalTicks = checked((long)metadata.IntervalMilliseconds * TimeSpan.TicksPerMillisecond);
        if (intervalTicks <= 0 || metadata.ThumbnailCount <= 0)
        {
            throw new InvalidOperationException("Generated timeline data must be positive.");
        }

        return new FrameTimelineOutcome.Success(intervalTicks, metadata.ThumbnailCount);
    }
}

internal sealed record FrameTimelineQuery(Guid ItemId, Guid? MediaSourceId)
{
    public Guid ResolvedMediaSourceId => MediaSourceId ?? ItemId;
}

internal abstract record FrameTimelineOutcome
{
    internal sealed record Success(long IntervalTicks, int FrameCount) : FrameTimelineOutcome;

    internal sealed record BadRequest : FrameTimelineOutcome;

    internal sealed record Unauthorized : FrameTimelineOutcome;

    internal sealed record Forbidden : FrameTimelineOutcome;

    internal sealed record NotFound : FrameTimelineOutcome;

    internal sealed record InternalError : FrameTimelineOutcome;
}
