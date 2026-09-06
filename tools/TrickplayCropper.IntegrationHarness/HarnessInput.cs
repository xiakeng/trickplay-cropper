using System.Text.Json;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Holds operator-supplied credentials and three distinct subjects.</summary>
public sealed class HarnessInput
{
    private static readonly string[] fields = ["adminToken", "invisibleItemId", "playableItemIds"];

    private HarnessInput(string token, Guid[] playableItems, Guid invisibleItem, string userlessApiKey)
    {
        Token = token;
        UserlessApiKey = userlessApiKey;
        PlayableItems = Array.AsReadOnly(playableItems);
        InvisibleItem = invisibleItem;
    }

    /// <summary>Gets the credential for HTTP headers; never include it in diagnostics.</summary>
    public string Token { get; }

    /// <summary>Gets an optional operator-supplied userless API key, used only for authentication checks.</summary>
    public string UserlessApiKey { get; }

    /// <summary>Gets exactly two distinct playable logical videos.</summary>
    public IReadOnlyList<Guid> PlayableItems { get; }

    /// <summary>Gets the existing video concealed from the credential's user.</summary>
    public Guid InvisibleItem { get; }

    /// <summary>Rejects malformed, duplicate, or unexpected fields without echoing any input.</summary>
    public static HarnessInput Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string[] names = root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
            if (!names.SequenceEqual(fields) && !names.SequenceEqual(fields.Append("userlessApiKey")))
            {
                throw new InvalidDataException("Use the three fields in harness.example.json and optionally userlessApiKey.");
            }

            string token = root.GetProperty("adminToken").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token) || token.Any(character => !char.IsAsciiLetterOrDigit(character)))
            {
                throw new InvalidDataException("Supply a Jellyfin administrator user access token.");
            }

            string apiKey = root.TryGetProperty("userlessApiKey", out JsonElement key) ? key.GetString() ?? string.Empty : string.Empty;
            if (root.TryGetProperty("userlessApiKey", out _) && (string.IsNullOrWhiteSpace(apiKey)
                || apiKey.Any(character => !char.IsAsciiLetterOrDigit(character))))
            {
                throw new InvalidDataException("Supply a valid userless API key or omit the optional field.");
            }

            Guid[] playable = root.GetProperty("playableItemIds").EnumerateArray().Select(value => Guid.Parse(value.GetString()!)).ToArray();
            Guid invisible = Guid.Parse(root.GetProperty("invisibleItemId").GetString()!);
            if (playable.Length != 2 || playable.Append(invisible).Contains(Guid.Empty)
                || playable.Append(invisible).Distinct().Count() != 3)
            {
                throw new InvalidDataException("Supply two playable Items and one distinct existing invisible Item.");
            }

            return new HarnessInput(token, playable, invisible, apiKey);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            // Parser exceptions can contain credential-bearing property names and values.
            throw new InvalidDataException("Invalid harness.json; follow harness.example.json exactly.");
        }
    }
}
