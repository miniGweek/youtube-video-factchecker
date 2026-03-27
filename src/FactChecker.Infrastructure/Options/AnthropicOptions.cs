namespace FactChecker.Infrastructure.Options;

public class AnthropicOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string FastModel { get; set; } = "claude-haiku-4-5-20251001";
    public string StandardModel { get; set; } = "claude-sonnet-4-20250514";
    public int MaxRetries { get; set; } = 2;
}
