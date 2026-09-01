namespace Jellyfin.Plugin.TrickplayCropper.Imaging;

/// <summary>
/// Carries native decode and JPEG encode durations.
/// </summary>
/// <param name="Decode">The scanline decode duration.</param>
/// <param name="Encode">The JPEG encode duration.</param>
internal sealed record PreviewEncodingTelemetry(TimeSpan Decode, TimeSpan Encode);
