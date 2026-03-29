namespace FactChecker.Infrastructure.LlmProviders.Gemini;

/// <summary>
/// Configuration for the Google Gemini API provider.
/// API key must come from the GEMINI_API_KEY environment variable — never from appsettings.json.
/// </summary>
public class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string FastModel { get; set; } = "gemini-2.5-flash";
    public string StandardModel { get; set; } = "gemini-2.5-flash";
    public string PremiumModel { get; set; } = "gemini-2.5-pro";
    public bool EnableSearchGrounding { get; set; } = true;
    public int MaxRetries { get; set; } = 2;
}
