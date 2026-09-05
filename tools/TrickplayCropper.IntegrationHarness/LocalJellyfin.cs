using System.Net;
using System.Text.Json;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Validates only supplied subjects and gates the deployed plugin using read-only HTTP requests.</summary>
public sealed class LocalJellyfin(HttpClient http)
{
    private static readonly Guid pluginId = new("630fb758-9a29-4f2c-a54c-95793651bb8a");
    private static readonly TimeSpan healthTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan debugTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Requires an administrator user and user-scoped playable/concealed subject roles.</summary>
    public async Task ValidateAsync(HarnessInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        using JsonDocument user = await ReadJsonAsync("/Users/Me", cancellationToken).ConfigureAwait(false);
        Guid userId = Guid.Parse(user.RootElement.GetProperty("Id").GetString()!);
        JsonElement policy = user.RootElement.GetProperty("Policy");
        Require(userId != Guid.Empty && policy.GetProperty("IsAdministrator").GetBoolean()
            && policy.GetProperty("EnableMediaPlayback").GetBoolean(), "An administrator user with playback access is required.");
        foreach (Guid item in input.PlayableItems)
        {
            await ValidatePlayableAsync(item, userId, cancellationToken).ConfigureAwait(false);
        }

        using HttpResponseMessage invisible = await http.GetAsync(
            $"/Items/{input.InvisibleItem:N}/PlaybackInfo?userId={userId:N}", cancellationToken).ConfigureAwait(false);
        Require(invisible.StatusCode == HttpStatusCode.NotFound, "The invisible Item must be concealed from this user.");
    }

    /// <summary>Waits for host health within a fixed deadline and per-request timeout.</summary>
    public async Task WaitForHealthAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(healthTimeout);
        while (true)
        {
            try
            {
                using HttpResponseMessage response = await http.GetAsync("/health", timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // A restarting host may briefly refuse the connection.
            }
            catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
            {
                // A single timed-out request does not consume the whole health budget.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Proves the expected Active version and a fresh real FrameSelected Debug event after one JPEG GET.</summary>
    public async Task VerifyDeploymentAsync(HarnessInput input, string version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await WaitForHealthAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Health gate passed; waiting for plugin inventory readiness.");
        using JsonDocument plugins = await ReadStartedPluginsAsync(cancellationToken).ConfigureAwait(false);
        Require(plugins.RootElement.EnumerateArray().Any(plugin => Guid.Parse(plugin.GetProperty("Id").GetString()!) == pluginId
            && plugin.GetProperty("Status").GetString() == "Active"
            && plugin.GetProperty("Version").GetString() == version), "The deployed plugin is not Active at its built version.");
        Console.WriteLine("Load-Proof gate passed; requesting a real Preview JPEG.");
        DateTimeOffset since = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using HttpResponseMessage preview = await http.GetAsync(
            $"/TrickplayCropper/Videos/{input.PlayableItems[0]:N}/Preview?PositionTicks=0", cancellationToken).ConfigureAwait(false);
        byte[] jpeg = await preview.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        Require(preview.StatusCode == HttpStatusCode.OK && preview.Content.Headers.ContentType?.MediaType == "image/jpeg"
            && jpeg.Length > 3 && jpeg[0] == 0xff && jpeg[1] == 0xd8 && jpeg[^2] == 0xff && jpeg[^1] == 0xd9,
            "The deployment probe did not return a JPEG.");
        Console.WriteLine("Preview JPEG passed; waiting for fresh structured Debug-Proof.");
        await WaitForDebugAsync(since, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> ReadStartedPluginsAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(healthTimeout);
        while (true)
        {
            try
            {
                using HttpResponseMessage response = await http.GetAsync("/Plugins", timeout.Token).ConfigureAwait(false);
                // Startup can still interrupt connections or return 503 after /health returns 200.
                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                {
                    Require(response.StatusCode == HttpStatusCode.OK, "The plugin inventory endpoint rejected the request.");
                    return JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                }
            }
            catch (HttpRequestException)
            {
                // The host has not finished opening its API listener.
            }
            catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
            {
                // Retain the overall deadline when a single HTTP request times out.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token).ConfigureAwait(false);
        }
    }

    private async Task ValidatePlayableAsync(Guid item, Guid user, CancellationToken cancellationToken)
    {
        using JsonDocument metadata = await ReadJsonAsync(
            $"/Items?ids={item:N}&userId={user:N}&fields=MediaSources&enableImages=false", cancellationToken).ConfigureAwait(false);
        JsonElement[] items = metadata.RootElement.GetProperty("Items").EnumerateArray().ToArray();
        Require(items.Length == 1 && Guid.Parse(items[0].GetProperty("Id").GetString()!) == item
            && items[0].GetProperty("MediaType").GetString() == "Video", "A playable Item lacks user-scoped video playback access.");
        using JsonDocument playback = await ReadJsonAsync(
            $"/Items/{item:N}/PlaybackInfo?userId={user:N}", cancellationToken).ConfigureAwait(false);
        Require(playback.RootElement.GetProperty("MediaSources").EnumerateArray().Any(source =>
            Guid.TryParse(source.GetProperty("Id").GetString(), out Guid sourceId) && sourceId == item
            && !source.GetProperty("IsRemote").GetBoolean()), "A playable Item needs its own local Media Source.");
    }

    private async Task WaitForDebugAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(debugTimeout);
        while (true)
        {
            string text = await ReadNewestLogAsync(timeout.Token).ConfigureAwait(false);
            if (DebugEventReader.HasFrameSelection(text, since))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Reads only the newest server log, excluding encoder and other diagnostic logs.</summary>
    public async Task<string> ReadNewestLogAsync(CancellationToken cancellationToken)
    {
        using JsonDocument logs = await ReadJsonAsync("/System/Logs", cancellationToken).ConfigureAwait(false);
        string newest = logs.RootElement.EnumerateArray()
            .Where(log => IsServerLog(log.GetProperty("Name").GetString()!))
            .OrderByDescending(log => log.GetProperty("DateModified").GetDateTimeOffset())
            .ThenBy(log => log.GetProperty("Name").GetString(), StringComparer.Ordinal)
            .First().GetProperty("Name").GetString()!;
        return await http.GetStringAsync(
            $"/System/Logs/Log?name={Uri.EscapeDataString(newest)}", cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> ReadJsonAsync(string route, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(route, cancellationToken).ConfigureAwait(false);
        Require(response.StatusCode == HttpStatusCode.OK, "A Jellyfin validation endpoint rejected the request.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    private static bool IsServerLog(string name) =>
        (name.StartsWith("jellyfin", StringComparison.OrdinalIgnoreCase) || name.StartsWith("log_", StringComparison.Ordinal))
        && (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
