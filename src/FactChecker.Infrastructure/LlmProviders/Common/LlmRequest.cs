using FactChecker.Core.Enums;

namespace FactChecker.Infrastructure.LlmProviders.Common;

/// <summary>
/// A provider-agnostic LLM completion request.
/// </summary>
/// <param name="StageId">Identifies the pipeline stage — used in logging.</param>
/// <param name="Tier">Model tier to use for this call.</param>
/// <param name="SystemPrompt">Instructions that define the model's role and output format.</param>
/// <param name="UserPrompt">The content the model should process (transcript, claim text, etc.).</param>
public record LlmRequest(
    string StageId,
    ModelTier Tier,
    string SystemPrompt,
    string UserPrompt);
