using TrickplayCropper.IntegrationHarness;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class CacheTreeSnapshotSpecs : IDisposable
{
    private const string Canonical = "11111111111111111111111111111111/w0320/s000001-0123456789abcdef0123456789abcdef/f0000000103.jpg";
    private readonly string root = Path.Combine(Path.GetTempPath(), "storm-cache-specs-" + Guid.NewGuid().ToString("N"));

    public CacheTreeSnapshotSpecs() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData("residue")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("bytes")]
    [InlineData("noncanonical")]
    [InlineData("link")]
    public async Task RejectsAnIncompleteOrNoncanonicalRetainedTree(string fault)
    {
        string canonical = Path.Combine(root, Canonical);
        Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
        File.WriteAllBytes(canonical, [1, 2, 3]);
        IReadOnlyDictionary<string, byte[]> expected = await CacheTreeSnapshot.ReadAsync(root, CancellationToken.None);
        Assert.True(await CacheTreeSnapshot.MatchesAsync(root, expected, CancellationToken.None));
        switch (fault)
        {
            case "residue":
                File.WriteAllBytes(canonical + ".unfinished.tmp", [1]);
                break;
            case "duplicate":
                string duplicate = Path.Combine(root, Canonical.Replace("s000001", "s000002", StringComparison.Ordinal));
                Directory.CreateDirectory(Path.GetDirectoryName(duplicate)!);
                File.Copy(canonical, duplicate);
                break;
            case "missing":
                File.Delete(canonical);
                break;
            case "bytes":
                File.WriteAllBytes(canonical, [3, 2, 1]);
                break;
            case "noncanonical":
                File.WriteAllBytes(Path.Combine(root, "unexpected.jpg"), [1]);
                break;
            case "link":
                File.Move(canonical, Path.Combine(root, "target"));
                File.CreateSymbolicLink(canonical, Path.Combine(root, "target"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }

        Assert.False(await CacheTreeSnapshot.MatchesAsync(root, expected, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsCancellationThatArrivesWhileAcceptingAMatchingSnapshot()
    {
        using CancellationTokenSource cancellation = new();
        IReadOnlyDictionary<string, byte[]> expected = new CancellingExpectations(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CacheTreeSnapshot.MatchesAsync(root, expected, cancellation.Token));
    }

    private sealed class CancellingExpectations(CancellationTokenSource cancellation)
        : System.Collections.ObjectModel.ReadOnlyDictionary<string, byte[]>(new Dictionary<string, byte[]>()),
          IReadOnlyDictionary<string, byte[]>
    {
        int IReadOnlyCollection<KeyValuePair<string, byte[]>>.Count
        {
            get
            {
                // Cancel after the actual filesystem read, precisely when its matching result is accepted.
                cancellation.Cancel();
                return 0;
            }
        }
    }

    public void Dispose() => Directory.Delete(root, true);
}
