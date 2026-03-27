using System.Text.Json;
using FactChecker.Core.Enums;
using FactChecker.Core.Events;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Pipeline;
using FactChecker.Infrastructure.YouTube;
using FactChecker.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FactChecker.Web.Endpoints;

internal static class AnalysisEndpoints
{
    private static readonly JsonSerializerOptions EventJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/analyse", PostAnalyse);
        app.MapGet("/api/analyse/{id}/stream", GetStream);
        app.MapGet("/api/analyse/{id}", GetById);
        return app;
    }

    // POST /api/analyse
    private static IResult PostAnalyse(
        [FromBody] AnalyseRequest? request,
        IAnalysisStore store,
        AnalysisPipeline pipeline)
    {
        if (request?.Url is not { } videoUri)
            return Results.BadRequest(new { error = "Missing or empty 'url' field." });

        if (!YouTubeUrlValidator.TryParseVideoId(videoUri, out _))
            return Results.BadRequest(new { error = "URL does not appear to be a valid YouTube video URL." });

        var analysisId = Guid.NewGuid().ToString("N");

        // Fire and forget — pipeline publishes events and never throws
        _ = Task.Run(() => pipeline.RunAsync(analysisId, videoUri));

        return Results.Accepted($"/api/analyse/{analysisId}", new { analysisId });
    }

    // GET /api/analyse/{id}/stream  →  text/event-stream (SSE)
    private static async Task GetStream(
        string id,
        HttpContext httpContext,
        IAnalysisEventSource eventSource)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        var ct = httpContext.RequestAborted;

        try
        {
            await foreach (var evt in eventSource.SubscribeAsync(id, ct).ConfigureAwait(false))
            {
                var typeName = GetEventTypeName(evt);
                var data = JsonSerializer.Serialize(evt, evt.GetType(), EventJsonOptions);

                await httpContext.Response.WriteAsync($"event: {typeName}\n", ct).ConfigureAwait(false);
                await httpContext.Response.WriteAsync($"data: {data}\n\n", ct).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal termination
        }
    }

    // GET /api/analyse/{id}
    private static IResult GetById(string id, IAnalysisStore store)
    {
        var result = store.TryGet(id);

        if (result is null)
            return Results.NotFound(new { error = $"Analysis '{id}' not found." });

        if (result.Status is AnalysisStatus.Complete or AnalysisStatus.Failed)
            return Results.Ok(result);

        return Results.Accepted($"/api/analyse/{id}/stream", result);
    }

    private static string GetEventTypeName(AnalysisEvent evt) => evt switch
    {
        AnalysisStartedEvent => "AnalysisStarted",
        TranscriptExtractedEvent => "TranscriptExtracted",
        DomainDetectedEvent => "DomainDetected",
        SummaryCompleteEvent => "SummaryComplete",
        ClaimsExtractedEvent => "ClaimsExtracted",
        ClaimVerifiedEvent => "ClaimVerified",
        ScoringCompleteEvent => "ScoringComplete",
        AssessmentCompleteEvent => "AssessmentComplete",
        AnalysisFailedEvent => "AnalysisFailed",
        _ => evt.GetType().Name
    };
}
