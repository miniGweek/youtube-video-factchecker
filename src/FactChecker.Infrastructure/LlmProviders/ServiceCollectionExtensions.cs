using FactChecker.Core.Interfaces;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Anthropic;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Gemini;
using FactChecker.Infrastructure.LlmProviders.Stages;
using FactChecker.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FactChecker.Infrastructure.LlmProviders;

/// <summary>
/// Registers the LLM provider (Gemini or Anthropic) and all provider-agnostic pipeline stages.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLlmProvider(
        this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetValue<string>("LlmProvider") ?? "Gemini";

        // Stage model tier configuration (shared by all providers)
        services.AddOptions<StageModelOptions>()
            .BindConfiguration("StageModelOptions");

        // Provider-agnostic stages
        services.AddTransient<IDomainDetector, DomainDetectorStage>();
        services.AddTransient<ISummariser, SummariserStage>();
        services.AddTransient<IClaimExtractor, ClaimExtractorStage>();
        services.AddTransient<IClaimVerifier, ClaimVerifierStage>();
        services.AddTransient<IAssessmentGenerator, AssessmentGeneratorStage>();

        switch (provider)
        {
            case "Gemini":
                services.AddOptions<GeminiOptions>()
                    .BindConfiguration("GeminiOptions")
                    .PostConfigure(o =>
                    {
                        var fromEnv = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                        if (!string.IsNullOrWhiteSpace(fromEnv))
                            o.ApiKey = fromEnv;
                    })
                    .Validate(
                        o => !string.IsNullOrWhiteSpace(o.ApiKey),
                        "Gemini API key is required. Set the GEMINI_API_KEY environment variable.");
                services.AddSingleton<ILlmClient, GeminiLlmClient>();
                break;

            case "Anthropic":
                services.AddOptions<AnthropicOptions>()
                    .BindConfiguration("AnthropicOptions")
                    .PostConfigure(o =>
                    {
                        var fromEnv = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
                        if (!string.IsNullOrWhiteSpace(fromEnv))
                            o.ApiKey = fromEnv;
                    })
                    .Validate(
                        o => !string.IsNullOrWhiteSpace(o.ApiKey),
                        "Anthropic API key is required. Set the ANTHROPIC_API_KEY environment variable.");
                services.AddSingleton<ILlmClient, AnthropicLlmClient>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown LLM provider '{provider}'. Supported values: Gemini, Anthropic.");
        }

        return services;
    }
}
