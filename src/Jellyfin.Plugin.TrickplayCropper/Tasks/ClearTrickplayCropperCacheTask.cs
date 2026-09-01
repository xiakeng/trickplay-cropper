using Jellyfin.Plugin.TrickplayCropper.Caching;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.TrickplayCropper.Tasks;

/// <summary>
/// Exposes Preview Cache Entry cleanup to Jellyfin's scheduled-task host.
/// </summary>
public sealed class ClearTrickplayCropperCacheTask : IScheduledTask
{
    private const string TaskDescription =
        "Deletes cached previews and orphaned temporary files created by Trickplay Cropper.";
    private const int DefaultTriggerHour = 3;

    private readonly IPreviewCacheMaintenance maintenance;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClearTrickplayCropperCacheTask"/> class.
    /// </summary>
    /// <param name="maintenance">The shared Preview Cache maintenance module.</param>
    public ClearTrickplayCropperCacheTask(IPreviewCacheMaintenance maintenance)
    {
        ArgumentNullException.ThrowIfNull(maintenance);
        this.maintenance = maintenance;
    }

    /// <inheritdoc />
    public string Name => "Clear Trickplay Cropper Cache";

    /// <inheritdoc />
    public string Key => "ClearTrickplayCropperCache";

    /// <inheritdoc />
    public string Description => TaskDescription;

    /// <inheritdoc />
    public string Category => "Maintenance";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return maintenance.ClearAsync(progress, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(DefaultTriggerHour).Ticks,
            },
        ];
    }
}
