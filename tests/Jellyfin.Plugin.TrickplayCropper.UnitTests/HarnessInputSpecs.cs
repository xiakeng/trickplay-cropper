using TrickplayCropper.IntegrationHarness;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class HarnessInputSpecs
{
    [Fact]
    public void AcceptsJellyfinsCompactItemIds()
    {
        HarnessInput input = HarnessInput.Parse("""
            {"adminToken":"abc123","playableItemIds":["11111111111111111111111111111111","22222222222222222222222222222222"],
             "invisibleItemId":"33333333333333333333333333333333"}
            """);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), input.InvisibleItem);
    }

    [Fact]
    public void RejectsDuplicateSubjectsWithoutEchoingInput()
    {
        const string Input = """
            {"adminToken":"secrettoken","playableItemIds":[
            "11111111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111"],
            "invisibleItemId":"33333333-3333-3333-3333-333333333333"}
            """;
        InvalidDataException failure = Assert.Throws<InvalidDataException>(() => HarnessInput.Parse(Input));
        Assert.DoesNotContain("secrettoken", failure.ToString(), StringComparison.Ordinal);
    }
}
