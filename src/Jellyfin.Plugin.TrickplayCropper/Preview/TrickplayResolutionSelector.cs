namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Derives the one Selected Trickplay Resolution a Preview request serves from Jellyfin's current configuration.
/// </summary>
internal static class TrickplayResolutionSelector
{
    /// <summary>
    /// Selects the minimum positive Trickplay Resolution Target, clamps it to the matched source width when
    /// smaller, and normalizes the result to an even width using Jellyfin's round-down rule.
    /// </summary>
    /// <param name="configuredTargets">The current Trickplay Resolution Targets, or null when the snapshot is unreadable.</param>
    /// <param name="sourceVideoWidth">The matched Media Source video width, or null when the source reports none.</param>
    /// <returns>The even positive Selected Trickplay Resolution, or null when no target is configured.</returns>
    /// <exception cref="InvalidTrickplayConfigurationException">The snapshot cannot describe one Selected Trickplay Resolution.</exception>
    public static int? Select(int[]? configuredTargets, int? sourceVideoWidth)
    {
        if (configuredTargets is null)
        {
            throw new InvalidTrickplayConfigurationException("ConfigurationReadable", failedValue: null);
        }

        if (configuredTargets.Length == 0)
        {
            return null;
        }

        foreach (int configuredTarget in configuredTargets)
        {
            if (configuredTarget <= 0)
            {
                throw new InvalidTrickplayConfigurationException("ConfiguredTargetPositive", configuredTarget);
            }
        }

        int chosenTarget = configuredTargets.Min();
        int clampedWidth = sourceVideoWidth is int sourceWidth && sourceWidth < chosenTarget
            ? sourceWidth
            : chosenTarget;
        int selectedResolution = checked(clampedWidth - (clampedWidth % 2));
        if (selectedResolution <= 0)
        {
            throw new InvalidTrickplayConfigurationException("SelectedResolutionPositive", selectedResolution);
        }

        return selectedResolution;
    }
}
