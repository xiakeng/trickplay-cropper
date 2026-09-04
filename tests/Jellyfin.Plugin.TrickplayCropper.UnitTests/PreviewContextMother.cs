using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

/// <summary>
/// Provides the canonical test doubles for the shared Preview context contract.
/// </summary>
internal static class PreviewContextMother
{
    public static PreviewContextResolution CreateResolution(ContextFailureKind failureKind)
    {
        return failureKind switch
        {
            ContextFailureKind.BadRequest => new PreviewContextResolution.BadRequest(),
            ContextFailureKind.Unauthorized => new PreviewContextResolution.Unauthorized(),
            ContextFailureKind.Forbidden => new PreviewContextResolution.Forbidden(),
            ContextFailureKind.NotFound => new PreviewContextResolution.NotFound(PreviewUnavailableReason.Concealed),
            _ => throw new ArgumentOutOfRangeException(
                nameof(failureKind),
                failureKind,
                "Unknown shared context failure kind."),
        };
    }

    internal sealed class StubResolver : IPreviewContextResolver
    {
        private readonly PreviewContextResolution resolution;
        private int callCount;

        public StubResolver(PreviewContextResolution resolution)
        {
            this.resolution = resolution;
        }

        public int CallCount => Volatile.Read(ref callCount);

        public CancellationToken CancellationToken { get; private set; }

        public ClaimsPrincipal? Principal { get; private set; }

        public PreviewQuery? Query { get; private set; }

        public Task<PreviewContextResolution> ResolveAsync(
            PreviewQuery query,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            Query = query;
            Principal = principal;
            CancellationToken = cancellationToken;
            return Task.FromResult(resolution);
        }
    }

    internal sealed class CancellingResolver : IPreviewContextResolver
    {
        public Task<PreviewContextResolution> ResolveAsync(
            PreviewQuery query,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
        {
            return Task.FromCanceled<PreviewContextResolution>(cancellationToken);
        }
    }
}
