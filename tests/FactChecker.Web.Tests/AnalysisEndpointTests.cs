using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FactChecker.Core.Enums;
using FactChecker.Core.Events;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Core.Pipeline;
using FactChecker.Core.Scoring;
using FactChecker.Infrastructure.Events;
using FactChecker.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FactChecker.Web.Tests;

public class AnalysisEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AnalysisEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ── POST /api/analyse ─────────────────────────────────────────────────────

    [Fact]
    public async Task PostAnalyse_ValidYouTubeUrl_Returns202WithAnalysisId()
    {
        var client = CreateClientWithFakePipeline();

        var response = await client.PostAsJsonAsync("/api/analyse", new { url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("analysisId", out var idProp));
        Assert.False(string.IsNullOrWhiteSpace(idProp.GetString()));
    }

    [Fact]
    public async Task PostAnalyse_MissingUrl_Returns400()
    {
        var client = CreateClientWithFakePipeline();

        var response = await client.PostAsJsonAsync("/api/analyse", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalyse_EmptyUrl_Returns400()
    {
        var client = CreateClientWithFakePipeline();

        var response = await client.PostAsJsonAsync("/api/analyse", new { url = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalyse_NonYouTubeUrl_Returns400()
    {
        var client = CreateClientWithFakePipeline();

        var response = await client.PostAsJsonAsync("/api/analyse", new { url = "https://www.example.com/video" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalyse_InvalidUrlFormat_Returns400()
    {
        var client = CreateClientWithFakePipeline();

        var response = await client.PostAsJsonAsync("/api/analyse", new { url = "not-a-url" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── GET /api/analyse/{id} ─────────────────────────────────────────────────

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var client = CreateClientWithFakePipeline();

        var response = await client.GetAsync("/api/analyse/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_RunningAnalysis_Returns202()
    {
        var store = new InMemoryAnalysisStore();
        var result = new AnalysisResult("test-id");
        store.Add(result);
        var client = CreateClientWithStore(store);

        var response = await client.GetAsync("/api/analyse/test-id");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task GetById_CompleteAnalysis_Returns200()
    {
        var store = new InMemoryAnalysisStore();
        var result = BuildCompleteResult("done-id");
        store.Add(result);
        var client = CreateClientWithStore(store);

        var response = await client.GetAsync("/api/analyse/done-id");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_FailedAnalysis_Returns200()
    {
        var store = new InMemoryAnalysisStore();
        var result = new AnalysisResult("fail-id");
        result.Fail(AnalysisStage.Summarisation, "Something went wrong.");
        store.Add(result);
        var client = CreateClientWithStore(store);

        var response = await client.GetAsync("/api/analyse/fail-id");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── GET /api/analyse/{id}/stream ──────────────────────────────────────────

    [Fact]
    public async Task GetStream_CompletedAnalysis_ReturnsEventStream()
    {
        var transport = new ChannelEventTransport();
        var video = new VideoInfo(
            new Uri("https://youtube.com/watch?v=abc"),
            VideoId: "abc",
            Title: "T",
            Channel: "C",
            Duration: TimeSpan.FromMinutes(5),
            ThumbnailUrl: null);

        await transport.PublishAsync(new AnalysisStartedEvent("stream-test", DateTimeOffset.UtcNow, video));
        transport.Complete("stream-test");

        var client = CreateClientWithTransport(transport);

        var response = await client.GetAsync("/api/analyse/stream-test/stream");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: AnalysisStarted", body);
        Assert.Contains("data:", body);
    }

    [Fact]
    public async Task GetStream_UnknownId_CompletesWithNoEvents()
    {
        var transport = new ChannelEventTransport();
        transport.Complete("ghost-id"); // immediately complete empty channel

        var client = CreateClientWithTransport(transport);

        var response = await client.GetAsync("/api/analyse/ghost-id/stream");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Empty(body);
    }

    // ── SSE event format ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetStream_EventFormat_HasCorrectSseStructure()
    {
        var transport = new ChannelEventTransport();
        var score = new ScoreBreakdown(90, 80, 75, 87, "v1.0");
        await transport.PublishAsync(new ScoringCompleteEvent("fmt-test", DateTimeOffset.UtcNow, score));
        transport.Complete("fmt-test");

        var client = CreateClientWithTransport(transport);

        var response = await client.GetAsync("/api/analyse/fmt-test/stream");
        var body = await response.Content.ReadAsStringAsync();

        // SSE format: "event: ...\ndata: ...\n\n"
        Assert.Contains("event: ScoringComplete\n", body);
        Assert.Contains("data: {", body);
        Assert.Contains("\n\n", body);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient CreateClientWithFakePipeline()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AnalysisPipeline>();
                services.AddTransient<AnalysisPipeline>(sp => BuildNoOpPipeline(sp));
            });
        }).CreateClient();
    }

    private HttpClient CreateClientWithStore(IAnalysisStore store)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAnalysisStore>();
                services.AddSingleton(store);
                services.RemoveAll<AnalysisPipeline>();
                services.AddTransient<AnalysisPipeline>(sp => BuildNoOpPipeline(sp));
            });
        }).CreateClient();
    }

    private HttpClient CreateClientWithTransport(ChannelEventTransport transport)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ChannelEventTransport>();
                services.RemoveAll<IAnalysisEventSink>();
                services.RemoveAll<IAnalysisEventSource>();
                services.RemoveAll<IAnalysisEventCompleter>();
                services.AddSingleton(transport);
                services.AddSingleton<IAnalysisEventSink>(transport);
                services.AddSingleton<IAnalysisEventSource>(transport);
                services.AddSingleton<IAnalysisEventCompleter>(transport);
                services.RemoveAll<AnalysisPipeline>();
                services.AddTransient<AnalysisPipeline>(sp => BuildNoOpPipeline(sp));
            });
        }).CreateClient();
    }

    private static AnalysisPipeline BuildNoOpPipeline(IServiceProvider sp) =>
        new(
            new FakeMetadataProvider(),
            new FakeTranscriptExtractor(),
            new FakeDomainDetector(),
            new FakeSummariser(),
            new FakeClaimExtractor(),
            new FakeClaimVerifier(),
            new FakeSourceValidator(),
            new DefaultScoringEngine(),
            new FakeAssessmentGenerator(),
            sp.GetRequiredService<IAnalysisEventSink>(),
            sp.GetRequiredService<IAnalysisEventCompleter>(),
            sp.GetRequiredService<IAnalysisStore>(),
            sp.GetRequiredService<AnalysisOptions>());

    private static AnalysisResult BuildCompleteResult(string id)
    {
        var result = new AnalysisResult(id);
        result.SetVideo(new VideoInfo(
            new Uri("https://youtube.com/watch?v=x"), "x", "T", "C",
            TimeSpan.FromMinutes(5), null));
        result.SetTranscript(new Transcript("text", TranscriptQuality.Manual, 1));
        result.SetSummary(new Summary("Thesis", ["Point 1"], ContentDomain.General));
        result.SetClaims([]);
        result.SetScore(new ScoreBreakdown(90, 80, 75, 87, "v1.0"));
        result.Complete();
        return result;
    }

    // ── Minimal fakes ─────────────────────────────────────────────────────────

    private sealed class FakeMetadataProvider : IVideoMetadataProvider
    {
        public Task<VideoInfo> GetMetadataAsync(Uri videoUri, CancellationToken ct = default) =>
            Task.FromResult(new VideoInfo(videoUri, "abc", "T", "C", TimeSpan.FromMinutes(5), null));
    }

    private sealed class FakeTranscriptExtractor : ITranscriptExtractor
    {
        public Task<Transcript> ExtractAsync(string videoId, CancellationToken ct = default) =>
            Task.FromResult(new Transcript("text", TranscriptQuality.Manual, 1));
    }

    private sealed class FakeDomainDetector : IDomainDetector
    {
        public Task<ContentDomain> DetectAsync(string snippet, CancellationToken ct = default) =>
            Task.FromResult(ContentDomain.General);
    }

    private sealed class FakeSummariser : ISummariser
    {
        public Task<Summary> SummariseAsync(string transcript, ContentDomain domain, CancellationToken ct = default) =>
            Task.FromResult(new Summary("Thesis", ["P1"], domain));
    }

    private sealed class FakeClaimExtractor : IClaimExtractor
    {
        public Task<IReadOnlyList<Claim>> ExtractAsync(
            string transcript, ContentDomain domain, int max, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Claim>>([]);
    }

    private sealed class FakeClaimVerifier : IClaimVerifier
    {
        public Task<FactCheck> VerifyAsync(Claim claim, Summary summary, ContentDomain domain, CancellationToken ct = default) =>
            Task.FromResult(new FactCheck(claim.Id, Verdict.Supported, Confidence.High, "OK", []));
    }

    private sealed class FakeSourceValidator : ISourceValidator
    {
        public Task<Source> ValidateAsync(Source source, CancellationToken ct = default) =>
            Task.FromResult(source);
    }

    private sealed class FakeAssessmentGenerator : IAssessmentGenerator
    {
        public Task<Assessment> GenerateAsync(
            Summary summary, IReadOnlyList<FactCheck> factChecks,
            ScoreBreakdown score, CancellationToken ct = default) =>
            Task.FromResult(new Assessment(WatchRecommendation.Watch, "Good.", "High", []));
    }
}
