namespace FactChecker.Infrastructure.LlmProviders.Common;

/// <summary>
/// Result of a web-search-enabled LLM completion call.
/// Sources may be empty if the model chose not to search.
/// </summary>
public record LlmSearchResponse(
    string Content,
    IReadOnlyList<SearchResultSource> Sources,
    TokenUsage Usage);
