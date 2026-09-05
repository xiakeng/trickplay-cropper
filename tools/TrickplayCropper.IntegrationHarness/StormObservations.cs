namespace TrickplayCropper.IntegrationHarness;

/// <summary>Reconciles completed GET representations, canonical files, and aggregate stable events.</summary>
internal sealed class StormObservations(IReadOnlyDictionary<string, byte[]> baseline)
{
    private static readonly string[] dispositions = ["MISS", "HIT"];
    private readonly Dictionary<(Guid Item, int Frame), Representation> representations = [];
    private readonly List<Response> responses = [];
    private readonly Dictionary<string, byte[]> files = new(baseline, StringComparer.Ordinal);

    /// <summary>Gets the number of distinct requested Preview identities.</summary>
    public int IdentityCount => representations.Count;

    /// <summary>Gets all canonical files retained by the four smoke cases.</summary>
    public IReadOnlyDictionary<string, byte[]> Files => files;

    /// <summary>Records one completed wave, independently of concurrent response completion order.</summary>
    public void Record(IReadOnlyList<Response> wave)
    {
        foreach (Response response in wave)
        {
            var identity = (response.Request.Item, response.Request.FrameIndex);
            if (representations.TryGetValue(identity, out Representation? previous))
            {
                if (previous.Tag != response.Tag || !previous.Bytes.AsSpan().SequenceEqual(response.Bytes))
                {
                    throw new InvalidDataException("Repeated GET bytes or ETag changed for one Preview identity.");
                }
            }
            else
            {
                representations.Add(identity, new Representation(response.Tag, response.Bytes));
            }

            files[CanonicalPath(response)] = response.Bytes;
        }

        responses.AddRange(wave);
    }

    /// <summary>Requires a HIT in a later request wave than a MISS for the same Preview identity.</summary>
    public void VerifyTransition()
    {
        bool transition = responses.Where(response => response.Disposition == "MISS").Any(miss => responses.Any(hit =>
            hit.Disposition == "HIT" && hit.Wave > miss.Wave && hit.Request.Item == miss.Request.Item
            && hit.Request.FrameIndex == miss.Request.FrameIndex));
        if (!transition)
        {
            throw new InvalidDataException("Scrub Storm did not produce a MISS-to-HIT transition.");
        }
    }

    /// <summary>Compares event multiplicity because the stable protocol deliberately has no request identifiers.</summary>
    public bool Matches(IReadOnlyList<DebugEventReader.ProtocolEvent> events)
    {
        var expectedFrames = responses.GroupBy(response => (response.Request.FrameIndex, response.Request.SpriteIndex))
            .ToDictionary(group => group.Key, group => group.Count());
        var actualFrames = events.Where(value => value.EventId == 1002)
            .GroupBy(value => (value.FrameIndex!.Value, value.SpriteIndex!.Value))
            .ToDictionary(group => group.Key, group => group.Count());
        return expectedFrames.Count == actualFrames.Count
            && expectedFrames.All(pair => actualFrames.TryGetValue(pair.Key, out int count) && count == pair.Value)
            && dispositions.All(disposition => responses.Count(response => response.Disposition == disposition)
                == events.Count(value => value.EventId == 1003 && value.Disposition == disposition));
    }

    private static string CanonicalPath(Response response)
    {
        PreviewRequest preview = response.Request;
        string stamp = response.Tag.Substring(1, 32);
        return FormattableString.Invariant(
            $"{preview.Item:N}/w{preview.Metadata.Width:D4}/s{preview.SpriteIndex:D6}-{stamp}/f{preview.FrameIndex:D10}.jpg");
    }

    /// <summary>A verified GET with its request-wave ordinal for causal MISS-to-HIT checks.</summary>
    internal sealed record Response(PreviewRequest Request, string Tag, byte[] Bytes, string Disposition, int Wave);

    private sealed record Representation(string Tag, byte[] Bytes);
}
