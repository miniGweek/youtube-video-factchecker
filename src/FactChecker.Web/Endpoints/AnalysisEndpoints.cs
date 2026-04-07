using System.Text.Json;
using FactChecker.Core.Enums;
using FactChecker.Core.Events;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Pipeline;
using FactChecker.Infrastructure.YouTube;
using FactChecker.Web.Models;
using FactChecker.Web.Services;
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
        app.MapGet("/analysis/{id}/stream", GetHtmlStream);
        return app;
    }

    // POST /api/analyse
    private static async Task<IResult> PostAnalyse(
        [FromBody] AnalyseRequest? request,
        IAnalysisDispatcher queue,
        IAnalysisStore store)
    {
        if (request?.Url is not { } videoUri)
            return Results.BadRequest(new { error = "Missing or empty 'url' field." });

        if (!YouTubeUrlValidator.TryParseVideoId(videoUri, out var videoId))
            return Results.BadRequest(new { error = "URL does not appear to be a valid YouTube video URL." });

        // Return existing in-progress analysis for the same video
        var existingId = store.TryGetActiveByVideoId(videoId);
        if (existingId is not null)
            return Results.Accepted($"/api/analyse/{existingId}", new { analysisId = existingId });

        var analysisId = Guid.NewGuid().ToString("N");
        store.TrackVideoId(videoId, analysisId);

        await queue.EnqueueAsync(analysisId, videoUri).ConfigureAwait(false);

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

    // GET /analysis/{id}/stream  →  text/event-stream (HTML fragments for HTMX SSE)
    private static async Task GetHtmlStream(
        string id,
        HttpContext httpContext,
        IAnalysisEventSource eventSource,
        IAnalysisStore store,
        ViewRenderer viewRenderer)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        var ct = httpContext.RequestAborted;

        async Task SendHtmlAsync(string eventName, string html)
        {
            await httpContext.Response.WriteAsync($"event: {eventName}\n", ct).ConfigureAwait(false);
            // Each line of the HTML body becomes a separate SSE data: line;
            // the browser EventSource API joins them with \n before HTMX sees the content.
            foreach (var line in html.Split('\n'))
            {
                await httpContext.Response.WriteAsync($"data: {line}\n", ct).ConfigureAwait(false);
            }
            await httpContext.Response.WriteAsync("\n", ct).ConfigureAwait(false);
            await httpContext.Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }

        int verifiedCount = 0;
        int totalClaims = 0;

        try
        {
            await foreach (var evt in eventSource.SubscribeAsync(id, ct).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case AnalysisStartedEvent started:
                    {
                        var html = await viewRenderer.RenderPartialAsync(httpContext, "_VideoHeader", started.Video).ConfigureAwait(false);
                        await SendHtmlAsync("VideoHeader", html).ConfigureAwait(false);
                        await SendHtmlAsync("Status", "<p aria-busy=\"true\">Extracting transcript…</p>").ConfigureAwait(false);
                        break;
                    }

                    case TranscriptExtractedEvent transcript:
                    {
                        var html = await viewRenderer.RenderPartialAsync(httpContext, "_TranscriptInfo", transcript).ConfigureAwait(false);
                        await SendHtmlAsync("TranscriptInfo", html).ConfigureAwait(false);
                        await SendHtmlAsync("Status", "<p aria-busy=\"true\">Detecting domain…</p>").ConfigureAwait(false);
                        break;
                    }

                    case DomainDetectedEvent domain:
                    {
                        var html = await viewRenderer.RenderPartialAsync(httpContext, "_DomainBadge", domain.Domain).ConfigureAwait(false);
                        await SendHtmlAsync("DomainBadge", html).ConfigureAwait(false);
                        await SendHtmlAsync("Status", "<p aria-busy=\"true\">Summarising and extracting claims…</p>").ConfigureAwait(false);
                        break;
                    }

                    case SummaryCompleteEvent summary:
                    {
                        var html = await viewRenderer.RenderPartialAsync(httpContext, "_Summary", summary.Summary).ConfigureAwait(false);
                        await SendHtmlAsync("Summary", html).ConfigureAwait(false);
                        break;
                    }

                    case ClaimsExtractedEvent claims:
                    {
                        totalClaims = claims.Claims.Count;
                        var html = await viewRenderer.RenderPartialAsync(httpContext, "_ClaimsHeader", new ClaimsHeaderModel(totalClaims, false, 0)).ConfigureAwait(false);
                        await SendHtmlAsync("ClaimsHeader", html).ConfigureAwait(false);
                        await SendHtmlAsync("Status", "<p aria-busy=\"true\">Verifying claims…</p>").ConfigureAwait(false);
                        break;
                    }

                    case ClaimVerifiedEvent verified:
                    {
                        var result = store.TryGet(id);
                        var claim = result?.Claims?.FirstOrDefault(c => c.Id == verified.FactCheck.ClaimId);
                        if (claim is not null)
                        {
                            var model = new ClaimVerdictModel(claim, verified.FactCheck);
                            var html = await viewRenderer.RenderPartialAsync(httpContext, "_ClaimVerdict", model).ConfigureAwait(false);
                            await SendHtmlAsync("ClaimVerified", html).ConfigureAwait(false);
                        }
                        verifiedCount++;
                        var headerHtml = await viewRenderer.RenderPartialAsync(httpContext, "_ClaimsHeader", new ClaimsHeaderModel(totalClaims, false, verifiedCount)).ConfigureAwait(false);
                        await SendHtmlAsync("ClaimsHeader", headerHtml).ConfigureAwait(false);
                        break;
                    }

                    case ScoringCompleteEvent scoring:
                    {
                        var scoreHtml = await viewRenderer.RenderPartialAsync(httpContext, "_Score", scoring.Score).ConfigureAwait(false);
                        await SendHtmlAsync("Score", scoreHtml).ConfigureAwait(false);
                        // All claims are now verified — replace the spinning claims header with a done state
                        var claimCount = store.TryGet(id)?.Claims?.Count ?? 0;
                        var claimsHtml = await viewRenderer.RenderPartialAsync(httpContext, "_ClaimsHeader", new ClaimsHeaderModel(claimCount, true, claimCount)).ConfigureAwait(false);
                        await SendHtmlAsync("ClaimsHeader", claimsHtml).ConfigureAwait(false);
                        await SendHtmlAsync("Status", "<p aria-busy=\"true\">Generating assessment…</p>").ConfigureAwait(false);
                        break;
                    }

                    case AssessmentCompleteEvent assessment:
                    {
                        var html = await viewRenderer.RenderPartialAsync(httpContext, "_Assessment", assessment.Assessment).ConfigureAwait(false);
                        await SendHtmlAsync("Assessment", html).ConfigureAwait(false);
                        await SendHtmlAsync("Status", "").ConfigureAwait(false);
                        break;
                    }

                    case AnalysisFailedEvent failed:
                    {
                        var html = await viewRenderer.RenderPartialAsync(httpContext, "_Error", failed.Error).ConfigureAwait(false);
                        await SendHtmlAsync("Error", html).ConfigureAwait(false);
                        await SendHtmlAsync("Status", "").ConfigureAwait(false);
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal termination
        }
    }
}
