namespace FactChecker.Core.Enums;

/// <summary>
/// Selects the cost/capability tier for an LLM call.
/// Provider implementations map each tier to a concrete model string.
/// </summary>
public enum ModelTier
{
    /// <summary>Fast, cheap model — suitable for simple classification tasks.</summary>
    Fast,

    /// <summary>Mid-tier model — suitable for extraction and summarisation.</summary>
    Standard,

    /// <summary>High-capability model — suitable for fact verification with search.</summary>
    Premium
}
