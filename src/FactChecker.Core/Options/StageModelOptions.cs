using FactChecker.Core.Enums;

namespace FactChecker.Core.Options;

/// <summary>
/// Configures which model tier each pipeline stage uses.
/// Tunable via appsettings.json without code changes.
/// </summary>
public class StageModelOptions
{
    public ModelTier DomainDetection { get; set; } = ModelTier.Fast;
    public ModelTier Summarisation { get; set; } = ModelTier.Fast;
    public ModelTier ClaimExtraction { get; set; } = ModelTier.Standard;
    public ModelTier ClaimVerification { get; set; } = ModelTier.Premium;
    public ModelTier Assessment { get; set; } = ModelTier.Fast;
}
