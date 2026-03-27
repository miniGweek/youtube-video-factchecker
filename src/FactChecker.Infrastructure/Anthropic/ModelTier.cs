namespace FactChecker.Infrastructure.Anthropic;

/// <summary>
/// Selects which Anthropic model to use for a given pipeline stage.
/// Fast maps to Haiku (cheap); Standard maps to Sonnet (capable).
/// </summary>
public enum ModelTier
{
    Fast,
    Standard
}
