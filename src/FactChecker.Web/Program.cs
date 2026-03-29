using FactChecker.Core.Interfaces;
using FactChecker.Core.Options;
using FactChecker.Core.Pipeline;
using FactChecker.Core.Scoring;
using FactChecker.Infrastructure.Anthropic;
using FactChecker.Infrastructure.Events;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Stages;
using FactChecker.Infrastructure.Options;
using FactChecker.Infrastructure.Storage;
using FactChecker.Infrastructure.Validation;
using FactChecker.Infrastructure.YouTube;
using FactChecker.Web.Endpoints;
using FactChecker.Web.Services;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// ── Configuration ────────────────────────────────────────────────────────────

builder.Services.AddOptions<AnalysisOptions>()
    .BindConfiguration("AnalysisOptions");

builder.Services.AddOptions<AnthropicOptions>()
    .BindConfiguration("AnthropicOptions")
    .PostConfigure(o =>
    {
        // API key from environment variable takes precedence over appsettings
        var fromEnv = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            o.ApiKey = fromEnv;
    });

// Expose AnalysisOptions as a plain class (AnalysisPipeline doesn't use IOptions<T>)
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IOptions<AnalysisOptions>>().Value);

// ── HTTP Clients ─────────────────────────────────────────────────────────────

builder.Services.AddHttpClient();

// ── Infrastructure — YouTube ──────────────────────────────────────────────────

builder.Services.AddTransient<IVideoMetadataProvider, YouTubeMetadataProvider>();
builder.Services.AddTransient<ITranscriptExtractor, YouTubeTranscriptExtractor>();

builder.Services.AddOptions<StageModelOptions>()
    .BindConfiguration("StageModelOptions");

// ── Infrastructure — LLM Provider (Anthropic via temporary adapter) ──────────

builder.Services.AddTransient<AnthropicClientWrapper>();
#pragma warning disable CS0618 // AnthropicLlmClientAdapter is intentionally used here as a temporary bridge
builder.Services.AddTransient<ILlmClient, AnthropicLlmClientAdapter>();
#pragma warning restore CS0618

// ── Infrastructure — LLM Stages (provider-agnostic) ──────────────────────────

builder.Services.AddTransient<IDomainDetector, DomainDetectorStage>();
builder.Services.AddTransient<ISummariser, SummariserStage>();
builder.Services.AddTransient<IClaimExtractor, ClaimExtractorStage>();
builder.Services.AddTransient<IClaimVerifier, ClaimVerifierStage>();
builder.Services.AddTransient<IAssessmentGenerator, AssessmentGeneratorStage>();

// ── Infrastructure — Validation ───────────────────────────────────────────────

builder.Services.AddTransient<ISourceValidator, HttpSourceValidator>();

// ── Core — Scoring ────────────────────────────────────────────────────────────

builder.Services.AddTransient<IScoringEngine, DefaultScoringEngine>();

// ── Infrastructure — Event transport (singleton — one instance, multiple interfaces) ──

builder.Services.AddSingleton<ChannelEventTransport>();
builder.Services.AddSingleton<IAnalysisEventSink>(sp =>
    sp.GetRequiredService<ChannelEventTransport>());
builder.Services.AddSingleton<IAnalysisEventSource>(sp =>
    sp.GetRequiredService<ChannelEventTransport>());
builder.Services.AddSingleton<IAnalysisEventCompleter>(sp =>
    sp.GetRequiredService<ChannelEventTransport>());

// ── Infrastructure — Analysis store (singleton — in-memory across requests) ──

builder.Services.AddSingleton<IAnalysisStore, InMemoryAnalysisStore>();

// ── Core — Pipeline ───────────────────────────────────────────────────────────

builder.Services.AddTransient<AnalysisPipeline>();

// ── ASP.NET Core ──────────────────────────────────────────────────────────────

builder.Services.AddRazorPages();
builder.Services.AddTransient<ViewRenderer>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5000", "https://localhost:5001")
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors();
app.UseRouting();
app.MapRazorPages();
app.MapAnalysisEndpoints();

app.Run();

// Expose Program for WebApplicationFactory in tests
// CA1515 suppressed: must be public (or internal + InternalsVisibleTo) for WebApplicationFactory
#pragma warning disable CA1515
public partial class Program;
#pragma warning restore CA1515
