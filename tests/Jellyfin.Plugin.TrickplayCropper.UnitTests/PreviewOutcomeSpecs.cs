using System.Reflection;
using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class PreviewOutcomeSpecs
{
    private static readonly Guid itemId = Guid.Parse("5dd8f9cc-b38d-45fb-9a97-0358b5ef25e2");

    [Fact]
    public void OutcomeSetRemainsClosedAndTyped()
    {
        string[] outcomeNames = typeof(PreviewOutcome)
            .GetNestedTypes(BindingFlags.Public)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(PreviewOutcome.BadRequest),
                nameof(PreviewOutcome.Forbidden),
                nameof(PreviewOutcome.InternalError),
                nameof(PreviewOutcome.NotFound),
                nameof(PreviewOutcome.NotModified),
                nameof(PreviewOutcome.Ok),
                nameof(PreviewOutcome.Unauthorized),
            ],
            outcomeNames);
    }

    [Theory]
    [MemberData(nameof(ExpectedResolutionOutcomes))]
    public async Task MapsSourceResolutionToTypedOutcome(
        SourceResolutionKind resolutionKind,
        Type expectedOutcomeType)
    {
        PreviewSourceResolution resolution = CreateResolution(resolutionKind);
        var resolver = new StubSourceResolver(resolution);
        TrickplayPreview preview = CreatePreview(resolver);

        PreviewOutcome outcome = await ((ITrickplayPreview)preview).GetAsync(
            new PreviewQuery(itemId, null, 0),
            new ClaimsPrincipal(),
            [],
            CancellationToken.None);

        Assert.IsType(expectedOutcomeType, outcome);
    }

    [Fact]
    public async Task RejectsNegativePositionBeforeSourceResolution()
    {
        var resolver = new StubSourceResolver(new PreviewSourceResolution.NotFound());
        TrickplayPreview preview = CreatePreview(resolver);

        PreviewOutcome outcome = await ((ITrickplayPreview)preview).GetAsync(
            new PreviewQuery(itemId, null, -1),
            new ClaimsPrincipal(),
            [],
            CancellationToken.None);

        Assert.IsType<PreviewOutcome.BadRequest>(outcome);
        Assert.Equal(0, resolver.CallCount);
    }

    [Fact]
    public async Task PropagatesRequestCancellationWithoutConvertingItToAnOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var resolver = new CancellingSourceResolver();
        TrickplayPreview preview = CreatePreview(resolver);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ((ITrickplayPreview)preview).GetAsync(
                new PreviewQuery(itemId, null, 0),
                new ClaimsPrincipal(),
                [],
                cancellation.Token));
    }

    public static TheoryData<SourceResolutionKind, Type> ExpectedResolutionOutcomes => new()
    {
        { SourceResolutionKind.Unauthorized, typeof(PreviewOutcome.Unauthorized) },
        { SourceResolutionKind.Forbidden, typeof(PreviewOutcome.Forbidden) },
        { SourceResolutionKind.NotFound, typeof(PreviewOutcome.NotFound) },
    };

    private static PreviewSourceResolution CreateResolution(SourceResolutionKind resolutionKind)
    {
        return resolutionKind switch
        {
            SourceResolutionKind.Unauthorized => new PreviewSourceResolution.Unauthorized(),
            SourceResolutionKind.Forbidden => new PreviewSourceResolution.Forbidden(),
            SourceResolutionKind.NotFound => new PreviewSourceResolution.NotFound(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(resolutionKind),
                resolutionKind,
                "Unknown source resolution kind."),
        };
    }

    private static TrickplayPreview CreatePreview(IPreviewSourceResolver resolver)
    {
        return new TrickplayPreview(
            resolver,
            new UnreachablePreviewCache(),
            new UnreachablePreviewEncoder(),
            NullLogger<TrickplayPreview>.Instance);
    }

    private sealed class StubSourceResolver : IPreviewSourceResolver
    {
        private readonly PreviewSourceResolution resolution;
        private int callCount;

        public StubSourceResolver(PreviewSourceResolution resolution)
        {
            this.resolution = resolution;
        }

        public int CallCount => Volatile.Read(ref callCount);

        public Task<PreviewSourceResolution> ResolveAsync(
            PreviewQuery query,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(resolution);
        }
    }

    private sealed class CancellingSourceResolver : IPreviewSourceResolver
    {
        public Task<PreviewSourceResolution> ResolveAsync(
            PreviewQuery query,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
        {
            return Task.FromCanceled<PreviewSourceResolution>(cancellationToken);
        }
    }

    private sealed class UnreachablePreviewCache : IPreviewCache
    {
        public Task<PreviewCacheResult> GetOrCreateAsync(
            PreviewIdentity identity,
            Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The cache must not be reached for a typed source failure.");
        }

        public Task ClearAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Cleanup is outside this request-outcome specification.");
        }
    }

    private sealed class UnreachablePreviewEncoder : ITrickplayPreviewEncoder
    {
        public Task<PreviewEncodingTelemetry> EncodeAsync(
            ResolvedPreviewSource source,
            Stream destination,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The encoder must not be reached for a typed source failure.");
        }
    }

    public enum SourceResolutionKind
    {
        Unauthorized,
        Forbidden,
        NotFound,
    }
}
