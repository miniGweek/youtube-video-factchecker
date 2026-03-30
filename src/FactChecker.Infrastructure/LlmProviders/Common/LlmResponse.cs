namespace FactChecker.Infrastructure.LlmProviders.Common;

/// <summary>Result of a standard (non-search) LLM completion call.</summary>
public record LlmResponse(string Content, TokenUsage Usage);
