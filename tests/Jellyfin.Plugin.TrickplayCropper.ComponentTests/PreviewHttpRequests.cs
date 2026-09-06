using System.Globalization;
using Jellyfin.Database.Implementations.Entities;
using Microsoft.Extensions.DependencyInjection;

using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.PreviewHttpTestValues;
using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.TrickplayPreviewHttpSupport;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;


/// <summary>
/// Sends Preview requests through a component-test host without owning its lifecycle.
/// </summary>
internal static class PreviewHttpRequests
{
    public static Task<HttpResponseMessage> GetAsync(this PreviewHostFixture fixture)
    {
        return fixture.GetAsync(CancellationToken.None);
    }

    public static Task<HttpResponseMessage> GetAsync(
        this PreviewHostFixture fixture,
        CancellationToken cancellationToken)
    {
        return SendAsync(fixture, HttpMethod.Get, null, null, cancellationToken);
    }

    public static Task<HttpResponseMessage> GetConditionalAsync(
        this PreviewHostFixture fixture,
        string ifNoneMatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ifNoneMatch);
        return SendAsync(fixture, HttpMethod.Get, ifNoneMatch, null, CancellationToken.None);
    }

    public static Task<HttpResponseMessage> HeadAsync(this PreviewHostFixture fixture)
    {
        return fixture.HeadAsync(CancellationToken.None);
    }

    public static Task<HttpResponseMessage> HeadAsync(
        this PreviewHostFixture fixture,
        CancellationToken cancellationToken)
    {
        return SendAsync(fixture, HttpMethod.Head, null, null, cancellationToken);
    }

    public static Task<HttpResponseMessage> HeadConditionalAsync(
        this PreviewHostFixture fixture,
        string ifNoneMatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ifNoneMatch);
        return SendAsync(fixture, HttpMethod.Head, ifNoneMatch, null, CancellationToken.None);
    }

    public static Task<HttpResponseMessage> HeadRawAsync(
        this PreviewHostFixture fixture,
        string requestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        return SendAsync(fixture, HttpMethod.Head, null, requestPath, CancellationToken.None);
    }

    public static Task<HttpResponseMessage> SendVerbAsync(this PreviewHostFixture fixture, string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return SendAsync(fixture, new HttpMethod(method), null, null, CancellationToken.None);
    }

    public static string[] EnumerateCacheFiles(this PreviewHostFixture fixture)
    {
        return Directory.Exists(fixture.CacheRoot)
            ? Directory.EnumerateFiles(fixture.CacheRoot, "*", SearchOption.AllDirectories).ToArray()
            : [];
    }

    public static void SetPlaybackAccess(this PreviewHostFixture fixture, bool hasPlaybackAccess)
    {
        PreviewHttpJellyfinFakes.SetPlaybackPermission(
            fixture.Services.GetRequiredService<User>(),
            hasPlaybackAccess);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        PreviewHostFixture fixture,
        HttpMethod method,
        string? ifNoneMatch,
        string? requestPath,
        CancellationToken cancellationToken)
    {
        PreviewScenario scenario = fixture.Services.GetRequiredService<PreviewScenario>();
        string path = requestPath ?? CreateRequestPath(scenario);
        using var request = new HttpRequestMessage(method, path);
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return await fixture.Client.SendAsync(request, cancellationToken);
    }

    private static string CreateRequestPath(PreviewScenario scenario)
    {
        string mediaSourceQuery = scenario.UsesAlternateSource
            ? $"MediaSourceId={AlternateSourceId.ToString(scenario.MediaSourceIdFormat)}&"
            : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"/TrickplayCropper/Videos/{scenario.LogicalItemId:D}/Preview?{mediaSourceQuery}"
            + $"PositionTicks={scenario.RequestPositionTicks}");
    }
}
