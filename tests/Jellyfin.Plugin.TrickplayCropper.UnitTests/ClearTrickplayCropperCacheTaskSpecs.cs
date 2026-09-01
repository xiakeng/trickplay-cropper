using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Tasks;
using MediaBrowser.Model.Tasks;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class ClearTrickplayCropperCacheTaskSpecs
{
    [Fact]
    public void JellyfinDiscoversStableTaskMetadataAndDefaultTrigger()
    {
        var task = new ClearTrickplayCropperCacheTask(new RecordingMaintenance());

        Type[] discoveredTasks = typeof(Plugin).Assembly
            .GetExportedTypes()
            .Where(type => typeof(IScheduledTask).IsAssignableFrom(type))
            .ToArray();
        TaskTriggerInfo trigger = Assert.Single(task.GetDefaultTriggers());

        Assert.Contains(typeof(ClearTrickplayCropperCacheTask), discoveredTasks);
        Assert.Equal("Clear Trickplay Cropper Cache", task.Name);
        Assert.Equal("ClearTrickplayCropperCache", task.Key);
        Assert.Equal("Maintenance", task.Category);
        Assert.Equal(
            "Deletes cached previews and orphaned temporary files created by Trickplay Cropper.",
            task.Description);
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromHours(3).Ticks, trigger.TimeOfDayTicks);
    }

    [Fact]
    public async Task ForwardsProgressAndCancellationToCacheMaintenance()
    {
        var maintenance = new RecordingMaintenance();
        var task = new ClearTrickplayCropperCacheTask(maintenance);
        var progress = new Progress<double>();
        using var cancellation = new CancellationTokenSource();

        await task.ExecuteAsync(progress, cancellation.Token);

        Assert.Same(progress, maintenance.Progress);
        Assert.Equal(cancellation.Token, maintenance.CancellationToken);
    }

    private sealed class RecordingMaintenance : IPreviewCacheMaintenance
    {
        public CancellationToken CancellationToken { get; private set; }

        public IProgress<double>? Progress { get; private set; }

        public Task ClearAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            Progress = progress;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
