namespace FactChecker.Core.Options;

public class AnalysisOptions
{
    public int MaxVideoDurationMinutes { get; set; } = 45;
    public int MaxClaimsToVerify { get; set; } = 15;
    public int MaxConcurrentVerifications { get; set; } = 4;
    public int SourceValidationTimeoutSeconds { get; set; } = 5;
    public int PipelineTimeoutSeconds { get; set; } = 120;
    public int MaxVerificationRetries { get; set; } = 2;
    public int CompletedAnalysisRetentionMinutes { get; set; } = 60;
}
