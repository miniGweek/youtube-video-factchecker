using FactChecker.Core.Enums;
using FactChecker.Core.Models;
using FactChecker.Infrastructure.Anthropic.Stages;

namespace FactChecker.Infrastructure.Tests.Anthropic.Stages;

public class AnthropicClaimVerifierTests : StageTestBase
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

    [Fact]
    public async Task VerifyAsync_SupportedResponse_ReturnsSupportedVerdict()
    {
        var wrapper = CreateWrapper(LoadFixture("verification_response_supported.json"));
        var verifier = new AnthropicClaimVerifier(wrapper);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Supported, result.Verdict);
        Assert.Equal(Confidence.High, result.Confidence);
        Assert.Single(result.Sources);
    }

    [Fact]
    public async Task VerifyAsync_SupportedResponse_SourceUrlParsedCorrectly()
    {
        var wrapper = CreateWrapper(LoadFixture("verification_response_supported.json"));
        var verifier = new AnthropicClaimVerifier(wrapper);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        var source = result.Sources[0];
        Assert.NotNull(source.Url);
        Assert.False(string.IsNullOrEmpty(source.Title));
        Assert.False(string.IsNullOrEmpty(source.Snippet));
        // IsAccessible starts false — set later by HttpSourceValidator
        Assert.False(source.IsAccessible);
    }

    [Fact]
    public async Task VerifyAsync_RefutedResponse_ReturnsRefutedVerdict()
    {
        var wrapper = CreateWrapper(LoadFixture("verification_response_refuted.json"));
        var verifier = new AnthropicClaimVerifier(wrapper);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Refuted, result.Verdict);
        Assert.NotEmpty(result.Sources);
    }

    [Fact]
    public async Task VerifyAsync_UnverifiableResponse_ReturnsUnverifiableWithNoSources()
    {
        var wrapper = CreateWrapper(LoadFixture("verification_response_unverifiable.json"));
        var verifier = new AnthropicClaimVerifier(wrapper);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Unverifiable, result.Verdict);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task VerifyAsync_NotAClaimResponse_ReturnsNotAClaimVerdict()
    {
        var wrapper = CreateWrapper(LoadFixture("verification_response_not_a_claim.json"));
        var opinionClaim = TestClaim with { Text = "Exercise is obviously the best medicine" };
        var verifier = new AnthropicClaimVerifier(wrapper);

        var result = await verifier.VerifyAsync(opinionClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.NotAClaim, result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_MalformedResponse_FallsBackToUnverifiable()
    {
        const string badFixture = """
            {
              "id": "msg_bad",
              "type": "message",
              "role": "assistant",
              "content": [{ "type": "text", "text": "this is not json at all" }],
              "model": "claude-sonnet-4-20250514",
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 100, "output_tokens": 10 }
            }
            """;
        var wrapper = CreateWrapper(badFixture);
        var verifier = new AnthropicClaimVerifier(wrapper);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(Verdict.Unverifiable, result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_ClaimIdPreserved()
    {
        var wrapper = CreateWrapper(LoadFixture("verification_response_supported.json"));
        var verifier = new AnthropicClaimVerifier(wrapper);

        var result = await verifier.VerifyAsync(TestClaim, TestSummary, ContentDomain.Health);

        Assert.Equal(TestClaim.Id, result.ClaimId);
    }
}
