# YouTube Video Fact-Checker — Codebase Guide

A web app that takes a YouTube URL and produces: a structured video summary, per-claim fact-checks with source citations, and a watch-worthiness score. Single-instance, stateless (in-memory), no auth required.

**Stack:** C# / .NET 9, ASP.NET Core, Razor Pages, HTMX, Pico CSS, Anthropic Claude API, YoutubeExplode.
**Tests:** 145 passing (xUnit). All warnings treated as errors.

---

## Project Structure

```
youtube-fact-checker.sln
├── src/
│   ├── FactChecker.Core/          # Domain models, interfaces, pipeline, scoring — zero NuGet deps
│   ├── FactChecker.Infrastructure/ # Implements Core interfaces: YouTube, Anthropic, HTTP, in-memory store
│   └── FactChecker.Web/           # Composition root: DI wiring, API endpoints, Razor pages
├── tests/
│   ├── FactChecker.Core.Tests/
│   ├── FactChecker.Infrastructure.Tests/
│   └── FactChecker.Web.Tests/
├── .ai/                           # AI agent memory, architecture docs, task contracts
├── Dockerfile
└── docker-compose.yml
```

Dependency direction (strict, one-way):

```
FactChecker.Web → FactChecker.Infrastructure → FactChecker.Core
FactChecker.Web → FactChecker.Core
```

---

## FactChecker.Core

Pure C#. No NuGet dependencies. Everything here is domain logic only.

### Models (`Core/Models/`)

| File | What it represents |
|------|--------------------|
| `VideoInfo.cs` | YouTube video metadata: title, channel, duration, thumbnail URL |
| `Transcript.cs` | Extracted transcript text, word count, `TranscriptQuality` (Auto/Manual) |
| `Claim.cs` | A single falsifiable assertion extracted from the transcript. Has `Id`, `Text`, `Importance` (1–5) |
| `FactCheck.cs` | Verdict for one claim: `Verdict`, `Confidence`, explanation, list of `Source` |
| `Source.cs` | A citation URL with `IsAccessible` flag (set after HTTP HEAD validation) |
| `Summary.cs` | LLM-generated summary: key points, main topics, estimated audience |
| `ScoreBreakdown.cs` | Four numeric scores (0–100): Accuracy, SourceQuality, Verifiability, Aggregate |
| `Assessment.cs` | LLM-synthesised watch recommendation (`WatchRecommendation` enum) + rationale |
| `AnalysisResult.cs` | **The central mutable aggregate.** Holds all of the above. Has strict state-machine transitions via `SetVideo()`, `SetTranscript()`, `SetSummary()`, `SetClaims()`, `AddFactCheck()`, `SetScore()`, `SetAssessment()`, `Fail()`. Status progresses: `Submitted → Extracting → Analysing → FactChecking → Scoring → Complete/Failed`. |
| `AnalysisError.cs` | Records which `AnalysisStage` failed and the error message |

### Enums (`Core/Enums/`)

| Enum | Values |
|------|--------|
| `AnalysisStatus` | Submitted, Extracting, Analysing, FactChecking, Scoring, Complete, Failed |
| `AnalysisStage` | Validation, TranscriptExtraction, DomainDetection, Summarisation, FactChecking, Scoring, Assessment |
| `Verdict` | Supported, PartiallySupported, Unverifiable, Refuted, NotAClaim |
| `Confidence` | Low, Medium, High |
| `ContentDomain` | General, Science, Technology, Health, Finance, Politics, History, Entertainment |
| `TranscriptQuality` | Auto, Manual |
| `WatchRecommendation` | Recommended, RecommendedWithCaveats, NotRecommended |

### Interfaces (`Core/Interfaces/`)

These are the seams between Core and Infrastructure. Core never references implementations.

| Interface | Responsibility |
|-----------|---------------|
| `IVideoMetadataProvider` | Fetch `VideoInfo` from a YouTube URL |
| `ITranscriptExtractor` | Download and return `Transcript` for a video ID |
| `IDomainDetector` | Classify transcript into a `ContentDomain` |
| `ISummariser` | Generate `Summary` from transcript text |
| `IClaimExtractor` | Extract a list of `Claim` from transcript text |
| `IClaimVerifier` | Fact-check a single `Claim`, return `FactCheck` with sources |
| `ISourceValidator` | HTTP HEAD-check a `Source` URL, return updated `Source` with `IsAccessible` |
| `IScoringEngine` | Compute `ScoreBreakdown` deterministically from claims + fact-checks |
| `IAssessmentGenerator` | Generate `Assessment` from summary + fact-checks + score |
| `IAnalysisEventSink` | Write events into the event transport |
| `IAnalysisEventSource` | Subscribe to events by analysis ID (async enumerable) |

### Events (`Core/Events/`)

One record per pipeline stage. All inherit `AnalysisEvent` (base record with `AnalysisId` and `Timestamp`).

| Event | Fired when |
|-------|-----------|
| `AnalysisStartedEvent` | Metadata fetched; carries `VideoInfo` |
| `TranscriptExtractedEvent` | Transcript ready; carries quality + word count |
| `DomainDetectedEvent` | Domain classified; carries `ContentDomain` |
| `SummaryCompleteEvent` | Summary ready; carries `Summary` |
| `ClaimsExtractedEvent` | Claims ready; carries the full `IReadOnlyList<Claim>` |
| `ClaimVerifiedEvent` | One claim verified; carries `FactCheck` |
| `ScoringCompleteEvent` | Score computed; carries `ScoreBreakdown` |
| `AssessmentCompleteEvent` | Assessment ready; carries `Assessment` |
| `AnalysisFailedEvent` | Pipeline failed; carries `AnalysisError` |

### Pipeline (`Core/Pipeline/`)

**`AnalysisPipeline.cs`** — the top-level orchestrator. `RunAsync(analysisId, videoUri)`:

1. Fetch metadata → emit `AnalysisStartedEvent`
2. Extract transcript → emit `TranscriptExtractedEvent`
3. Detect domain (graceful — defaults to `General` on failure) → emit `DomainDetectedEvent`
4. **Parallel:** Summarise + Extract claims (`Task.WhenAll`) → emit `SummaryCompleteEvent`, `ClaimsExtractedEvent`
5. **Parallel bounded:** Verify each claim (`Parallel.ForEachAsync`, max 4 concurrent) + validate sources per claim → emit `ClaimVerifiedEvent` per claim
6. Score deterministically → emit `ScoringCompleteEvent`
7. Generate assessment (graceful — skipped on failure) → emit `AssessmentCompleteEvent`
8. On any hard failure: emit `AnalysisFailedEvent`, call `_completer.Complete(id)` in `finally`

The pipeline **never throws** — all errors become `AnalysisFailedEvent`. Individual claim verification failures mark that claim `Unverifiable` and continue.

**`IAnalysisStore`** (interface in Core/Pipeline/) — read/write `AnalysisResult` by ID.

**`IAnalysisEventCompleter`** (interface in Core/Pipeline/) — signals the channel is done writing (closes it). Added beyond original scaffold to manage channel lifecycle.

### Scoring (`Core/Scoring/`)

**`DefaultScoringEngine.cs`** — pure deterministic math, no LLM involvement:

- **Accuracy (60%):** Weighted average of verdict scores (Supported=100, Partially=60, Unverifiable=40, Refuted=0), weighted by claim `Importance`. Extra −15 per high-importance (≥4) refuted claim, capped at −30.
- **Source Quality (20%):** % of source URLs that are accessible.
- **Verifiability (15%):** % of claims that have a non-Unverifiable verdict.
- **Transcript Bonus (5%):** +100 if transcript is `Manual` quality.
- All sub-scores clamped to [0, 100].

### Options (`Core/Options/`)

**`AnalysisOptions.cs`** — bound from `appsettings.json`:
- `MaxVideoDurationMinutes` (45)
- `MaxClaimsToVerify` (15)
- `MaxConcurrentVerifications` (4)
- `SourceValidationTimeoutSeconds` (5)
- `PipelineTimeoutSeconds` (120)

---

## FactChecker.Infrastructure

Implements all Core interfaces. External dependencies: Anthropic SDK (direct HTTP), YoutubeExplode, Polly.

### YouTube (`Infrastructure/YouTube/`)

| File | What it does |
|------|-------------|
| `YouTubeMetadataProvider.cs` | Implements `IVideoMetadataProvider`. Uses YoutubeExplode to fetch title, channel, duration, thumbnail |
| `YouTubeTranscriptExtractor.cs` | Implements `ITranscriptExtractor`. Uses YoutubeExplode to download captions; prefers manual tracks |
| `YouTubeUrlValidator.cs` | Static utility. `TryParseVideoId(Uri, out string)` — validates YouTube URL formats |
| `TranscriptNotAvailableException.cs` | Thrown when no caption track exists for a video |

### Anthropic (`Infrastructure/Anthropic/`)

**`AnthropicClientWrapper.cs`** — shared HTTP client for all LLM calls:
- Routes to Fast (Haiku) or Standard (Sonnet) model via `ModelTier` enum
- Polly retry pipeline: exponential backoff with jitter on `HttpRequestException` / `TimeoutException`
- If JSON parse fails, retries once with a "respond with valid JSON only" nudge
- `SendAsync<T>()` — sends prompt, deserialises response to `T`
- `SendWithToolsAsync()` — sends prompt with tool definitions, returns raw JSON

**`ModelTier.cs`** — `Fast` (Haiku, used for: domain detection, assessment) vs `Standard` (Sonnet, used for: summarisation, claim extraction, claim verification with web search)

**`StructuredOutputParser.cs`** — strips markdown code fences then calls `JsonSerializer.Deserialize<T>`

**`AnthropicTool.cs`** — record representing a tool definition sent to the API (name, description, input schema)

**`AnthropicException.cs`** — thrown for non-transient API errors (4xx other than 429)

**`StagePrompts.cs`** (`Anthropic/Prompts/`) — all prompt templates in one file as string constants

**LLM stage implementations** (`Anthropic/Stages/`):

| File | Interface | Model | What it does |
|------|-----------|-------|-------------|
| `AnthropicDomainDetector.cs` | `IDomainDetector` | Fast | Single prompt, returns `ContentDomain` |
| `AnthropicSummariser.cs` | `ISummariser` | Standard | Returns structured `Summary` JSON |
| `AnthropicClaimExtractor.cs` | `IClaimExtractor` | Standard | Returns list of `Claim` JSON objects |
| `AnthropicClaimVerifier.cs` | `IClaimVerifier` | Standard | Uses web search tool to look up claims; returns `FactCheck` with sources |
| `AnthropicAssessmentGenerator.cs` | `IAssessmentGenerator` | Fast | Returns `Assessment` JSON with watch recommendation |

### Events (`Infrastructure/Events/`)

**`ChannelEventTransport.cs`** — implements `IAnalysisEventSink`, `IAnalysisEventSource`, `IAnalysisEventCompleter`. Singleton.

Uses `System.Threading.Channels`: one `Channel<AnalysisEvent>` per analysis ID stored in a `ConcurrentDictionary`. The pipeline writes via `PublishAsync()`, SSE endpoints read via `SubscribeAsync()` (async enumerable), and the pipeline signals done via `Complete()` which closes the channel writer. Late subscribers (arriving after `Complete()`) see an already-closed channel and yield zero items.

### Validation (`Infrastructure/Validation/`)

**`HttpSourceValidator.cs`** — implements `ISourceValidator`. Sends HTTP HEAD request (with 5s timeout) to each source URL; sets `IsAccessible = true/false`.

### Storage (`Infrastructure/Storage/`)

**`InMemoryAnalysisStore.cs`** — implements `IAnalysisStore`. Singleton `ConcurrentDictionary<string, AnalysisResult>`. `Add(result)` and `TryGet(id)`. No persistence — resets on restart.

### Options (`Infrastructure/Options/`)

**`AnthropicOptions.cs`** — bound from `appsettings.json`:
- `ApiKey` — overridable by `ANTHROPIC_API_KEY` env var (env takes precedence)
- `FastModel` — `claude-haiku-4-5-20251001`
- `StandardModel` — `claude-sonnet-4-20250514`
- `MaxRetries` — 2

---

## FactChecker.Web

Composition root. Wires DI, exposes HTTP surface.

### Program.cs

Registers all services:
- `AnalysisOptions` + `AnthropicOptions` from config (env var overrides API key)
- `IHttpClientFactory` (shared across all HTTP calls)
- Infrastructure implementations for all Core interfaces
- `ChannelEventTransport` as singleton, registered under three interfaces
- `InMemoryAnalysisStore` as singleton
- `AnalysisPipeline` as transient
- Razor Pages + `ViewRenderer`
- CORS (localhost:5000 + localhost:5001)

### API Endpoints (`Web/Endpoints/AnalysisEndpoints.cs`)

Minimal API, all routes registered via `MapAnalysisEndpoints()` extension:

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/api/analyse` | Validates URL, fires pipeline as background task (`Task.Run`), returns `202 { analysisId }` |
| `GET` | `/api/analyse/{id}/stream` | SSE stream of **JSON events** (one per pipeline stage). Used by API consumers. |
| `GET` | `/api/analyse/{id}` | Snapshot: `200` if complete/failed, `202` if still running |
| `GET` | `/analysis/{id}/stream` | SSE stream of **HTML fragments** for HTMX. Uses `ViewRenderer` to render Razor partials per event. |

### Razor Pages (`Web/Pages/`)

| File | Route | What it does |
|------|-------|-------------|
| `Index.cshtml` / `.cs` | `/` | YouTube URL input form. Client-side validation via HTML5 `pattern`. On submit, POSTs to `/api/analyse` then redirects to `/analysis/{id}`. |
| `Analysis.cshtml` / `.cs` | `/analysis/{id}` | Results page. On load, opens HTMX SSE connection to `/analysis/{id}/stream`. Each incoming HTML event targets a named `<div>` via `hx-swap-oob`. |

**Shared partials** (`Web/Pages/Shared/`):

| Partial | Model type | Renders |
|---------|-----------|---------|
| `_VideoHeader.cshtml` | `VideoInfo` | Thumbnail, title, channel, duration |
| `_TranscriptInfo.cshtml` | `TranscriptExtractedEvent` | Word count, quality badge |
| `_DomainBadge.cshtml` | `ContentDomain` | Coloured pill badge |
| `_Summary.cshtml` | `Summary` | Key points list, main topics, audience |
| `_ClaimsHeader.cshtml` | `int` (claim count) | "N claims to verify" heading |
| `_ClaimVerdict.cshtml` | `ClaimVerdictModel` | Claim text + verdict badge + confidence + sources |
| `_Score.cshtml` | `ScoreBreakdown` | Four score bars (Accuracy, Source Quality, Verifiability, Aggregate) |
| `_Assessment.cshtml` | `Assessment` | Watch recommendation + rationale |
| `_Error.cshtml` | `AnalysisError` | Error stage + message |
| `_Layout.cshtml` | — | HTML shell with Pico CSS + HTMX + HTMX SSE extension from CDN |

**`_ViewImports.cshtml`** — adds `@addTagHelper *` and common namespaces.
**`_ViewStart.cshtml`** — sets `_Layout` as default layout.

### Services (`Web/Services/`)

**`ViewRenderer.cs`** — renders a named Razor partial to an HTML string using `IRazorViewEngine`. Used by `GetHtmlStream` to generate HTML fragments from event data without a full page render.

### Models (`Web/Models/`)

| File | Purpose |
|------|---------|
| `AnalyseRequest.cs` | Request body for `POST /api/analyse`: `{ "url": "..." }` |
| `ClaimVerdictModel.cs` | View model combining a `Claim` and its `FactCheck` for the `_ClaimVerdict` partial |

---

## Configuration (`appsettings.json`)

```json
{
  "AnalysisOptions": {
    "MaxVideoDurationMinutes": 45,
    "MaxClaimsToVerify": 15,
    "MaxConcurrentVerifications": 4,
    "SourceValidationTimeoutSeconds": 5,
    "PipelineTimeoutSeconds": 120
  },
  "AnthropicOptions": {
    "FastModel": "claude-haiku-4-5-20251001",
    "StandardModel": "claude-sonnet-4-20250514",
    "MaxRetries": 2
  }
}
```

API key: set `ANTHROPIC_API_KEY` environment variable (takes precedence over `appsettings.json`).

---

## Data Flow

```
Browser: POST /api/analyse  { url }
  └─ AnalysisEndpoints.PostAnalyse()
       ├─ Validates URL via YouTubeUrlValidator
       ├─ Generates analysisId (GUID)
       ├─ Fires pipeline.RunAsync() as background Task.Run (non-blocking)
       └─ Returns 202 { analysisId }

Browser: GET /analysis/{id}/stream  (HTMX SSE)
  └─ AnalysisEndpoints.GetHtmlStream()
       └─ Iterates eventSource.SubscribeAsync(id)  ← blocks on channel reads
            └─ For each event, renders Razor partial → sends as SSE HTML fragment
                 └─ HTMX swaps named <div> elements in Analysis.cshtml

Background: AnalysisPipeline.RunAsync()
  ├─ Each stage: does work → sink.PublishAsync(event) → channel.Writer.WriteAsync()
  └─ finally: completer.Complete(id) → channel.Writer.TryComplete()
```

---

## Tests

| Project | Count | Approach |
|---------|-------|---------|
| `FactChecker.Core.Tests` | 53 | Unit tests. No mocks. Hand-crafted inputs for scoring, state-machine, pipeline (mocked interfaces). |
| `FactChecker.Infrastructure.Tests` | 79 | Unit tests for LLM stages use **recorded API response fixtures** (JSON files) — no live API calls. Channel transport, URL validator, HTTP source validator tested directly. |
| `FactChecker.Web.Tests` | 13 | Integration tests via `WebApplicationFactory`. Tests all three API endpoints. |

---

## What's Not Here (Intentional)

- No database — in-memory only (`InMemoryAnalysisStore`)
- No authentication
- No JavaScript build pipeline — HTMX + Pico CSS loaded from CDN
- No LLM framework (LangChain etc.) — direct Anthropic API calls via SDK
- No score generation by LLM — scoring is pure deterministic math in `DefaultScoringEngine`
