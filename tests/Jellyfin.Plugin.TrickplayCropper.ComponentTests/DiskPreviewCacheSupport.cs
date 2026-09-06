using System.Reflection;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal static class DiskPreviewCacheSupport
{
    internal static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);
    internal static IApplicationPaths CreateApplicationPaths(string temporaryDirectory)
    {
        IApplicationPaths paths = DispatchProxy.Create<IApplicationPaths, ApplicationPathsSpecs>();
        ((ApplicationPathsSpecs)(object)paths).TemporaryDirectory = temporaryDirectory;
        return paths;
    }

    internal static PreviewIdentity CreateIdentity()
    {
        return CreateIdentity("f0000000000.jpg");
    }

    internal static PreviewIdentity CreateIdentity(string entryName)
    {
        return new PreviewIdentity(
            "0123456789abcdef0123456789abcdef",
            "\"0123456789abcdef0123456789abcdef-f0000000000\"",
            Path.Combine(
                "3f728b7b4aa54f65b488a6029edb6725",
                "w0320",
                "s000000-0123456789abcdef0123456789abcdef",
                entryName));
    }

    internal sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            this.utcNow = utcNow;
        }

        public int GetUtcNowCallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCallCount++;
            return utcNow;
        }
    }

    internal sealed class RecordingProgress : IProgress<double>
    {
        private readonly List<double> values = [];

        public IReadOnlyList<double> Values => values;

        public void Report(double value)
        {
            values.Add(value);
        }
    }

    internal sealed class CallbackProgress(Action<double> callback) : IProgress<double>
    {
        public void Report(double value)
        {
            callback(value);
        }
    }

    internal sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        private readonly List<DiskCacheRecordedLog> entries = [];

        public IReadOnlyList<DiskCacheRecordedLog> Entries
        {
            get
            {
                lock (entries)
                {
                    return entries.ToArray();
                }
            }
        }

        IDisposable? ILogger.BeginScope<TState>(TState state)
        {
            return null;
        }

        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Dictionary<string, object?> properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : [];
            lock (entries)
            {
                entries.Add(new DiskCacheRecordedLog(logLevel, exception, properties));
            }
        }
    }

    internal sealed record DiskCacheRecordedLog(
        LogLevel Level,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);

    internal sealed class PluginDirectoryReparseFixture : IDisposable
    {
        private readonly string externalDirectory;

        private PluginDirectoryReparseFixture(
            TemporaryCacheFixture cacheFixture,
            string externalDirectory,
            RecordingLogger<DiskPreviewCache> logger)
        {
            CacheFixture = cacheFixture;
            this.externalDirectory = externalDirectory;
            ExternalEntryPath = Path.Combine(externalDirectory, "f0000000000.jpg");
            Logger = logger;
        }

        public TemporaryCacheFixture CacheFixture { get; }

        public string ExternalEntryPath { get; }

        public RecordingLogger<DiskPreviewCache> Logger { get; }

        public static async Task<PluginDirectoryReparseFixture> CreateAsync()
        {
            string externalDirectory = Path.Combine(
                Path.GetTempPath(),
                $"trickplay-plugin-external-{Guid.NewGuid():N}");
            Directory.CreateDirectory(externalDirectory);
            var logger = new RecordingLogger<DiskPreviewCache>();
            TemporaryCacheFixture cacheFixture = TemporaryCacheFixture.Create(
                static _ => { },
                TimeProvider.System,
                logger);
            var fixture = new PluginDirectoryReparseFixture(
                cacheFixture,
                externalDirectory,
                logger);
            await File.WriteAllBytesAsync(fixture.ExternalEntryPath, [9], CancellationToken.None);
            Directory.CreateSymbolicLink(cacheFixture.PluginRoot, externalDirectory);
            return fixture;
        }

        public void Dispose()
        {
            CacheFixture.Dispose();
            Directory.Delete(externalDirectory, recursive: true);
        }
    }

    internal sealed class TemporaryCacheFixture : IDisposable
    {
        private readonly string temporaryDirectory;

        private TemporaryCacheFixture(
            string temporaryDirectory,
            PreviewIdentity identity,
            PreviewCacheCoordination coordination,
            TimeProvider timeProvider,
            ILogger<DiskPreviewCache> logger)
        {
            this.temporaryDirectory = temporaryDirectory;
            Identity = identity;
            FinalPath = Path.Combine(
                temporaryDirectory,
                "Jellyfin.Plugin.TrickplayCropper",
                "preview-v1",
                identity.RelativePath);
            Coordination = coordination;
            Cache = new DiskPreviewCache(
                CreateApplicationPaths(temporaryDirectory),
                timeProvider,
                coordination,
                logger);
        }

        public DiskPreviewCache Cache { get; }

        public string CacheRoot => Path.Combine(
            temporaryDirectory,
            "Jellyfin.Plugin.TrickplayCropper",
            PreviewIdentity.CacheNamespace);

        public string PluginRoot => Path.Combine(
            temporaryDirectory,
            "Jellyfin.Plugin.TrickplayCropper");

        public string FinalPath { get; }

        public PreviewIdentity Identity { get; }

        public PreviewCacheCoordination Coordination { get; }

        public static TemporaryCacheFixture Create()
        {
            return Create(static _ => { });
        }

        public static TemporaryCacheFixture Create(
            Action<PreviewCacheCheckpoint> checkpointObserver)
        {
            return Create(
                checkpointObserver,
                TimeProvider.System,
                NullLogger<DiskPreviewCache>.Instance);
        }

        public static TemporaryCacheFixture Create(
            Action<PreviewCacheCheckpoint> checkpointObserver,
            TimeProvider timeProvider,
            ILogger<DiskPreviewCache> logger)
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"trickplay-cache-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            PreviewIdentity identity = CreateIdentity();
            var coordination = new PreviewCacheCoordination(logger, checkpointObserver);
            return new TemporaryCacheFixture(
                temporaryDirectory,
                identity,
                coordination,
                timeProvider,
                logger);
        }

        public void Dispose()
        {
            Cache.Dispose();
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
