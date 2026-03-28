using FactChecker.Core.Enums;
using FactChecker.Core.Events;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Core.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;

namespace FactChecker.Core.Tests.Pipeline;

public class AnalysisPipelineTests
{
    // ── Factories ──────────────────────────────────────────────────────────────

    private static VideoInfo MakeVideo() => new(
        new Uri("https://youtube.com/watch?v=abc123"),
        VideoId: "abc123",
        Title: "Test Video",
        Channel: "Test Channel",
        Duration: TimeSpan.FromMinutes(10),
        ThumbnailUrl: null);

    private static Transcript MakeTranscript(string text = "Some transcript text.") =>
        new(text, TranscriptQuality.Manual, WordCount: 3);

    private static Summary MakeSummary() => new(
        Thesis: "The thesis.",
        KeyPoints: ["Point 1", "Point 2"],
        Domain: ContentDomain.General);

    private static Claim MakeClaim(string id = "c1", int importance = 3) => new(
        Id: id, Text: "A factual claim.", Context: "Some context.", Importance: importance);

    private static FactCheck MakeFactCheck(string claimId = "c1") => new(
        claimId, Verdict.Supported, Confidence.High, "Reasoning.", []);

    private static ScoreBreakdown MakeScore() => new(
        AccuracyScore: 90, SourceQualityScore: 80, VerifiabilityScore: 75,
        AggregateScore: 87, ScoreMethod: "v1.0");

    private static Assessment MakeAssessment() => new(
        Recommendation: WatchRecommendation.Watch,
        Reasoning: "Good video.",
        InformationDensity: "High",
        Caveats: []);

    private static AnalysisOptions DefaultOptions() => new()
    {
        MaxClaimsToVerify = 10,
        MaxConcurrentVerifications = 2,
        PipelineTimeoutSeconds = 30,
        SourceValidationTimeoutSeconds = 5
    };

    private static AnalysisPipeline CreatePipeline(
        IVideoMetadataProvider? metadata = null,
        ITranscriptExtractor? transcript = null,
        IDomainDetector? domain = null,
        ISummariser? summariser = null,
        IClaimExtractor? claims = null,
        IClaimVerifier? verifier = null,
        ISourceValidator? sources = null,
        IScoringEngine? scoring = null,
        IAssessmentGenerator? assessment = null,
        IAnalysisEventSink? sink = null,
        IAnalysisEventCompleter? completer = null,
        IAnalysisStore? store = null,
        AnalysisOptions? options = null)
    {
        return new AnalysisPipeline(
            metadata ?? new FakeMetadataProvider(MakeVideo()),
            transcript ?? new FakeTranscriptExtractor(MakeTranscript()),
            domain ?? new FakeDomainDetector(ContentDomain.General),
            summariser ?? new FakeSummariser(MakeSummary()),
            claims ?? new FakeClaimExtractor([MakeClaim()]),
            verifier ?? new FakeClaimVerifier(MakeFactCheck()),
            sources ?? new FakeSourceValidator(),
            scoring ?? new FakeScoringEngine(MakeScore()),
            assessment ?? new FakeAssessmentGenerator(MakeAssessment()),
            sink ?? new FakeEventSink(),
            completer ?? new FakeEventCompleter(),
            store ?? new FakeAnalysisStore(),
            options ?? DefaultOptions(),
            NullLogger<AnalysisPipeline>.Instance);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_HappyPath_PublishesAllEventsInOrder()
    {
        var sink = new FakeEventSink();
        var store = new FakeAnalysisStore();
        var pipeline = CreatePipeline(sink: sink, store: store);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        var types = sink.Published.Select(e => e.GetType()).ToList();

        Assert.Contains(typeof(AnalysisStartedEvent), types);
        Assert.Contains(typeof(TranscriptExtractedEvent), types);
        Assert.Contains(typeof(DomainDetectedEvent), types);
        Assert.Contains(typeof(SummaryCompleteEvent), types);
        Assert.Contains(typeof(ClaimsExtractedEvent), types);
        Assert.Contains(typeof(ClaimVerifiedEvent), types);
        Assert.Contains(typeof(ScoringCompleteEvent), types);
        Assert.Contains(typeof(AssessmentCompleteEvent), types);
        Assert.DoesNotContain(typeof(AnalysisFailedEvent), types);
    }

    [Fact]
    public async Task RunAsync_HappyPath_ResultIsComplete()
    {
        var store = new FakeAnalysisStore();
        var pipeline = CreatePipeline(store: store);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        var result = store.TryGet("id1");
        Assert.NotNull(result);
        Assert.Equal(AnalysisStatus.Complete, result.Status);
        Assert.NotNull(result.Score);
        Assert.NotNull(result.Assessment);
    }

    [Fact]
    public async Task RunAsync_HappyPath_EventCompleterCalled()
    {
        var completer = new FakeEventCompleter();
        var pipeline = CreatePipeline(completer: completer);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        Assert.Contains("id1", completer.Completed);
    }

    [Fact]
    public async Task RunAsync_HappyPath_FactCheckAddedToResult()
    {
        var store = new FakeAnalysisStore();
        var verifier = new FakeClaimVerifier(MakeFactCheck("c1"));
        var claims = new FakeClaimExtractor([MakeClaim("c1")]);
        var pipeline = CreatePipeline(claims: claims, verifier: verifier, store: store);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        var result = store.TryGet("id1");
        Assert.Single(result!.FactChecks);
        Assert.Equal("c1", result.FactChecks[0].ClaimId);
    }

    // ── Domain detection failure ────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DomainDetectorThrows_DefaultsToGeneralAndContinues()
    {
        var sink = new FakeEventSink();
        var store = new FakeAnalysisStore();
        var domain = new FakeDomainDetector(throws: new InvalidOperationException("domain fail"));
        var pipeline = CreatePipeline(domain: domain, sink: sink, store: store);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        var domainEvent = sink.Published.OfType<DomainDetectedEvent>().Single();
        Assert.Equal(ContentDomain.General, domainEvent.Domain);

        var result = store.TryGet("id1");
        Assert.Equal(AnalysisStatus.Complete, result!.Status);
    }

    // ── Summarisation failure ──────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SummariserThrows_FailsPipeline()
    {
        var sink = new FakeEventSink();
        var store = new FakeAnalysisStore();
        var summariser = new FakeSummariser(throws: new InvalidOperationException("summary fail"));
        var pipeline = CreatePipeline(summariser: summariser, sink: sink, store: store);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        Assert.Contains(sink.Published, e => e is AnalysisFailedEvent);
        var result = store.TryGet("id1");
        Assert.Equal(AnalysisStatus.Failed, result!.Status);
        Assert.Equal(AnalysisStage.Summarisation, result.Error!.Stage);
    }

    [Fact]
    public async Task RunAsync_ClaimExtractorThrows_FailsPipeline()
    {
        var sink = new FakeEventSink();
        var store = new FakeAnalysisStore();
        var extractor = new FakeClaimExtractor(throws: new InvalidOperationException("claims fail"));
        var pipeline = CreatePipeline(claims: extractor, sink: sink, store: store);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        Assert.Contains(sink.Published, e => e is AnalysisFailedEvent);
        var result = store.TryGet("id1");
        Assert.Equal(AnalysisStatus.Failed, result!.Status);
    }

    // ── Individual claim verification failure ──────────────────────────────────

    [Fact]
    public async Task RunAsync_OneClaimVerificationFails_OtherClaimsSucceed()
    {
        var sink = new FakeEventSink();
        var store = new FakeAnalysisStore();
        var claim1 = MakeClaim("c1");
        var claim2 = MakeClaim("c2");
        var claims = new FakeClaimExtractor([claim1, claim2]);

        // c1 fails, c2 succeeds
        var verifier = new FakeClaimVerifier(claimId =>
            claimId == "c1"
                ? Task.FromException<FactCheck>(new InvalidOperationException("c1 fail"))
                : Task.FromResult(MakeFactCheck("c2")));

        var pipeline = CreatePipeline(claims: claims, verifier: verifier, sink: sink, store: store);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        var verifiedEvents = sink.Published.OfType<ClaimVerifiedEvent>().ToList();
        Assert.Equal(2, verifiedEvents.Count);

        var c1Check = verifiedEvents.Single(e => e.FactCheck.ClaimId == "c1");
        Assert.Equal(Verdict.Unverifiable, c1Check.FactCheck.Verdict);

        var c2Check = verifiedEvents.Single(e => e.FactCheck.ClaimId == "c2");
        Assert.Equal(Verdict.Supported, c2Check.FactCheck.Verdict);

        var result = store.TryGet("id1");
        Assert.Equal(AnalysisStatus.Complete, result!.Status);
    }

    // ── Assessment failure ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AssessmentGeneratorThrows_CompletesWithoutAssessment()
    {
        var store = new FakeAnalysisStore();
        var sink = new FakeEventSink();
        var assessment = new FakeAssessmentGenerator(throws: new InvalidOperationException("assess fail"));
        var pipeline = CreatePipeline(assessment: assessment, sink: sink, store: store);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        var result = store.TryGet("id1");
        Assert.Equal(AnalysisStatus.Complete, result!.Status);
        Assert.Null(result.Assessment);
        Assert.DoesNotContain(sink.Published, e => e is AnalysisFailedEvent);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CancelledBeforeStart_PublishesFailedEventAndCompletes()
    {
        var sink = new FakeEventSink();
        var completer = new FakeEventCompleter();
        // Make transcript extractor block until cancelled
        var transcript = new FakeTranscriptExtractor(delay: TimeSpan.FromSeconds(60));
        var pipeline = CreatePipeline(transcript: transcript, sink: sink, completer: completer);

        using var cts = new CancellationTokenSource();
        var runTask = pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"), cts.Token);
        cts.Cancel();

        await runTask; // must not throw

        Assert.Contains(sink.Published, e => e is AnalysisFailedEvent);
        Assert.Contains("id1", completer.Completed);
    }

    // ── Zero claims ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ZeroClaims_SkipsVerificationAndCompletes()
    {
        var store = new FakeAnalysisStore();
        var sink = new FakeEventSink();
        var claims = new FakeClaimExtractor([]);
        var pipeline = CreatePipeline(claims: claims, sink: sink, store: store);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        var result = store.TryGet("id1");
        Assert.Equal(AnalysisStatus.Complete, result!.Status);
        Assert.Empty(result.FactChecks);
        Assert.DoesNotContain(sink.Published, e => e is ClaimVerifiedEvent);
        Assert.Contains(sink.Published, e => e is ScoringCompleteEvent);
    }

    // ── Source validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SourceValidation_ValidatedSourcesStoredInFactCheck()
    {
        var store = new FakeAnalysisStore();
        var source = new Source(new Uri("https://example.com"), "Title", "Snippet", IsAccessible: false);
        var factCheck = new FactCheck("c1", Verdict.Supported, Confidence.High, "Good.", [source]);
        var verifier = new FakeClaimVerifier(factCheck);
        // Source validator marks sources accessible
        var sourceValidator = new FakeSourceValidator(markAccessible: true);
        var pipeline = CreatePipeline(verifier: verifier, sources: sourceValidator, store: store);

        await pipeline.RunAsync("id1", new Uri("https://youtube.com/watch?v=abc123"));

        var result = store.TryGet("id1");
        var fc = result!.FactChecks.Single();
        Assert.True(fc.Sources.Single().IsAccessible);
    }

    // ── Fakes ──────────────────────────────────────────────────────────────────

    private sealed class FakeMetadataProvider(VideoInfo result) : IVideoMetadataProvider
    {
        public Task<VideoInfo> GetMetadataAsync(Uri videoUri, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeTranscriptExtractor : ITranscriptExtractor
    {
        private readonly Transcript? _result;
        private readonly TimeSpan _delay;

        public FakeTranscriptExtractor(Transcript result) => _result = result;
        public FakeTranscriptExtractor(TimeSpan delay) => _delay = delay;

        public async Task<Transcript> ExtractAsync(string videoId, CancellationToken ct = default)
        {
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, ct).ConfigureAwait(false);
            return _result!;
        }
    }

    private sealed class FakeDomainDetector : IDomainDetector
    {
        private readonly ContentDomain _result;
        private readonly Exception? _throws;

        public FakeDomainDetector(ContentDomain result) => _result = result;
        public FakeDomainDetector(Exception throws) => _throws = throws;

        public Task<ContentDomain> DetectAsync(string transcriptSnippet, CancellationToken ct = default) =>
            _throws is not null
                ? Task.FromException<ContentDomain>(_throws)
                : Task.FromResult(_result);
    }

    private sealed class FakeSummariser : ISummariser
    {
        private readonly Summary? _result;
        private readonly Exception? _throws;

        public FakeSummariser(Summary result) => _result = result;
        public FakeSummariser(Exception throws) => _throws = throws;

        public Task<Summary> SummariseAsync(string transcript, ContentDomain domain, CancellationToken ct = default) =>
            _throws is not null
                ? Task.FromException<Summary>(_throws)
                : Task.FromResult(_result!);
    }

    private sealed class FakeClaimExtractor : IClaimExtractor
    {
        private readonly IReadOnlyList<Claim>? _result;
        private readonly Exception? _throws;

        public FakeClaimExtractor(IReadOnlyList<Claim> result) => _result = result;
        public FakeClaimExtractor(Exception throws) => _throws = throws;

        public Task<IReadOnlyList<Claim>> ExtractAsync(
            string transcript, ContentDomain domain, int maxClaims, CancellationToken ct = default) =>
            _throws is not null
                ? Task.FromException<IReadOnlyList<Claim>>(_throws)
                : Task.FromResult(_result!);
    }

    private sealed class FakeClaimVerifier : IClaimVerifier
    {
        private readonly FactCheck? _result;
        private readonly Exception? _throws;
        private readonly Func<string, Task<FactCheck>>? _impl;

        public FakeClaimVerifier(FactCheck result) => _result = result;
        public FakeClaimVerifier(Func<string, Task<FactCheck>> impl) => _impl = impl;
        public FakeClaimVerifier(Exception throws) => _throws = throws;

        public Task<FactCheck> VerifyAsync(Claim claim, Summary summary, ContentDomain domain, CancellationToken ct = default)
        {
            if (_impl is not null) return _impl(claim.Id);
            if (_throws is not null) return Task.FromException<FactCheck>(_throws);
            return Task.FromResult(_result!);
        }
    }

    private sealed class FakeSourceValidator : ISourceValidator
    {
        private readonly bool _markAccessible;

        public FakeSourceValidator(bool markAccessible = false) => _markAccessible = markAccessible;

        public Task<Source> ValidateAsync(Source source, CancellationToken ct = default) =>
            Task.FromResult(source with { IsAccessible = _markAccessible });
    }

    private sealed class FakeScoringEngine : IScoringEngine
    {
        private readonly ScoreBreakdown _result;

        public FakeScoringEngine(ScoreBreakdown result) => _result = result;

        public ScoreBreakdown Calculate(
            IReadOnlyList<Claim> claims, IReadOnlyList<FactCheck> factChecks,
            ContentDomain domain, TranscriptQuality transcriptQuality) => _result;
    }

    private sealed class FakeAssessmentGenerator : IAssessmentGenerator
    {
        private readonly Assessment? _result;
        private readonly Exception? _throws;

        public FakeAssessmentGenerator(Assessment result) => _result = result;
        public FakeAssessmentGenerator(Exception throws) => _throws = throws;

        public Task<Assessment> GenerateAsync(
            Summary summary, IReadOnlyList<FactCheck> factChecks,
            ScoreBreakdown score, CancellationToken ct = default) =>
            _throws is not null
                ? Task.FromException<Assessment>(_throws)
                : Task.FromResult(_result!);
    }

    private sealed class FakeEventSink : IAnalysisEventSink
    {
        public List<AnalysisEvent> Published { get; } = [];

        public Task PublishAsync(AnalysisEvent analysisEvent, CancellationToken ct = default)
        {
            Published.Add(analysisEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEventCompleter : IAnalysisEventCompleter
    {
        public List<string> Completed { get; } = [];

        public void Complete(string analysisId) => Completed.Add(analysisId);
    }

    private sealed class FakeAnalysisStore : IAnalysisStore
    {
        private readonly Dictionary<string, AnalysisResult> _results = [];

        public void Add(AnalysisResult result) => _results[result.Id] = result;
        public AnalysisResult? TryGet(string id) => _results.TryGetValue(id, out var r) ? r : null;
    }
}
