using FactChecker.Core.Enums;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Stages;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Stages;

public class ClaimVerifierStageTests
{
    private static Claim TestClaim => new(
        Id: "claim-01",
        Text: "30 minutes of exercise reduces heart disease risk by 35%",
        Context: "The host cites a WHO study.",
        Importance: 5);

    private static Summary TestSummary => new(
        Thesis: "Exercise reduces heart disease risk.",
        KeyPoints: ["Exercise is beneficial"],
        Domain: ContentDomain.Health);

    private const string SupportedVerdictJson = """
        {
          "verdict": "Supported",
          "confidence": "High",
          "reasoning": "Multiple peer-reviewed studies confirm this claim.",
          "sources": [
            {
              "url": "https://www.who.int/news/item/exercise-heart-study",
              "title": "WHO Exercise and Heart Health Study",
              "snippet": "Regular moderate physical activity reduces cardiovascular mortality by 35%."
            }
          ]
        }
        """;

    [Fact]
    public async Task VerifyAsync_SupportedResponse_ReturnsSupportedVerdict()
    {
        var client = new StubLlmClient()
            .WithSearchResponse(SupportedVerdictJson);
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Supported, result.Verdict);
        Assert.Equal(Confidence.High, result.Confidence);
    }

    [Fact]
    public async Task VerifyAsync_ProviderSearchSources_MappedToSources()
    {
        var providerSources = new[]
        {
            new SearchResultSource(
                new Uri("https://example.com/study"),
                "Study Title",
                "Study snippet")
        };
        var client = new StubLlmClient()
            .WithSearchResponse(SupportedVerdictJson, providerSources);
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Single(result.Sources);
        var source = result.Sources[0];
        Assert.Equal(new Uri("https://example.com/study"), source.Url);
        Assert.Equal("Study Title", source.Title);
        Assert.Equal("Study snippet", source.Snippet);
        Assert.False(source.IsAccessible);
    }

    [Fact]
    public async Task VerifyAsync_NoProviderSources_FallsBackToJsonSources()
    {
        var client = new StubLlmClient()
            .WithSearchResponse(SupportedVerdictJson);
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Single(result.Sources);
        Assert.Equal(new Uri("https://www.who.int/news/item/exercise-heart-study"), result.Sources[0].Url);
    }

    [Fact]
    public async Task VerifyAsync_RefutedResponse_ReturnsRefutedVerdict()
    {
        const string refutedJson = """
            {
              "verdict": "Refuted",
              "confidence": "High",
              "reasoning": "Evidence contradicts the claim.",
              "sources": [{ "url": "https://example.com/refutation", "title": "Refutation", "snippet": "Not true." }]
            }
            """;
        var client = new StubLlmClient().WithSearchResponse(refutedJson);
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Refuted, result.Verdict);
        Assert.NotEmpty(result.Sources);
    }

    [Fact]
    public async Task VerifyAsync_UnverifiableResponse_ReturnsUnverifiableWithNoSources()
    {
        const string unverifiableJson = """
            {
              "verdict": "Unverifiable",
              "confidence": "Low",
              "reasoning": "No reliable sources found.",
              "sources": []
            }
            """;
        var client = new StubLlmClient().WithSearchResponse(unverifiableJson);
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Unverifiable, result.Verdict);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task VerifyAsync_NotAClaimResponse_ReturnsNotAClaimVerdict()
    {
        const string notAClaimJson = """
            {
              "verdict": "NotAClaim",
              "confidence": "High",
              "reasoning": "This is an opinion, not a factual claim.",
              "sources": []
            }
            """;
        var client = new StubLlmClient().WithSearchResponse(notAClaimJson);
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.NotAClaim, result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_MalformedJson_FallsBackToUnverifiable()
    {
        var client = new StubLlmClient()
            .WithSearchResponse("this is not json at all");
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Unverifiable, result.Verdict);
        Assert.Equal(Confidence.Low, result.Confidence);
    }

    [Fact]
    public async Task VerifyAsync_ProviderException_FallsBackToUnverifiable()
    {
        var client = new StubLlmClient()
            .WithException(new HttpRequestException("API error"));
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Unverifiable, result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_ClaimIdPreserved()
    {
        var client = new StubLlmClient().WithSearchResponse(SupportedVerdictJson);
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(TestClaim.Id, result.ClaimId);
    }

    [Fact]
    public async Task VerifyAsync_UsesCompleteWithSearchAsync()
    {
        var client = new StubLlmClient().WithSearchResponse(SupportedVerdictJson);
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.NotNull(client.LastRequest);
        Assert.Equal("ClaimVerification", client.LastRequest.StageId);
    }

    [Fact]
    public async Task VerifyAsync_UsesCorrectModelTier()
    {
        var options = StageTestHelper.CreateOptions(new StageModelOptions { ClaimVerification = ModelTier.Premium });
        var client = new StubLlmClient().WithSearchResponse(SupportedVerdictJson);
        var verifier = new ClaimVerifierStage(client, options);

        await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.NotNull(client.LastRequest);
        Assert.Equal(ModelTier.Premium, client.LastRequest.Tier);
    }

    [Fact]
    public async Task VerifyAsync_ProviderSourcesTakePrecedenceOverJsonSources()
    {
        var providerSources = new[]
        {
            new SearchResultSource(
                new Uri("https://provider.com/result"),
                "Provider Result",
                "Provider snippet")
        };
        var client = new StubLlmClient()
            .WithSearchResponse(SupportedVerdictJson, providerSources);
        var verifier = new ClaimVerifierStage(client, StageTestHelper.CreateOptions());

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Single(result.Sources);
        Assert.Equal(new Uri("https://provider.com/result"), result.Sources[0].Url);
    }
}
