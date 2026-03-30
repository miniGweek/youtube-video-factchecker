using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Stages.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.LlmProviders.Stages;

public sealed partial class ClaimVerifierStage : IClaimVerifier
{
    private const string StageId = "ClaimVerification";

    private readonly ILlmClient _client;
    private readonly StageModelOptions _stageOptions;
    private readonly int _maxRetries;
    private readonly ILogger<ClaimVerifierStage> _logger;

    public ClaimVerifierStage(
        ILlmClient client,
        IOptions<StageModelOptions> stageOptions,
        IOptions<AnalysisOptions> analysisOptions,
        ILogger<ClaimVerifierStage> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(stageOptions);
        ArgumentNullException.ThrowIfNull(analysisOptions);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _stageOptions = stageOptions.Value;
        _maxRetries = analysisOptions.Value.MaxVerificationRetries;
        _logger = logger;
    }

    public async Task<FactCheck> VerifyAsync(
        Claim claim, Summary summary, ContentDomain domain, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(summary);

        var userMessage = BuildUserMessage(claim, summary, domain);
        var baseRequest = new LlmRequest(
            StageId: StageId,
            Tier: _stageOptions.ClaimVerification,
            SystemPrompt: StagePrompts.ClaimVerification,
            UserPrompt: userMessage);

        // Tracks whether the previous attempt had empty content (vs bad JSON).
        // Empty content → retry with original prompt; bad JSON → retry with nudge.
        bool lastWasEmpty = false;
        string? lastParseError = null;
        LlmRequest currentRequest = baseRequest;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            bool isFinalAttempt = attempt == _maxRetries;

            LlmSearchResponse response;
            try
            {
                response = await _client.CompleteWithSearchAsync(currentRequest, ct).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Intentional: any provider failure produces Unverifiable
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt == 0)
                    LogProviderErrorInitialCall(claim.Id, ex.Message);
                else
                    LogProviderErrorRetry(claim.Id, attempt, ex.Message);
                return UnverifiableFallback(claim.Id, "LLM provider returned an error during verification.");
            }
#pragma warning restore CA1031

            // ── Empty content ─────────────────────────────────────────────────
            if (string.IsNullOrEmpty(response.Content))
            {
                if (isFinalAttempt)
                    LogEmptyLlmResponseFinal(claim.Id, attempt + 1);
                else
                    LogEmptyLlmResponseWillRetry(claim.Id, attempt + 1);

                lastWasEmpty = true;
                currentRequest = baseRequest; // retry with original prompt
                continue;
            }

            // ── Non-empty: try JSON parse ─────────────────────────────────────
            var (result, parseError) = TryParseVerificationResponse(claim.Id, response);
            if (result is not null)
                return result;

            LogFullResponseOnParseFailure(claim.Id, response.Content);

            if (isFinalAttempt)
                LogJsonParseFailedFinal(claim.Id, attempt + 1, parseError, Truncate(response.Content));
            else
                LogJsonParseFailedWillRetry(claim.Id, attempt + 1, parseError, Truncate(response.Content));

            lastWasEmpty = false;
            lastParseError = parseError;
            currentRequest = baseRequest with
            {
                SystemPrompt = StagePrompts.ClaimVerification + BuildJsonNudge(parseError)
            };
        }

        var finalReason = lastWasEmpty
            ? $"LLM returned empty response on all {_maxRetries + 1} attempts."
            : "Verification response was not valid JSON.";
        return UnverifiableFallback(claim.Id, finalReason);
    }

    private static string BuildUserMessage(Claim claim, Summary summary, ContentDomain domain)
    {
        return $"""
            Domain: {domain}
            Video Thesis: {summary.Thesis}

            Claim to verify:
            "{claim.Text}"

            Context from video:
            "{claim.Context}"
            """;
    }

    private static string BuildJsonNudge(string? parseError) =>
        $"\n\nIMPORTANT: Your previous response could not be parsed as JSON. " +
        $"Parse error: {parseError ?? "unknown"}. " +
        "Respond ONLY with a valid JSON object matching the schema above. " +
        "Do not include any text, markdown fences, or comments outside the JSON.";

    private static (FactCheck? Result, string? ParseError) TryParseVerificationResponse(
        string claimId, LlmSearchResponse searchResponse)
    {
        var parseResult = StructuredOutputParser.TryParse<VerificationResponse>(searchResponse.Content);
        if (!parseResult.IsSuccess)
            return (null, parseResult.Error);

        var response = parseResult.Value!;

        var verdict = Enum.TryParse<Verdict>(response.Verdict, ignoreCase: true, out var v)
            ? v
            : Verdict.Unverifiable;

        var confidence = Enum.TryParse<Confidence>(response.Confidence, ignoreCase: true, out var c)
            ? c
            : Confidence.Low;

        IReadOnlyList<Source> sources;
        if (searchResponse.Sources.Count > 0)
        {
            sources = searchResponse.Sources
                .Select(s => new Source(
                    Url: s.Url,
                    Title: s.Title,
                    Snippet: s.Snippet,
                    IsAccessible: false))
                .ToList()
                .AsReadOnly();
        }
        else
        {
            sources = (response.Sources ?? [])
                .Where(s => Uri.TryCreate(s.Url, UriKind.Absolute, out _))
                .Select(s => new Source(
                    Url: new Uri(s.Url),
                    Title: s.Title,
                    Snippet: s.Snippet,
                    IsAccessible: false))
                .ToList()
                .AsReadOnly();
        }

        return (new FactCheck(claimId, verdict, confidence, response.Reasoning, sources), null);
    }

    private static FactCheck UnverifiableFallback(string claimId, string reason) =>
        new(claimId, Verdict.Unverifiable, Confidence.Low, reason, []);

    private static string Truncate(string text, int maxLength = 500) =>
        text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "…");

    // ── Source-generated LoggerMessage methods ────────────────────────────────

    [LoggerMessage(Level = LogLevel.Error,
        Message = "ClaimVerification [{ClaimId}]: LLM provider error on initial call — {ErrorMessage}")]
    private partial void LogProviderErrorInitialCall(string claimId, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "ClaimVerification [{ClaimId}]: LLM provider error on attempt {Attempt} — {ErrorMessage}")]
    private partial void LogProviderErrorRetry(string claimId, int attempt, string errorMessage);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ClaimVerification [{ClaimId}]: LLM returned empty text content on attempt {Attempt} — retrying with original prompt")]
    private partial void LogEmptyLlmResponseWillRetry(string claimId, int attempt);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "ClaimVerification [{ClaimId}]: LLM returned empty text content on all {Attempt} attempts — returning Unverifiable")]
    private partial void LogEmptyLlmResponseFinal(string claimId, int attempt);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "ClaimVerification [{ClaimId}]: Full LLM response for failed parse: {FullResponse}")]
    private partial void LogFullResponseOnParseFailure(string claimId, string fullResponse);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ClaimVerification [{ClaimId}]: JSON parse failed on attempt {Attempt} — {ParseError}. Retrying with nudge. Response (truncated): {ResponseSnippet}")]
    private partial void LogJsonParseFailedWillRetry(string claimId, int attempt, string? parseError, string responseSnippet);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "ClaimVerification [{ClaimId}]: JSON parse failed on all {Attempt} attempts — {ParseError}. Returning Unverifiable. Response (truncated): {ResponseSnippet}")]
    private partial void LogJsonParseFailedFinal(string claimId, int attempt, string? parseError, string responseSnippet);
}

#pragma warning disable CA1812
file sealed record SourceDto(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("snippet")] string Snippet);

file sealed record VerificationResponse(
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("reasoning")] string Reasoning,
    [property: JsonPropertyName("sources")] List<SourceDto>? Sources);
#pragma warning restore CA1812
