namespace FactChecker.Infrastructure.LlmProviders.Common;

/// <summary>
/// Provider-agnostic LLM client interface.
/// Implementations (GeminiLlmClient, AnthropicLlmClient) handle transport,
/// model-tier mapping, retry, and search mechanics.
/// Stage classes depend only on this interface — never on provider-specific types.
/// </summary>
public interface ILlmClient
{
    /// <summary>Sends a completion request and returns the text response.</summary>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a completion request with web search enabled.
    /// Returns the text response and any sources the provider retrieved.
    /// Sources may be empty if the model chose not to invoke search.
    /// </summary>
    Task<LlmSearchResponse> CompleteWithSearchAsync(LlmRequest request, CancellationToken ct = default);
}
