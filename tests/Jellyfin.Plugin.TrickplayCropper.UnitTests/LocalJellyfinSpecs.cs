using System.Net;
using System.Text;
using System.Text.Json;
using TrickplayCropper.IntegrationHarness;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class LocalJellyfinSpecs
{
    private const string UserId = "44444444444444444444444444444444";
    private static HarnessInput Input => HarnessInput.Parse("""
        {"adminToken":"abc123","playableItemIds":["11111111111111111111111111111111","22222222222222222222222222222222"],
         "invisibleItemId":"33333333333333333333333333333333"}
        """);

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public async Task RequiresAdministratorPlaybackAndConcealment(bool administrator, bool playback, bool visible, bool accepted)
    {
        using HostResponses handler = new(administrator, playback, visible);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost:8096") };
        Task validate = new LocalJellyfin(http).ValidateAsync(Input, CancellationToken.None);
        if (accepted)
        {
            await validate;
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => validate);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true, false)]
    [InlineData(HttpStatusCode.Unauthorized, false, false)]
    [InlineData(HttpStatusCode.OK, true, true)]
    public async Task DeploymentWaitsForStartupButDoesNotRetryRejectedAuthentication(HttpStatusCode initialStatus, bool accepted, bool disconnectOnce)
    {
        using DeploymentResponses handler = new(initialStatus, disconnectOnce);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost:8096") };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        Task verify = new LocalJellyfin(http).VerifyDeploymentAsync(Input, "1.0.0.0", timeout.Token);

        if (accepted)
        {
            await verify;
            Assert.Equal(2, handler.PluginRequests);
            Assert.Equal(1, handler.PreviewRequests);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => verify);
            Assert.Equal(1, handler.PluginRequests);
            Assert.Equal(0, handler.PreviewRequests);
        }
    }

    private sealed class DeploymentResponses(HttpStatusCode initialStatus, bool disconnectOnce) : HttpMessageHandler
    {
        public int PluginRequests { get; private set; }

        public int PreviewRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string route = request.RequestUri!.AbsolutePath;
            if (route == "/Plugins" && ++PluginRequests == 1)
            {
                if (disconnectOnce)
                {
                    return Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated restarting connection."));
                }

                return Task.FromResult(new HttpResponseMessage(initialStatus));
            }

            if (route.EndsWith("/Preview", StringComparison.Ordinal))
            {
                PreviewRequests++;
                var jpeg = new ByteArrayContent([0xff, 0xd8, 0xff, 0xd9]);
                jpeg.Headers.ContentType = new("image/jpeg");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = jpeg });
            }

            string text = route switch
            {
                "/health" => "Healthy",
                "/Plugins" => """[{"Id":"630fb7589a294f2ca54c95793651bb8a","Status":"Active","Version":"1.0.0.0"}]""",
                "/System/Logs" => """[{"Name":"jellyfin.log","DateModified":"2026-09-05T00:00:00Z"}]""",
                "/System/Logs/Log" => $$"""[{{DateTimeOffset.UtcNow:O}}] [DBG] TrickplayDebug {"EventId":1002,"EventName":"TrickplayPreviewFrameSelected","FrameIndex":0,"SpriteIndex":0}""",
                _ => throw new InvalidOperationException("Unexpected deployment request."),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        }
    }

    private sealed class HostResponses(bool administrator, bool playback, bool visible) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string route = request.RequestUri!.PathAndQuery;
            string json;
            if (route == "/Users/Me")
            {
                json = JsonSerializer.Serialize(new { Id = UserId, Policy = new { IsAdministrator = administrator, EnableMediaPlayback = playback } });
            }
            else if (route.Contains(Input.InvisibleItem.ToString("N"), StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(visible ? HttpStatusCode.OK : HttpStatusCode.NotFound));
            }
            else
            {
                Guid item = Input.PlayableItems.Single(item => route.Contains(item.ToString("N"), StringComparison.Ordinal));
                json = route.StartsWith("/Items?", StringComparison.Ordinal)
                    ? $$"""{"Items":[{"Id":"{{item:N}}","MediaType":"Video"}]}"""
                    : $$"""{"MediaSources":[{"Id":"{{item:N}}","IsRemote":false,"Type":"Default","Protocol":"File"}]}""";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
