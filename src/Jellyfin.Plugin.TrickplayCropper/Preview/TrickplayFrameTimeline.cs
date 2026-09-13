using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Resolves one authorized Frame Timeline without representation work.
/// </summary>
internal sealed class TrickplayFrameTimeline : IFrameTimeline
{
    private readonly JellyfinPreviewContextResolver contextResolver;
    private readonly ITrickplayFrameCalculationResolver calculationResolver;

    /// <summary>Initializes a new instance of the <see cref="TrickplayFrameTimeline"/> class.</summary>
    public TrickplayFrameTimeline(
        JellyfinPreviewContextResolver contextResolver,
        ITrickplayFrameCalculationResolver calculationResolver)
    {
        this.contextResolver = contextResolver;
        this.calculationResolver = calculationResolver;
    }

    /// <inheritdoc />
    public async Task<FrameTimelineOutcome> GetAsync(
        PreviewSourceQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        try
        {
            AuthorizedSourceResolution authorization = await contextResolver
                .ResolveAuthorizedSourceAsync(query, principal, cancellationToken)
                .ConfigureAwait(false);
            if (authorization is not AuthorizedSourceResolution.Resolved resolved)
            {
                return MapAuthorization(authorization);
            }

            TrickplayTimelineCalculationResolution calculation = await calculationResolver
                .ResolveForTimelineAsync(
                    resolved.SourceVideo.Id,
                    resolved.NormalizationSourceWidth,
                    cancellationToken)
                .ConfigureAwait(false);
            if (calculation is TrickplayTimelineCalculationResolution.NotFound)
            {
                return new FrameTimelineOutcome.NotFound();
            }

            var selected = (TrickplayTimelineCalculationResolution.Selected)calculation;
            long intervalTicks = checked(
                (long)selected.Metadata.IntervalMilliseconds * TimeSpan.TicksPerMillisecond);
            if (intervalTicks <= 0 || selected.Metadata.ThumbnailCount <= 0)
            {
                return new FrameTimelineOutcome.InternalError();
            }

            return new FrameTimelineOutcome.Success(intervalTicks, selected.Metadata.ThumbnailCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new FrameTimelineOutcome.InternalError();
        }
    }

    private static FrameTimelineOutcome MapAuthorization(AuthorizedSourceResolution resolution)
    {
        return resolution switch
        {
            AuthorizedSourceResolution.BadRequest => new FrameTimelineOutcome.BadRequest(),
            AuthorizedSourceResolution.Unauthorized => new FrameTimelineOutcome.Unauthorized(),
            AuthorizedSourceResolution.Forbidden => new FrameTimelineOutcome.Forbidden(),
            AuthorizedSourceResolution.NotFound => new FrameTimelineOutcome.NotFound(),
            _ => throw new InvalidOperationException(
                $"Unknown authorized source resolution {resolution.GetType().Name}."),
        };
    }
}
