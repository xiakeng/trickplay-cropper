using System.Globalization;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class TrickplayResolutionSelectorSpecs
{
    [Theory]
    [InlineData(new[] { 320 }, null, 320)]
    [InlineData(new[] { 321 }, null, 320)]
    [InlineData(new[] { 319 }, null, 318)]
    [InlineData(new[] { 1023 }, null, 1022)]
    [InlineData(new[] { 2 }, null, 2)]
    public void NormalizesTheOnlyConfiguredTargetToAnEvenSelectedResolution(
        int[] configuredTargets,
        int? sourceVideoWidth,
        int selectedResolution)
    {
        Assert.Equal(
            selectedResolution,
            TrickplayResolutionSelector.Select(configuredTargets, sourceVideoWidth));
    }

    [Theory]
    [InlineData(new[] { 640, 320, 480 }, 320)]
    [InlineData(new[] { 320, 640, 480 }, 320)]
    [InlineData(new[] { 480, 320 }, 320)]
    [InlineData(new[] { 1920, 1280, 640 }, 640)]
    public void SelectsTheMinimumPositiveTargetIndependentOfOrder(int[] configuredTargets, int selectedResolution)
    {
        Assert.Equal(
            selectedResolution,
            TrickplayResolutionSelector.Select(configuredTargets, sourceVideoWidth: null));
    }

    [Theory]
    [InlineData(new[] { 320, 320 }, 320)]
    [InlineData(new[] { 320, 320, 480 }, 320)]
    [InlineData(new[] { 480, 480 }, 480)]
    public void TreatsPositiveDuplicatesAsOneTarget(int[] configuredTargets, int selectedResolution)
    {
        Assert.Equal(
            selectedResolution,
            TrickplayResolutionSelector.Select(configuredTargets, sourceVideoWidth: null));
    }

    [Fact]
    public void ReportsNoConfiguredTargetForAnEmptySnapshot()
    {
        Assert.Null(TrickplayResolutionSelector.Select([], sourceVideoWidth: null));
    }

    [Theory]
    [InlineData(new[] { 320 }, 300, 300)]
    [InlineData(new[] { 640 }, 300, 300)]
    [InlineData(new[] { 320 }, 301, 300)]
    [InlineData(new[] { 321 }, 301, 300)]
    [InlineData(new[] { 640, 480 }, 400, 400)]
    public void ClampsTheChosenTargetToASmallerSourceWidthBeforeEvenNormalization(
        int[] configuredTargets,
        int sourceVideoWidth,
        int selectedResolution)
    {
        Assert.Equal(
            selectedResolution,
            TrickplayResolutionSelector.Select(configuredTargets, sourceVideoWidth));
    }

    [Theory]
    [InlineData(new[] { 320 }, 320, 320)]
    [InlineData(new[] { 320 }, 640, 320)]
    [InlineData(new[] { 320 }, null, 320)]
    public void DoesNotClampWhenTheSourceWidthIsAbsentOrNotSmaller(
        int[] configuredTargets,
        int? sourceVideoWidth,
        int selectedResolution)
    {
        Assert.Equal(
            selectedResolution,
            TrickplayResolutionSelector.Select(configuredTargets, sourceVideoWidth));
    }

    [Fact]
    public void RejectsAnUnreadableConfigurationSnapshot()
    {
        InvalidTrickplayConfigurationException exception = Assert.Throws<InvalidTrickplayConfigurationException>(
            () => TrickplayResolutionSelector.Select(configuredTargets: null, sourceVideoWidth: null));

        Assert.Equal("ConfigurationReadable", exception.FailedValidation);
    }

    [Theory]
    [InlineData(new[] { 0 }, 0)]
    [InlineData(new[] { -1 }, -1)]
    [InlineData(new[] { 320, 0 }, 0)]
    [InlineData(new[] { 320, -1 }, -1)]
    public void RejectsAnyNonPositiveConfiguredTarget(int[] configuredTargets, int failedValue)
    {
        InvalidTrickplayConfigurationException exception = Assert.Throws<InvalidTrickplayConfigurationException>(
            () => TrickplayResolutionSelector.Select(configuredTargets, sourceVideoWidth: null));

        Assert.Equal("ConfiguredTargetPositive", exception.FailedValidation);
        Assert.Equal(failedValue, exception.FailedValue);
    }

    [Theory]
    [InlineData(new[] { 1 }, null, 0)]
    [InlineData(new[] { 320 }, 1, 0)]
    [InlineData(new[] { 320 }, 0, 0)]
    [InlineData(new[] { 2 }, 1, 0)]
    [InlineData(new[] { 320 }, -5, -4)]
    public void RejectsANonPositiveNormalizedSelectedResolution(
        int[] configuredTargets,
        int? sourceVideoWidth,
        int failedValue)
    {
        InvalidTrickplayConfigurationException exception = Assert.Throws<InvalidTrickplayConfigurationException>(
            () => TrickplayResolutionSelector.Select(configuredTargets, sourceVideoWidth));

        Assert.Equal("SelectedResolutionPositive", exception.FailedValidation);
        Assert.Equal(failedValue, exception.FailedValue);
        Assert.Contains(
            failedValue.ToString(CultureInfo.InvariantCulture),
            exception.Message,
            StringComparison.Ordinal);
    }
}
