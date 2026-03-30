namespace FactChecker.Infrastructure.LlmProviders.Common;

/// <summary>Token consumption for a single LLM API call.</summary>
public record TokenUsage(int InputTokens, int OutputTokens);
