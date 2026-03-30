namespace FactChecker.Infrastructure.LlmProviders.Common;

/// <summary>
/// A single search result returned by a web-search-enabled LLM call.
/// Provider-agnostic: both Gemini grounding and Anthropic web_search tool map to this type.
/// </summary>
public record SearchResultSource(Uri Url, string Title, string Snippet);
