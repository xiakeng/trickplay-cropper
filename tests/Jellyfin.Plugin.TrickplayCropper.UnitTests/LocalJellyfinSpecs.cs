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
