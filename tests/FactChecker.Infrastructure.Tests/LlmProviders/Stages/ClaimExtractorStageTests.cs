using FactChecker.Core.Enums;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Stages;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Stages;

public class ClaimExtractorStageTests
{
    private const string ValidResponse = """
        {
          "claims": [
            {
              "id": "claim-01",
              "text": "30 minutes of moderate exercise 5 days a week reduces heart disease risk by 35%",
              "context": "The host cites a 2023 WHO study.",
              "importance": 5
            },
            {
              "id": "claim-02",
              "text": "Resistance training lowers LDL cholesterol by an average of 10%",
              "context": "Citing a meta-analysis of 40 studies.",
              "importance": 3
            }
          ]
        }
        """;

    [Fact]
    public async Task ExtractAsync_ValidResponse_ReturnsClaims()
    {
        var client = new StubLlmClient().WithCompleteResponse(ValidResponse);
        var extractor = new ClaimExtractorStage(client, StageTestHelper.CreateOptions());

        var claims = await extractor.ExtractAsync("Transcript...", ContentDomain.Health, maxClaims: 15);

        Assert.Equal(2, claims.Count);
    }

    [Fact]
    public async Task ExtractAsync_ValidResponse_ClaimFieldsPopulated()
    {
        var client = new StubLlmClient().WithCompleteResponse(ValidResponse);
        var extractor = new ClaimExtractorStage(client, StageTestHelper.CreateOptions());

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
        var client = new StubLlmClient().WithCompleteResponse(ValidResponse);
        var extractor = new ClaimExtractorStage(client, StageTestHelper.CreateOptions());

        var claims = await extractor.ExtractAsync("Transcript...", ContentDomain.Health, maxClaims: 1);

        Assert.Single(claims);
    }

    [Fact]
    public async Task ExtractAsync_ImportanceClamped_NeverOutOfRange()
    {
        const string responseWithOutOfRange = """
            {
              "claims": [
                { "id": "c1", "text": "A claim", "context": "Context", "importance": 10 },
                { "id": "c2", "text": "B claim", "context": "Context", "importance": -1 }
              ]
            }
            """;
        var client = new StubLlmClient().WithCompleteResponse(responseWithOutOfRange);
        var extractor = new ClaimExtractorStage(client, StageTestHelper.CreateOptions());

        var claims = await extractor.ExtractAsync("Transcript...", ContentDomain.Health, maxClaims: 15);

        Assert.All(claims, c => Assert.InRange(c.Importance, 1, 5));
    }

    [Fact]
    public async Task ExtractAsync_EmptyClaimsResponse_ReturnsEmptyList()
    {
        var client = new StubLlmClient().WithCompleteResponse("""{"claims":[]}""");
        var extractor = new ClaimExtractorStage(client, StageTestHelper.CreateOptions());

        var claims = await extractor.ExtractAsync("Short transcript.", ContentDomain.General, maxClaims: 15);

        Assert.Empty(claims);
    }

    [Fact]
    public async Task ExtractAsync_UsesCorrectModelTier()
    {
        var options = StageTestHelper.CreateOptions(new StageModelOptions { ClaimExtraction = ModelTier.Premium });
        var client = new StubLlmClient().WithCompleteResponse(ValidResponse);
        var extractor = new ClaimExtractorStage(client, options);

        await extractor.ExtractAsync("Transcript...", ContentDomain.Health, maxClaims: 15);

        Assert.NotNull(client.LastRequest);
        Assert.Equal(ModelTier.Premium, client.LastRequest.Tier);
    }

    [Fact]
    public async Task ExtractAsync_SetsCorrectStageId()
    {
        var client = new StubLlmClient().WithCompleteResponse(ValidResponse);
        var extractor = new ClaimExtractorStage(client, StageTestHelper.CreateOptions());

        await extractor.ExtractAsync("Transcript...", ContentDomain.Health, maxClaims: 15);

        Assert.NotNull(client.LastRequest);
        Assert.Equal("ClaimExtraction", client.LastRequest.StageId);
    }
}
