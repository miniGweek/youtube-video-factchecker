using System.Collections.Concurrent;
using FactChecker.Core.Enums;
using FactChecker.Core.Events;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using Microsoft.Extensions.Logging;

namespace FactChecker.Core.Pipeline;

public sealed partial class AnalysisPipeline
{
    private readonly IVideoMetadataProvider _metadataProvider;
    private readonly ITranscriptExtractor _transcriptExtractor;
    private readonly IDomainDetector _domainDetector;
    private readonly ISummariser _summariser;
    private readonly IClaimExtractor _claimExtractor;
    private readonly IClaimVerifier _claimVerifier;
    private readonly ISourceValidator _sourceValidator;
    private readonly IScoringEngine _scoringEngine;
    private readonly IAssessmentGenerator _assessmentGenerator;
    private readonly IAnalysisEventSink _sink;
    private readonly IAnalysisEventCompleter _completer;
    private readonly IAnalysisStore _store;
    private readonly AnalysisOptions _options;
    private readonly ILogger<AnalysisPipeline> _logger;

    public AnalysisPipeline(
        IVideoMetadataProvider metadataProvider,
        ITranscriptExtractor transcriptExtractor,
        IDomainDetector domainDetector,
        ISummariser summariser,
        IClaimExtractor claimExtractor,
        IClaimVerifier claimVerifier,
        ISourceValidator sourceValidator,
        IScoringEngine scoringEngine,
        IAssessmentGenerator assessmentGenerator,
        IAnalysisEventSink sink,
        IAnalysisEventCompleter completer,
        IAnalysisStore store,
        AnalysisOptions options,
        ILogger<AnalysisPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(metadataProvider);
        ArgumentNullException.ThrowIfNull(transcriptExtractor);
        ArgumentNullException.ThrowIfNull(domainDetector);
        ArgumentNullException.ThrowIfNull(summariser);
        ArgumentNullException.ThrowIfNull(claimExtractor);
        ArgumentNullException.ThrowIfNull(claimVerifier);
        ArgumentNullException.ThrowIfNull(sourceValidator);
        ArgumentNullException.ThrowIfNull(scoringEngine);
        ArgumentNullException.ThrowIfNull(assessmentGenerator);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(completer);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _metadataProvider = metadataProvider;
        _transcriptExtractor = transcriptExtractor;
        _domainDetector = domainDetector;
        _summariser = summariser;
        _claimExtractor = claimExtractor;
        _claimVerifier = claimVerifier;
        _sourceValidator = sourceValidator;
        _scoringEngine = scoringEngine;
        _assessmentGenerator = assessmentGenerator;
        _sink = sink;
        _completer = completer;
        _store = store;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full analysis pipeline. Never throws — all failures are published as
    /// <see cref="AnalysisFailedEvent"/> and the analysis channel is always completed.
    /// </summary>
    public async Task RunAsync(string analysisId, Uri videoUri, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        ArgumentNullException.ThrowIfNull(videoUri);

        var result = new AnalysisResult(analysisId);
        _store.Add(result);

        LogAnalysisStarted(analysisId, videoUri);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.PipelineTimeoutSeconds));
        var linkedCt = timeoutCts.Token;

        var currentStage = AnalysisStage.Validation;

        try
        {
            // Stage 1 — Metadata + transcript extraction
            currentStage = AnalysisStage.TranscriptExtraction;
            var video = await _metadataProvider.GetMetadataAsync(videoUri, linkedCt).ConfigureAwait(false);
            result.SetVideo(video);
            await _sink.PublishAsync(
                new AnalysisStartedEvent(analysisId, DateTimeOffset.UtcNow, video), linkedCt)
                .ConfigureAwait(false);

            var transcript = await _transcriptExtractor.ExtractAsync(video.VideoId, linkedCt).ConfigureAwait(false);
            result.SetTranscript(transcript);
            LogTranscriptExtracted(analysisId, transcript.WordCount, transcript.Quality);
            await _sink.PublishAsync(
                new TranscriptExtractedEvent(analysisId, DateTimeOffset.UtcNow, transcript.Quality, transcript.WordCount),
                linkedCt)
                .ConfigureAwait(false);

            // Stage 2 — Domain detection (graceful: defaults to General on failure)
            currentStage = AnalysisStage.DomainDetection;
            ContentDomain domain;
#pragma warning disable CA1031 // Intentional: domain detection failure is non-fatal
            try
            {
                domain = await _domainDetector.DetectAsync(transcript.Text, linkedCt).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDomainDetectionFailed(ex, analysisId);
                domain = ContentDomain.General;
            }
#pragma warning restore CA1031
            LogDomainDetected(analysisId, domain);
            await _sink.PublishAsync(
                new DomainDetectedEvent(analysisId, DateTimeOffset.UtcNow, domain), linkedCt)
                .ConfigureAwait(false);

            // Stage 3 — Summarise + Extract claims in parallel (either failure fails the pipeline)
            currentStage = AnalysisStage.Summarisation;
            var summaryTask = _summariser.SummariseAsync(transcript.Text, domain, linkedCt);
            var claimsTask = _claimExtractor.ExtractAsync(
                transcript.Text, domain, _options.MaxClaimsToVerify, linkedCt);

            await Task.WhenAll(summaryTask, claimsTask).ConfigureAwait(false);

            var summary = await summaryTask.ConfigureAwait(false);
            var claims = await claimsTask.ConfigureAwait(false);

            LogSummarisationComplete(analysisId);
            LogClaimsExtracted(analysisId, claims.Count);
            result.SetSummary(summary);
            await _sink.PublishAsync(
                new SummaryCompleteEvent(analysisId, DateTimeOffset.UtcNow, summary), linkedCt)
                .ConfigureAwait(false);

            result.SetClaims(claims); // transitions → FactChecking
            await _sink.PublishAsync(
                new ClaimsExtractedEvent(analysisId, DateTimeOffset.UtcNow, claims), linkedCt)
                .ConfigureAwait(false);

            // Stage 4 — Claim verification with bounded parallelism
            // Individual claim failures → Unverifiable; they do not fail the pipeline
            currentStage = AnalysisStage.FactChecking;
            var factCheckBag = new ConcurrentBag<FactCheck>();
            int verifiedCount = 0;

            await Parallel.ForEachAsync(
                claims,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _options.MaxConcurrentVerifications,
                    CancellationToken = linkedCt
                },
                async (claim, claimCt) =>
                {
                    FactCheck factCheck;
#pragma warning disable CA1031 // Intentional: individual claim failure must not abort the pipeline
                    try
                    {
                        factCheck = await _claimVerifier
                            .VerifyAsync(claim, summary, domain, claimCt)
                            .ConfigureAwait(false);

                        var validatedSources = new List<Source>(factCheck.Sources.Count);
                        foreach (var source in factCheck.Sources)
                        {
                            var validated = await _sourceValidator
                                .ValidateAsync(source, claimCt)
                                .ConfigureAwait(false);
                            validatedSources.Add(validated);
                        }

                        factCheck = factCheck with { Sources = validatedSources.AsReadOnly() };
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogClaimVerificationFailed(ex, analysisId, claim.Id);
                        factCheck = new FactCheck(
                            claim.Id, Verdict.Unverifiable, Confidence.Low,
                            $"Verification failed: {ex.Message}", []);
                    }
#pragma warning restore CA1031

                    factCheckBag.Add(factCheck);
                    var n = Interlocked.Increment(ref verifiedCount);
                    LogClaimVerified(analysisId, claim.Id, factCheck.Verdict, n, claims.Count);
                    await _sink.PublishAsync(
                        new ClaimVerifiedEvent(analysisId, DateTimeOffset.UtcNow, factCheck), claimCt)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);

            // Apply collected fact checks sequentially (List<T> is not thread-safe)
            foreach (var fc in factCheckBag)
                result.AddFactCheck(fc);

            LogFactCheckingComplete(analysisId, factCheckBag.Count);

            // Stage 5 — Scoring (deterministic, no failure expected)
            currentStage = AnalysisStage.Scoring;
            var score = _scoringEngine.Calculate(claims, result.FactChecks, domain, transcript.Quality);
            result.SetScore(score); // transitions → Scoring
            LogScoringComplete(analysisId, score.AggregateScore);
            await _sink.PublishAsync(
                new ScoringCompleteEvent(analysisId, DateTimeOffset.UtcNow, score), linkedCt)
                .ConfigureAwait(false);

            // Stage 6 — Assessment (graceful: skipped on failure)
            currentStage = AnalysisStage.Assessment;
#pragma warning disable CA1031 // Intentional: assessment failure is non-fatal
            try
            {
                var assessment = await _assessmentGenerator
                    .GenerateAsync(summary, result.FactChecks, score, linkedCt)
                    .ConfigureAwait(false);
                result.SetAssessment(assessment); // transitions → Complete
                await _sink.PublishAsync(
                    new AssessmentCompleteEvent(analysisId, DateTimeOffset.UtcNow, assessment), linkedCt)
                    .ConfigureAwait(false);
                LogAnalysisCompleted(analysisId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogAssessmentFailed(ex, analysisId);
                result.Complete(); // Complete without assessment
            }
#pragma warning restore CA1031
        }
#pragma warning disable CA1031 // Intentional: pipeline must never throw to the caller
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                LogPipelineTimeout(ex, analysisId, currentStage);
            else
                LogPipelineFailed(ex, analysisId, currentStage);

            var message = ex is OperationCanceledException
                ? "Pipeline timed out or was cancelled."
                : ex.Message;

            result.Fail(currentStage, message);

            await _sink.PublishAsync(
                new AnalysisFailedEvent(analysisId, DateTimeOffset.UtcNow, result.Error!),
                CancellationToken.None)
                .ConfigureAwait(false);
        }
#pragma warning restore CA1031
        finally
        {
            _completer.Complete(analysisId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis {AnalysisId} started for {VideoUri}")]
    private partial void LogAnalysisStarted(string analysisId, Uri videoUri);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis {AnalysisId} transcript extracted: {WordCount} words, quality={Quality}")]
    private partial void LogTranscriptExtracted(string analysisId, int wordCount, TranscriptQuality quality);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analysis {AnalysisId} domain detection failed, defaulting to General")]
    private partial void LogDomainDetectionFailed(Exception ex, string analysisId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analysis {AnalysisId} claim {ClaimId} verification failed, marking Unverifiable")]
    private partial void LogClaimVerificationFailed(Exception ex, string analysisId, string claimId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis {AnalysisId} claim {ClaimId} verified: {Verdict} ({VerifiedCount}/{TotalCount})")]
    private partial void LogClaimVerified(string analysisId, string claimId, Verdict verdict, int verifiedCount, int totalCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis {AnalysisId} domain detected: {Domain}")]
    private partial void LogDomainDetected(string analysisId, ContentDomain domain);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis {AnalysisId} summarisation complete")]
    private partial void LogSummarisationComplete(string analysisId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis {AnalysisId} claims extracted: {ClaimCount} claims")]
    private partial void LogClaimsExtracted(string analysisId, int claimCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis {AnalysisId} fact-checking complete: {CheckCount} claims verified")]
    private partial void LogFactCheckingComplete(string analysisId, int checkCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis {AnalysisId} scoring complete: {Score}")]
    private partial void LogScoringComplete(string analysisId, double score);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis {AnalysisId} completed successfully")]
    private partial void LogAnalysisCompleted(string analysisId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analysis {AnalysisId} assessment generation failed, completing without assessment")]
    private partial void LogAssessmentFailed(Exception ex, string analysisId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Analysis {AnalysisId} pipeline timed out at stage {Stage}")]
    private partial void LogPipelineTimeout(Exception ex, string analysisId, AnalysisStage stage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Analysis {AnalysisId} pipeline failed at stage {Stage}")]
    private partial void LogPipelineFailed(Exception ex, string analysisId, AnalysisStage stage);
}
