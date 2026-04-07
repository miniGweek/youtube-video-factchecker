using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FactChecker.Core.Enums;
using FactChecker.Core.Events;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Core.Pipeline;
using Microsoft.Extensions.Options;
using FactChecker.Infrastructure.Events;
using FactChecker.Infrastructure.Storage;
using FactChecker.Web.Services;
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
        var store = new InMemoryAnalysisStore(Options.Create(new AnalysisOptions()));
        var result = new AnalysisResult("test-id");
        store.Add(result);
        var client = CreateClientWithStore(store);

        var response = await client.GetAsync("/api/analyse/test-id");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task GetById_CompleteAnalysis_Returns200()
    {
        var store = new InMemoryAnalysisStore(Options.Create(new AnalysisOptions()));
        var result = BuildCompleteResult("done-id");
        store.Add(result);
        var client = CreateClientWithStore(store);

        var response = await client.GetAsync("/api/analyse/done-id");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_FailedAnalysis_Returns200()
    {
        var store = new InMemoryAnalysisStore(Options.Create(new AnalysisOptions()));
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
                services.RemoveAll<IAnalysisDispatcher>();
                services.AddSingleton<IAnalysisDispatcher>(new NoOpDispatcher());
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
                services.RemoveAll<IAnalysisDispatcher>();
                services.AddSingleton<IAnalysisDispatcher>(new NoOpDispatcher());
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
                services.RemoveAll<IAnalysisDispatcher>();
                services.AddSingleton<IAnalysisDispatcher>(new NoOpDispatcher());
            });
        }).CreateClient();
    }

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

    private sealed class NoOpDispatcher : IAnalysisDispatcher
    {
        public ValueTask EnqueueAsync(string analysisId, Uri videoUri, CancellationToken ct = default) =>
            ValueTask.CompletedTask;
    }
}
