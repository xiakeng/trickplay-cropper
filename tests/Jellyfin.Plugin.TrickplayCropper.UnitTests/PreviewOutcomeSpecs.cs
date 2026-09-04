using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using Xunit;
using static Jellyfin.Plugin.TrickplayCropper.UnitTests.PreviewContextMother;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class PreviewOutcomeSpecs
{
    private static readonly Guid itemId = Guid.Parse("5dd8f9cc-b38d-45fb-9a97-0358b5ef25e2");

    [Fact]
    public void ProductionOutcomeTypesMatchTheContract()
    {
        Type outcomeType = typeof(PreviewOutcome);
        string[] outcomeNames = outcomeType.Assembly
            .GetTypes()
            .Where(type => type != outcomeType && outcomeType.IsAssignableFrom(type))
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
    [MemberData(nameof(ExpectedContextOutcomes))]
    public async Task MapsSharedContextFailureToTypedOutcomeWithoutGetOnlyWork(
        ContextFailureKind failureKind,
        Type expectedOutcomeType)
    {
        var contextResolver = new StubResolver(CreateResolution(failureKind));
        var sourceResolver = new StubSourceResolver(new PreviewSourceResolution.NotFound());
        TrickplayPreview preview = CreatePreview(contextResolver, sourceResolver);

        PreviewOutcome outcome = await ((ITrickplayPreview)preview).GetAsync(
            new PreviewQuery(itemId, null, 0),
            new ClaimsPrincipal(),
            [],
            CancellationToken.None);

        Assert.IsType(expectedOutcomeType, outcome);
        Assert.Equal(1, contextResolver.CallCount);
        Assert.Equal(0, sourceResolver.CallCount);
    }

    [Fact]
    public async Task MapsUnavailableSourceSpriteToNotFoundWithoutCacheAccess()
    {
        var contextResolver = new StubResolver(
            new PreviewContextResolution.Resolved(CreateContext()));
        var sourceResolver = new StubSourceResolver(new PreviewSourceResolution.NotFound());
        TrickplayPreview preview = CreatePreview(contextResolver, sourceResolver);

        PreviewOutcome outcome = await ((ITrickplayPreview)preview).GetAsync(
            new PreviewQuery(itemId, null, 0),
            new ClaimsPrincipal(),
            [],
            CancellationToken.None);

        Assert.IsType<PreviewOutcome.NotFound>(outcome);
        Assert.Equal(1, sourceResolver.CallCount);
    }

    [Fact]
    public async Task PropagatesRequestCancellationWithoutConvertingItToAnOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var contextResolver = new CancellingResolver();
        var sourceResolver = new StubSourceResolver(new PreviewSourceResolution.NotFound());
        TrickplayPreview preview = CreatePreview(contextResolver, sourceResolver);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ((ITrickplayPreview)preview).GetAsync(
                new PreviewQuery(itemId, null, 0),
                new ClaimsPrincipal(),
                [],
                cancellation.Token));

        Assert.Equal(0, sourceResolver.CallCount);
    }

    [Theory]
    [MemberData(nameof(ExpectedConditionalOutcomes))]
    public async Task ComparesConditionalEntityTagsUsingWeakSemantics(
        ConditionalRequestKind requestKind,
        Type expectedOutcomeType,
        int expectedCacheCalls)
    {
        PreviewContext context = CreateContext();
        ResolvedPreviewSource source = CreateSource(context);
        var contextResolver = new StubResolver(new PreviewContextResolution.Resolved(context));
        var sourceResolver = new StubSourceResolver(new PreviewSourceResolution.Found(source));
        var cache = new StubPreviewCache();
        TrickplayPreview preview = CreatePreview(contextResolver, sourceResolver, cache);
        string entityTag = PreviewIdentity.Create(source).EntityTag;

        PreviewOutcome outcome = await ((ITrickplayPreview)preview).GetAsync(
            new PreviewQuery(itemId, null, 0),
            new ClaimsPrincipal(),
            CreateConditionalEntityTags(requestKind, entityTag),
            CancellationToken.None);

        Assert.IsType(expectedOutcomeType, outcome);
        Assert.Equal(expectedCacheCalls, cache.CallCount);
    }

    [Fact]
    public async Task ConvertsUnexpectedCacheFailureToInternalError()
    {
        PreviewContext context = CreateContext();
        var contextResolver = new StubResolver(new PreviewContextResolution.Resolved(context));
        var sourceResolver = new StubSourceResolver(
            new PreviewSourceResolution.Found(CreateSource(context)));
        var cache = new StubPreviewCache(new IOException("The unit-test cache failed."));
        TrickplayPreview preview = CreatePreview(contextResolver, sourceResolver, cache);

        PreviewOutcome outcome = await ((ITrickplayPreview)preview).GetAsync(
            new PreviewQuery(itemId, null, 0),
            new ClaimsPrincipal(),
            [],
            CancellationToken.None);

        Assert.IsType<PreviewOutcome.InternalError>(outcome);
        Assert.Equal(1, cache.CallCount);
    }

    public static TheoryData<ContextFailureKind, Type> ExpectedContextOutcomes => new()
    {
        { ContextFailureKind.BadRequest, typeof(PreviewOutcome.BadRequest) },
        { ContextFailureKind.Unauthorized, typeof(PreviewOutcome.Unauthorized) },
        { ContextFailureKind.Forbidden, typeof(PreviewOutcome.Forbidden) },
        { ContextFailureKind.NotFound, typeof(PreviewOutcome.NotFound) },
    };

    public static TheoryData<ConditionalRequestKind, Type, int> ExpectedConditionalOutcomes => new()
    {
        { ConditionalRequestKind.Exact, typeof(PreviewOutcome.NotModified), 0 },
        { ConditionalRequestKind.Weak, typeof(PreviewOutcome.NotModified), 0 },
        { ConditionalRequestKind.Wildcard, typeof(PreviewOutcome.NotModified), 0 },
        { ConditionalRequestKind.Stale, typeof(PreviewOutcome.Ok), 1 },
        { ConditionalRequestKind.Missing, typeof(PreviewOutcome.Ok), 1 },
    };

    private static IReadOnlyCollection<EntityTagHeaderValue> CreateConditionalEntityTags(
        ConditionalRequestKind requestKind,
        string entityTag)
    {
        return requestKind switch
        {
            ConditionalRequestKind.Exact => [new EntityTagHeaderValue(entityTag)],
            ConditionalRequestKind.Weak => [new EntityTagHeaderValue(entityTag, isWeak: true)],
            ConditionalRequestKind.Wildcard => [EntityTagHeaderValue.Any],
            ConditionalRequestKind.Stale => [new EntityTagHeaderValue("\"stale-source-f0000000000\"")],
            ConditionalRequestKind.Missing => [],
            _ => throw new ArgumentOutOfRangeException(
                nameof(requestKind),
                requestKind,
                "Unknown conditional request kind."),
        };
    }

    private static TrickplayPreview CreatePreview(
        IPreviewContextResolver contextResolver,
        IPreviewSourceResolver sourceResolver)
    {
        return CreatePreview(contextResolver, sourceResolver, new UnreachablePreviewCache());
    }

    private static TrickplayPreview CreatePreview(
        IPreviewContextResolver contextResolver,
        IPreviewSourceResolver sourceResolver,
        IPreviewCache previewCache)
    {
        return new TrickplayPreview(
            contextResolver,
            sourceResolver,
            previewCache,
            new UnreachablePreviewEncoder(),
            NullLogger<TrickplayPreview>.Instance);
    }

    private static PreviewContext CreateContext()
    {
        return new PreviewContext(
            itemId,
            new Video { Id = itemId },
            new TrickplayMetadata(320, 180, 10_000, 2, 2, 4),
            0);
    }

    private static ResolvedPreviewSource CreateSource(PreviewContext context)
    {
        return new ResolvedPreviewSource(
            context.MediaSourceId,
            "/manager/source-sprite.jpg",
            12_345,
            638_397_614_450_000_000,
            context.Metadata,
            FrameSelection.Create(context.Metadata, context.FrameIndex));
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

        public Task<PreviewSourceResolution> ResolveAsync(PreviewContext context)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(resolution);
        }
    }

    private sealed class UnreachablePreviewCache : IPreviewCache
    {
        public Task<PreviewCacheResult> GetOrCreateAsync(
            PreviewIdentity identity,
            Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The cache must not be reached for a typed request failure.");
        }

        public Task ClearAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Cleanup is outside this request-outcome specification.");
        }
    }

    private sealed class StubPreviewCache : IPreviewCache
    {
        private readonly Exception? failure;
        private int callCount;

        public StubPreviewCache()
        {
        }

        public StubPreviewCache(Exception failure)
        {
            this.failure = failure;
        }

        public int CallCount => Volatile.Read(ref callCount);

        public Task<PreviewCacheResult> GetOrCreateAsync(
            PreviewIdentity identity,
            Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            if (failure is not null)
            {
                throw failure;
            }

            var result = new PreviewCacheResult(
                new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 },
                PreviewCacheDisposition.Hit,
                null);
            return Task.FromResult(result);
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
            throw new InvalidOperationException("The encoder must not be reached for a typed request failure.");
        }
    }

    public enum ConditionalRequestKind
    {
        Exact,
        Weak,
        Wildcard,
        Stale,
        Missing,
    }
}
