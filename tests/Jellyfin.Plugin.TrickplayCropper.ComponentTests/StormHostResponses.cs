using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed class StormHostResponses(string root, string fault = "") : HttpMessageHandler
{
    private readonly HttpMessageInvoker previews = new(new SmokeHostResponses(fault));
    private readonly SemaphoreSlim gate = new(1);
    private readonly StringBuilder log = new();
    private readonly List<string> requests = [];
    private readonly List<TaskCompletionSource> waves = [];
    private int heads;
    private int gets;

    public IReadOnlyList<string> Requests => requests;

    public int LogReads { get; private set; }

    public TaskCompletionSource WaitingRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.RequestUri!.AbsolutePath.EndsWith("/Preview", StringComparison.Ordinal))
        {
            return await ReadEndpointAsync(request, cancellationToken);
        }

        Task barrier = await ArriveAsync(request, cancellationToken);
        await barrier.WaitAsync(cancellationToken);
        if (fault is "cancel" or "timeout")
        {
            WaitingRequest.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (fault == "status")
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            HttpResponseMessage response = await previews.SendAsync(request, cancellationToken);
            if (request.Method == HttpMethod.Get)
            {
                await PublishAsync(request, response, cancellationToken);
            }

            return response;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Task> ArriveAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (request.Method == HttpMethod.Head)
            {
                Assert.Equal(heads / 144 * 144, gets);
                heads++;
            }
            else
            {
                Assert.Equal((gets / 144 + 1) * 144, heads);
                gets++;
            }

            int ordinal = requests.Count;
            if (ordinal % 6 == 0)
            {
                waves.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            requests.Add(request.Method + " " + request.Headers.GetValues("X-Trickplay-Harness-Client").Single()
                + " " + request.RequestUri!.PathAndQuery);
            TaskCompletionSource wave = waves[ordinal / 6];
            if (ordinal % 6 == 5)
            {
                wave.SetResult();
            }

            return wave.Task;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task PublishAsync(HttpRequestMessage request, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        long ticks = long.Parse(request.RequestUri!.Query.Split('=')[1], CultureInfo.InvariantCulture);
        string item = request.RequestUri.Segments[^2].TrimEnd('/');
        int frame = (int)Math.Min(ticks / 25000000, item.StartsWith('1') ? 6 : 4);
        string disposition = response.Headers.GetValues("X-Trickplay-Cache").Single();
        if (fault == "all-hit")
        {
            response.Headers.Remove("X-Trickplay-Cache");
            response.Headers.Add("X-Trickplay-Cache", "HIT");
            disposition = "HIT";
        }

        string prefix = FormattableString.Invariant($"[{DateTimeOffset.UtcNow:O}] [DBG] TrickplayDebug ");
        log.AppendLine(prefix + JsonSerializer.Serialize(new
        {
            EventId = 1002,
            EventName = "TrickplayPreviewFrameSelected",
            FrameIndex = fault == "log-frame" ? 99 : frame,
            SpriteIndex = fault == "log-sprite" ? 1 : 0,
        }));
        log.AppendLine(prefix + JsonSerializer.Serialize(new
        {
            EventId = 1003,
            EventName = "TrickplayPreviewCacheDisposition",
            CacheDisposition = fault == "log-disposition" ? "Miss" : disposition == "MISS" ? "Miss" : "Hit",
        }));
        string path = Path.Combine(root, item, "w0320", string.Concat("s000000-", response.Headers.ETag!.Tag.AsSpan(1, 32)),
            FormattableString.Invariant($"f{frame:D10}.jpg"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
        if (fault == "temporary")
        {
            await File.WriteAllBytesAsync(path + ".pending.tmp", [1], cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> ReadEndpointAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path == "/System/Logs")
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [{"Name":"jellyfin-old.log","DateModified":"2026-09-04T00:00:00Z"},
                     {"Name":"FFmpeg.Transcode-new.log","DateModified":"2026-09-06T00:00:00Z"},
                     {"Name":"jellyfin-new.log","DateModified":"2026-09-05T00:00:00Z"}]
                    """),
            };
        }

        if (path == "/System/Logs/Log")
        {
            Assert.Equal("?name=jellyfin-new.log", request.RequestUri.Query);
            LogReads++;
            if (LogReads == 2 && fault == "temporary")
            {
                foreach (string residue in Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories))
                {
                    File.Delete(residue);
                }
            }
            if (LogReads == 2 && fault.StartsWith("log-", StringComparison.Ordinal))
            {
                throw new IOException("Stop the fixture after proving mismatched events cannot pass quiescence.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(log.ToString()) };
        }

        return await previews.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            previews.Dispose();
            gate.Dispose();
        }

        base.Dispose(disposing);
    }
}
