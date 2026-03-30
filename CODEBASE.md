# YouTube Video Fact-Checker — Codebase Guide

A web app that takes a YouTube URL and produces: a structured video summary, per-claim fact-checks with source citations, and a watch-worthiness score. Single-instance, stateless (in-memory), no auth required.

**Stack:** C# / .NET 9, ASP.NET Core, Razor Pages, HTMX, Pico CSS, Google Gemini API (default) / Anthropic Claude API (alternate), YoutubeExplode.
**Tests:** 220 passing (xUnit). All warnings treated as errors.

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
| `AnalysisStage` | Validation, TranscriptExtraction, DomainDetection, Summarisation, ClaimExtraction, FactChecking, SourceValidation, Scoring, Assessment |
| `Verdict` | Supported, PartiallySupported, Unverifiable, Refuted, NotAClaim |
| `Confidence` | Low, Medium, High |
| `ContentDomain` | News, Science, Finance, Health, General |
| `TranscriptQuality` | Auto, Manual |
| `WatchRecommendation` | Watch, WatchWithCaution, Skip |
| `ModelTier` | Fast, Standard, Premium — selects cost/capability tier for LLM calls; provider implementations map each tier to a concrete model string |

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

**`AnalysisPipeline.cs`** — the top-level orchestrator. Accepts `ILogger<AnalysisPipeline>` and uses source-generated `[LoggerMessage]` methods for structured logging at each stage. `RunAsync(analysisId, videoUri)`:

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

**`StageModelOptions.cs`** — configures which `ModelTier` each pipeline stage uses; tunable via `appsettings.json` without code changes:
- `DomainDetection` → Fast
- `Summarisation` → Fast
- `ClaimExtraction` → Standard
- `ClaimVerification` → Premium
- `Assessment` → Fast

---

## FactChecker.Infrastructure

Implements all Core interfaces. External dependencies: Anthropic SDK (direct HTTP), YoutubeExplode, Polly.

### YouTube (`Infrastructure/YouTube/`)

| File | What it does |
|------|-------------|
| `YouTubeMetadataProvider.cs` | Implements `IVideoMetadataProvider`. Uses YoutubeExplode to fetch title, channel, duration, thumbnail |
| `YouTubeTranscriptExtractor.cs` | Implements `ITranscriptExtractor`. Uses YoutubeExplode to download captions; prefers manual tracks. Accepts `ILogger<YouTubeTranscriptExtractor>` with source-generated log methods |
| `YouTubeUrlValidator.cs` | Static utility. `TryParseVideoId(Uri, out string)` — validates YouTube URL formats |
| `TranscriptNotAvailableException.cs` | Thrown when no caption track exists for a video |

### LLM Provider Abstraction (`Infrastructure/LlmProviders/Common/`)

Provider-agnostic types shared by all LLM provider implementations. No provider-specific dependencies.

| File | What it does |
|------|-------------|
| `ILlmClient.cs` | Interface with `CompleteAsync` and `CompleteWithSearchAsync`. Stages depend only on this — never on provider types. |
| `LlmRequest.cs` | Record: `StageId`, `ModelTier`, `SystemPrompt`, `UserPrompt` |
| `LlmResponse.cs` | Record: `Content`, `TokenUsage` — result of a standard completion |
| `LlmSearchResponse.cs` | Record: `Content`, `IReadOnlyList<SearchResultSource>`, `TokenUsage` — result of a search-enabled completion |
| `SearchResultSource.cs` | Record: `Uri Url`, `Title`, `Snippet` — provider-agnostic search result |
| `TokenUsage.cs` | Record: `InputTokens`, `OutputTokens` |
| `StructuredOutputParser.cs` | Static utility. `Parse<T>()` strips markdown code fences and deserialises JSON from LLM response text |

### Gemini (`Infrastructure/LlmProviders/Gemini/`)

Google Gemini API provider implementation. Direct HTTP calls to the Gemini REST API (no Google Cloud SDK).

| File | What it does |
|------|-------------|
| `GeminiLlmClient.cs` | Implements `ILlmClient`. `CompleteAsync` sends `generateContent` POST to Gemini API. `CompleteWithSearchAsync` adds `tools: [{ google_search: {} }]`. Maps `ModelTier` to model strings via `GeminiOptions`. Polly retry with exponential backoff on 429/500/503. Source-generated `[LoggerMessage]` methods log model, token counts, latency, grounding source count. |
| `GeminiOptions.cs` | Config: `ApiKey` (env var only), `FastModel` (gemini-2.5-flash), `StandardModel` (gemini-2.5-flash), `PremiumModel` (gemini-2.5-pro), `EnableSearchGrounding`, `MaxRetries` |
| `GeminiGroundingParser.cs` | Static class. Extracts `SearchResultSource` records from `groundingMetadata.groundingChunks`. Maps `groundingSupports` segments to snippets. Returns empty list when no grounding metadata. |
| `GeminiApiException.cs` | Thrown for non-transient Gemini API errors (4xx other than 429) |

### Anthropic Provider (`Infrastructure/LlmProviders/Anthropic/`)

| File | What it does |
|------|-------------|
| `AnthropicLlmClient.cs` | Implements `ILlmClient`. `CompleteAsync` sends Messages API requests. `CompleteWithSearchAsync` uses `web_search_20250305` tool. Maps `ModelTier` to model strings via `AnthropicOptions`. Polly retry with exponential backoff on 429/500/503. Source-generated `[LoggerMessage]` methods. |
| `AnthropicWebSearchParser.cs` | Static class. Extracts `SearchResultSource` records from interleaved `tool_use`/`tool_result` content blocks in Anthropic web search responses. |

### Anthropic Legacy (`Infrastructure/Anthropic/`)

Legacy Anthropic infrastructure from the initial build. `AnthropicClientWrapper` and `AnthropicLlmClientAdapter` are retained for backwards compatibility but are no longer used when `LlmProvider` is set to `"Gemini"` (default) or `"Anthropic"` (which uses `AnthropicLlmClient` in `LlmProviders/Anthropic/`).

- **`AnthropicClientWrapper.cs`** — original HTTP client with Polly retry, `SendAsync<T>()`, `SendWithToolsAsync()`
- **`AnthropicLlmClientAdapter.cs`** — temporary adapter (deprecated) wrapping `AnthropicClientWrapper` as `ILlmClient`
- **`AnthropicTool.cs`** — tool definition record for API calls
- **`AnthropicException.cs`** — exception type for non-transient API errors
- **`ModelTier.cs`** — legacy 2-tier enum (superseded by `Core/Enums/ModelTier.cs`)
- **`StructuredOutputParser.cs`** — legacy location (superseded by `LlmProviders/Common/StructuredOutputParser.cs`)

### LLM Stage Implementations (`Infrastructure/LlmProviders/Stages/`)

Provider-agnostic stage implementations. Each stage depends only on `ILlmClient` and `StageModelOptions` — no Anthropic or Gemini imports.

| File | Interface | Default Tier | What it does |
|------|-----------|-------------|-------------|
| `DomainDetectorStage.cs` | `IDomainDetector` | Fast | Truncates transcript to 1000 words, classifies `ContentDomain` |
| `SummariserStage.cs` | `ISummariser` | Fast | Returns structured `Summary` with thesis and key points |
| `ClaimExtractorStage.cs` | `IClaimExtractor` | Standard | Extracts list of `Claim` with importance scores clamped to 1-5 |
| `ClaimVerifierStage.cs` | `IClaimVerifier` | Premium | Uses `CompleteWithSearchAsync`; maps `SearchResultSource` to domain `Source` records |
| `AssessmentGeneratorStage.cs` | `IAssessmentGenerator` | Fast | Returns `Assessment` with watch recommendation |

**`StagePrompts.cs`** (`LlmProviders/Stages/Prompts/`) — all prompt templates in one file as string constants. Provider-agnostic.

### DI Registration (`Infrastructure/LlmProviders/`)

**`ServiceCollectionExtensions.cs`** — `AddLlmProvider(IServiceCollection, IConfiguration)` extension method. Reads `"LlmProvider"` config value, registers the appropriate `ILlmClient` implementation (Gemini or Anthropic) as singleton, `StageModelOptions` from config, and all 5 provider-agnostic stage implementations.

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
- `PremiumModel` — `claude-sonnet-4-20250514`
- `MaxRetries` — 2

---

## FactChecker.Web

Composition root. Wires DI, exposes HTTP surface.

### Program.cs

Configures **Serilog** as the logging provider (`UseSerilog`, reads from `Serilog` config section). Registers all services:
- `AnalysisOptions` from config
- `IHttpClientFactory` (shared across all HTTP calls)
- LLM provider via `AddLlmProvider(configuration)` — reads `LlmProvider` config, registers `ILlmClient` (Gemini or Anthropic), `StageModelOptions`, and all 5 provider-agnostic stages
- YouTube extractors, source validator, scoring engine
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
  "LlmProvider": "Gemini",
  "Serilog": { "..." },
  "AnalysisOptions": {
    "MaxVideoDurationMinutes": 45,
    "MaxClaimsToVerify": 15,
    "MaxConcurrentVerifications": 4,
    "SourceValidationTimeoutSeconds": 5,
    "PipelineTimeoutSeconds": 600
  },
  "StageModelOptions": {
    "DomainDetection": "Fast",
    "Summarisation": "Fast",
    "ClaimExtraction": "Standard",
    "ClaimVerification": "Premium",
    "Assessment": "Fast"
  },
  "GeminiOptions": {
    "FastModel": "gemini-2.5-flash",
    "StandardModel": "gemini-2.5-flash",
    "PremiumModel": "gemini-2.5-pro",
    "EnableSearchGrounding": true,
    "MaxRetries": 2
  },
  "AnthropicOptions": {
    "FastModel": "claude-haiku-4-5-20251001",
    "StandardModel": "claude-sonnet-4-20250514",
    "PremiumModel": "claude-sonnet-4-20250514",
    "MaxRetries": 2
  }
}
```

**Provider switching:** Change `"LlmProvider"` to `"Gemini"` or `"Anthropic"`. No code changes required.

**API keys:** Set `GEMINI_API_KEY` (default provider) or `ANTHROPIC_API_KEY` environment variable. Keys must never be in appsettings.json. `UserSecretsId` is configured in the Web project for local development.

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
| `FactChecker.Infrastructure.Tests` | 154 | LLM provider clients tested with **recorded API response fixtures** (JSON files) — no live API calls. Provider-agnostic stages tested with mocked `ILlmClient`. Gemini: 18 client tests + 7 grounding parser tests. Anthropic: 12 client tests + 9 web search parser tests. Stages: 5 stage test classes. Common: 10 StructuredOutputParser tests. Channel transport, URL validator, HTTP source validator tested directly. |
| `FactChecker.Web.Tests` | 13 | Integration tests via `WebApplicationFactory`. Tests all three API endpoints. |

---

## What's Not Here (Intentional)

- No database — in-memory only (`InMemoryAnalysisStore`)
- No authentication
- No JavaScript build pipeline — HTMX + Pico CSS loaded from CDN
- No LLM framework (LangChain etc.) — direct HTTP calls to Gemini/Anthropic REST APIs via `ILlmClient`
- No score generation by LLM — scoring is pure deterministic math in `DefaultScoringEngine`
