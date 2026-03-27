using FactChecker.Core.Enums;
using FactChecker.Infrastructure.Anthropic.Stages;

namespace FactChecker.Infrastructure.Tests.Anthropic.Stages;

public class AnthropicClaimExtractorTests : StageTestBase
{
    [Fact]
    public async Task ExtractAsync_ValidResponse_ReturnsClaims()
    {
        var wrapper = CreateWrapper(LoadFixture("claims_response.json"));
        var extractor = new AnthropicClaimExtractor(wrapper);

        var claims = await extractor.ExtractAsync("Transcript...", ContentDomain.Health, maxClaims: 15);

        Assert.Equal(2, claims.Count);
    }

    [Fact]
    public async Task ExtractAsync_ValidResponse_ClaimFieldsPopulated()
    {
        var wrapper = CreateWrapper(LoadFixture("claims_response.json"));
        var extractor = new AnthropicClaimExtractor(wrapper);

        var claims = await extractor.ExtractAsync("Transcript...", ContentDomain.Health, maxClaims: 15);
        var first = claims[0];

        Assert.Equal("claim-01", first.Id);
        Assert.False(string.IsNullOrEmpty(first.Text));
        Assert.False(string.IsNullOrEmpty(first.Context));
        Assert.InRange(first.Importance, 1, 5);
    }

    [Fact]
    public async Task ExtractAsync_MaxClaimsRespected_TruncatesExcess()
    {
        var wrapper = CreateWrapper(LoadFixture("claims_response.json"));
        var extractor = new AnthropicClaimExtractor(wrapper);

        // Fixture has 2 claims; asking for max 1 should truncate
        var claims = await extractor.ExtractAsync("Transcript...", ContentDomain.Health, maxClaims: 1);

        Assert.Single(claims);
    }

    [Fact]
    public async Task ExtractAsync_ImportanceClamped_NeverOutOfRange()
    {
        var wrapper = CreateWrapper(LoadFixture("claims_response.json"));
        var extractor = new AnthropicClaimExtractor(wrapper);

        var claims = await extractor.ExtractAsync("Transcript...", ContentDomain.Health, maxClaims: 15);

        Assert.All(claims, c => Assert.InRange(c.Importance, 1, 5));
    }

    [Fact]
    public async Task ExtractAsync_EmptyClaimsResponse_ReturnsEmptyList()
    {
        var wrapper = CreateWrapper(LoadFixture("claims_response_empty.json"));
        var extractor = new AnthropicClaimExtractor(wrapper);

        var claims = await extractor.ExtractAsync("Short transcript with no claims.", ContentDomain.General, maxClaims: 15);

        Assert.Empty(claims);
    }
}
