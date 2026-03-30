using FactChecker.Core.Enums;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Stages;
using Microsoft.Extensions.Logging.Abstractions;

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

    private static ClaimVerifierStage CreateVerifier(
        StubLlmClient client,
        StageModelOptions? stageOptions = null,
        AnalysisOptions? analysisOptions = null)
    {
        return new ClaimVerifierStage(
            client,
            StageTestHelper.CreateOptions(stageOptions),
            StageTestHelper.CreateAnalysisOptions(analysisOptions),
            NullLogger<ClaimVerifierStage>.Instance);
    }

    [Fact]
    public async Task VerifyAsync_SupportedResponse_ReturnsSupportedVerdict()
    {
        var client = new StubLlmClient()
            .WithSearchResponse(SupportedVerdictJson);
        var verifier = CreateVerifier(client);

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
        var verifier = CreateVerifier(client);

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
        var verifier = CreateVerifier(client);

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
        var verifier = CreateVerifier(client);

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
        var verifier = CreateVerifier(client);

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
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.NotAClaim, result.Verdict);
    }

    // ── JSON retry behaviour ─────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_MalformedJson_RetriesBeforeFallingBack()
    {
        // Both attempts return bad JSON → Unverifiable after 2 calls (MaxVerificationRetries = 1)
        var client = new StubLlmClient()
            .WithSearchResponseSequence("this is not json at all", "still not json");
        var verifier = CreateVerifier(client, analysisOptions: new AnalysisOptions { MaxVerificationRetries = 1 });

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Unverifiable, result.Verdict);
        Assert.Equal(Confidence.Low, result.Confidence);
        Assert.Equal(2, client.Requests.Count); // initial + 1 retry
    }

    [Fact]
    public async Task VerifyAsync_MalformedJsonThenValidJson_ReturnsRetryResult()
    {
        var client = new StubLlmClient()
            .WithSearchResponseSequence("this is not json", SupportedVerdictJson);
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Supported, result.Verdict);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task VerifyAsync_MalformedJson_RetryIncludesNudgeInSystemPrompt()
    {
        var client = new StubLlmClient()
            .WithSearchResponseSequence("not json", SupportedVerdictJson);
        var verifier = CreateVerifier(client);

        await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        // First request uses the standard (non-nudged) prompt
        Assert.DoesNotContain("Parse error", client.Requests[0].SystemPrompt, StringComparison.Ordinal);
        // Retry request has the nudge with specific parse error context appended
        Assert.Contains("could not be parsed as JSON", client.Requests[1].SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Parse error:", client.Requests[1].SystemPrompt, StringComparison.Ordinal);
        Assert.True(client.Requests[1].SystemPrompt.Length > client.Requests[0].SystemPrompt.Length);
    }

    [Fact]
    public async Task VerifyAsync_MalformedJsonBothAttempts_FallsBackToUnverifiable()
    {
        var client = new StubLlmClient()
            .WithSearchResponseSequence("bad json 1", "bad json 2");
        var verifier = CreateVerifier(client, analysisOptions: new AnalysisOptions { MaxVerificationRetries = 1 });

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Unverifiable, result.Verdict);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task VerifyAsync_ProviderException_FallsBackToUnverifiable()
    {
        var client = new StubLlmClient()
            .WithException(new HttpRequestException("API error"));
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Unverifiable, result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_ClaimIdPreserved()
    {
        var client = new StubLlmClient().WithSearchResponse(SupportedVerdictJson);
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(TestClaim.Id, result.ClaimId);
    }

    [Fact]
    public async Task VerifyAsync_UsesCompleteWithSearchAsync()
    {
        var client = new StubLlmClient().WithSearchResponse(SupportedVerdictJson);
        var verifier = CreateVerifier(client);

        await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.NotNull(client.LastRequest);
        Assert.Equal("ClaimVerification", client.LastRequest.StageId);
    }

    [Fact]
    public async Task VerifyAsync_UsesCorrectModelTier()
    {
        var client = new StubLlmClient().WithSearchResponse(SupportedVerdictJson);
        var verifier = CreateVerifier(client, stageOptions: new StageModelOptions { ClaimVerification = ModelTier.Premium });

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
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Single(result.Sources);
        Assert.Equal(new Uri("https://provider.com/result"), result.Sources[0].Url);
    }

    // ── Empty content behaviour ──────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_EmptyContent_RetriesWithOriginalPrompt()
    {
        // First response is empty; retry returns valid JSON
        var client = new StubLlmClient()
            .WithSearchResponseSequence(string.Empty, SupportedVerdictJson);
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Supported, result.Verdict);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task VerifyAsync_EmptyContent_RetryUsesOriginalSystemPrompt()
    {
        // When content is empty, retry should NOT include a JSON nudge
        var client = new StubLlmClient()
            .WithSearchResponseSequence(string.Empty, SupportedVerdictJson);
        var verifier = CreateVerifier(client);

        await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        // Both requests should have the same system prompt — no JSON nudge appended
        Assert.Equal(client.Requests[0].SystemPrompt, client.Requests[1].SystemPrompt);
    }

    [Fact]
    public async Task VerifyAsync_EmptyContentBothAttempts_FallsBackToUnverifiable()
    {
        var client = new StubLlmClient()
            .WithSearchResponseSequence(string.Empty, string.Empty);
        var verifier = CreateVerifier(client, analysisOptions: new AnalysisOptions { MaxVerificationRetries = 1 });

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Unverifiable, result.Verdict);
        Assert.Equal(Confidence.Low, result.Confidence);
        Assert.Equal(2, client.Requests.Count);
    }

    // ── Configurable retry count ──────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_CustomRetryCount_RespectsConfig()
    {
        // 4 retries = 5 total attempts; provide 5 bad responses
        var client = new StubLlmClient()
            .WithSearchResponseSequence("bad 1", "bad 2", "bad 3", "bad 4", "bad 5");
        var verifier = CreateVerifier(client, analysisOptions: new AnalysisOptions { MaxVerificationRetries = 4 });

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Unverifiable, result.Verdict);
        Assert.Equal(5, client.Requests.Count);
    }

    [Fact]
    public async Task VerifyAsync_DefaultRetryCount_MakesThreeTotalAttempts()
    {
        // Default MaxVerificationRetries = 2 → 3 total attempts; succeed on attempt 3
        var client = new StubLlmClient()
            .WithSearchResponseSequence(string.Empty, string.Empty, SupportedVerdictJson);
        var verifier = CreateVerifier(client); // default AnalysisOptions

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Supported, result.Verdict);
        Assert.Equal(3, client.Requests.Count);
    }
}
