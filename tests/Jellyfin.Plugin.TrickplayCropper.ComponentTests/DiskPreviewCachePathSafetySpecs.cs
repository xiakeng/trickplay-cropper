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

using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.DiskPreviewCacheSupport;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class DiskPreviewCachePathSafetySpecs
{
    [Fact]
    public async Task RejectsARequestPathThatCrossesADirectoryReparsePoint()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string externalDirectory = Path.Combine(
            Path.GetTempPath(),
            $"trickplay-request-external-{Guid.NewGuid():N}");
        Directory.CreateDirectory(externalDirectory);
        try
        {
            using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
            Directory.CreateDirectory(fixture.CacheRoot);
            string firstIdentityDirectory = fixture.Identity.RelativePath.Split(Path.DirectorySeparatorChar)[0];
            Directory.CreateSymbolicLink(
                Path.Combine(fixture.CacheRoot, firstIdentityDirectory),
                externalDirectory);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => fixture.Cache.GetOrCreateAsync(
                    fixture.Identity,
                    async (destination, cancellationToken) =>
                    {
                        await destination.WriteAsync(new byte[] { 1 }, cancellationToken);
                        return new PreviewEncodingTelemetry(TimeSpan.Zero, TimeSpan.Zero);
                    },
                    CancellationToken.None).WaitAsync(CoordinationTimeout));

            Assert.Empty(Directory.EnumerateFileSystemEntries(externalDirectory));
        }
        finally
        {
            Directory.Delete(externalDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RequestsRejectAPluginDirectoryReparsePoint()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using PluginDirectoryReparseFixture fixture = await PluginDirectoryReparseFixture.CreateAsync();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.CacheFixture.Cache.GetOrCreateAsync(
                fixture.CacheFixture.Identity,
                (_, _) => throw new InvalidOperationException("A reparse path must fail before generation."),
                CancellationToken.None).WaitAsync(CoordinationTimeout));

        Assert.True(File.Exists(fixture.ExternalEntryPath));
    }

    [Fact]
    public async Task CleanupRejectsAPluginDirectoryReparsePoint()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using PluginDirectoryReparseFixture fixture = await PluginDirectoryReparseFixture.CreateAsync();

        await fixture.CacheFixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
            .WaitAsync(CoordinationTimeout);

        Assert.True(File.Exists(fixture.ExternalEntryPath));
        DiskCacheRecordedLog warning = Assert.Single(
            fixture.Logger.Entries,
            entry => entry.Level == LogLevel.Warning);
        Assert.Equal(fixture.CacheFixture.PluginRoot, warning.Properties["CachePath"]);
    }

    [Fact]
    public async Task RequestsRejectALexicalCacheTreeEscape()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        string escapedPath = Path.Combine(fixture.PluginRoot, "escaped.jpg");
        var escapingIdentity = fixture.Identity with
        {
            RelativePath = Path.Combine("..", "escaped.jpg"),
        };
        bool generationStarted = false;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Cache.GetOrCreateAsync(
                escapingIdentity,
                (_, _) =>
                {
                    generationStarted = true;
                    return Task.FromResult(new PreviewEncodingTelemetry(TimeSpan.Zero, TimeSpan.Zero));
                },
                CancellationToken.None).WaitAsync(CoordinationTimeout));

        Assert.False(generationStarted);
        Assert.False(File.Exists(escapedPath));
    }

    [Fact]
    public async Task RejectsAFinalEntryThatBecomesAReparsePointBeforePublication()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string externalPath = Path.Combine(
            Path.GetTempPath(),
            $"trickplay-request-external-{Guid.NewGuid():N}.jpg");
        byte[] externalContent = [9, 8, 7];
        await File.WriteAllBytesAsync(externalPath, externalContent, CancellationToken.None);
        try
        {
            using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();

            await Assert.ThrowsAsync<InvalidDataException>(
                () => fixture.Cache.GetOrCreateAsync(
                    fixture.Identity,
                    async (destination, cancellationToken) =>
                    {
                        await destination.WriteAsync(new byte[] { 1 }, cancellationToken);
                        File.CreateSymbolicLink(fixture.FinalPath, externalPath);
                        return new PreviewEncodingTelemetry(TimeSpan.Zero, TimeSpan.Zero);
                    },
                    CancellationToken.None).WaitAsync(CoordinationTimeout));

            Assert.Equal(externalContent, await File.ReadAllBytesAsync(externalPath, CancellationToken.None));
        }
        finally
        {
            File.Delete(externalPath);
        }
    }

    [Fact]
    public async Task DoesNotTraverseANestedDirectoryReparsePoint()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string externalDirectory = Path.Combine(
            Path.GetTempPath(),
            $"trickplay-external-{Guid.NewGuid():N}");
        Directory.CreateDirectory(externalDirectory);
        string externalEntryPath = Path.Combine(externalDirectory, "f0000000000.jpg");
        await File.WriteAllBytesAsync(externalEntryPath, [1], CancellationToken.None);
        try
        {
            var logger = new RecordingLogger<DiskPreviewCache>();
            using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
                static _ => { },
                TimeProvider.System,
                logger);
            Directory.CreateDirectory(fixture.CacheRoot);
            string linkedPath = Path.Combine(fixture.CacheRoot, "linked");
            Directory.CreateSymbolicLink(linkedPath, externalDirectory);

            await fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
                .WaitAsync(CoordinationTimeout);

            Assert.True(File.Exists(externalEntryPath));
            RecordedLog warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.Equal(linkedPath, warning.Properties["CachePath"]);
        }
        finally
        {
            Directory.Delete(externalDirectory, recursive: true);
        }
    }

}
