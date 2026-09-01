using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Identifies a versioned Preview Cache Entry and its HTTP entity.
/// </summary>
/// <param name="SourceStamp">The source-version digest prefix.</param>
/// <param name="EntityTag">The canonical HTTP entity tag.</param>
/// <param name="RelativePath">The structured path beneath the Cache Tree root.</param>
internal sealed record PreviewIdentity(string SourceStamp, string EntityTag, string RelativePath)
{
    /// <summary>
    /// Gets the versioned namespace beneath the Preview Cache Tree root.
    /// </summary>
    internal const string CacheNamespace = "preview-v1";

    /// <summary>
    /// Gets the JPEG quality used for Preview Cache Entries.
    /// </summary>
    internal const int JpegQuality = 90;

    /// <summary>
    /// Creates the canonical v1 identity for a resolved Source Sprite frame.
    /// </summary>
    /// <param name="source">The fully resolved source snapshot.</param>
    /// <returns>The canonical cache and HTTP identity.</returns>
    public static PreviewIdentity Create(ResolvedPreviewSource source)
    {
        string canonical = CreateCanonicalString(source);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        string sourceStamp = Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
        string entityTag = string.Create(
            CultureInfo.InvariantCulture,
            $"\"{sourceStamp}-f{source.Selection.FrameIndex:D10}\"");
        string relativePath = CreateRelativePath(source, sourceStamp);
        return new PreviewIdentity(sourceStamp, entityTag, relativePath);
    }

    private static string CreateCanonicalString(ResolvedPreviewSource source)
    {
        TrickplayMetadata metadata = source.Metadata;
        return string.Join(
            '\n',
            CacheNamespace,
            $"mediaSourceId={source.MediaSourceId:N}",
            $"width={metadata.FrameWidth.ToString(CultureInfo.InvariantCulture)}",
            $"height={metadata.FrameHeight.ToString(CultureInfo.InvariantCulture)}",
            $"intervalMs={metadata.IntervalMilliseconds.ToString(CultureInfo.InvariantCulture)}",
            $"tileWidth={metadata.TileWidth.ToString(CultureInfo.InvariantCulture)}",
            $"tileHeight={metadata.TileHeight.ToString(CultureInfo.InvariantCulture)}",
            $"thumbnailCount={metadata.ThumbnailCount.ToString(CultureInfo.InvariantCulture)}",
            $"spriteIndex={source.Selection.SpriteIndex.ToString(CultureInfo.InvariantCulture)}",
            $"spriteLength={source.SourceLength.ToString(CultureInfo.InvariantCulture)}",
            $"spriteLastWriteUtcTicks={source.SourceLastWriteUtcTicks.ToString(CultureInfo.InvariantCulture)}",
            $"jpegQuality={JpegQuality.ToString(CultureInfo.InvariantCulture)}");
    }

    private static string CreateRelativePath(ResolvedPreviewSource source, string sourceStamp)
    {
        TrickplayMetadata metadata = source.Metadata;
        FrameSelection selection = source.Selection;
        string widthDirectory = string.Create(CultureInfo.InvariantCulture, $"w{metadata.FrameWidth:D4}");
        string spriteDirectory = string.Create(
            CultureInfo.InvariantCulture,
            $"s{selection.SpriteIndex:D6}-{sourceStamp}");
        string fileName = string.Create(CultureInfo.InvariantCulture, $"f{selection.FrameIndex:D10}.jpg");
        return Path.Combine(source.MediaSourceId.ToString("N"), widthDirectory, spriteDirectory, fileName);
    }
}
