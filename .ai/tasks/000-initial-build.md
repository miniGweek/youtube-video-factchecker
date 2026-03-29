# YouTube Video Fact-Checker — Task Contracts

**Version:** 1.0
**Date:** 2026-03-27
**Design Reference:** `.ai/design/architecture.md`

---

## Execution Order

```
Task 1 (scaffolding + domain models)
  ├── Task 2 (transcript extraction)  ────────────────────┐
  ├── Task 3 (LLM foundation) ──┬── Task 4 (LLM stages) ─┤── Task 7 (orchestrator) → Task 8 (API) → Task 9 (UI) → Task 10 (spike)
  │                              └── Task 5 (fact-check)  ─┘
  └── Task 6 (scoring engine)  ───────────────────────────┘
```

**Parallelisable after Task 1:** Tasks 2, 3, and 6.
**Parallelisable after Task 3:** Tasks 4 and 5.
**Sequential from Task 7 onward.**

---

## Task 1: Project Scaffolding, Domain Models, and Configuration

```
---
status: pending
branch: task/01-scaffolding-domain-models
---
```

### Objective

Create the solution structure, all domain models, interfaces, event types, configuration classes, and build infrastructure.

### Context

- Module/area: All three projects (Core, Infrastructure, Web) + test projects
- In-scope files:
  - `youtube-fact-checker.sln`
  - `Directory.Build.props`
  - `src/FactChecker.Core/` — all models, enums, interfaces, events, options
  - `src/FactChecker.Infrastructure/` — empty project with correct references
  - `src/FactChecker.Web/` — empty project with correct references
  - `tests/FactChecker.Core.Tests/` — model and enum tests
  - `tests/FactChecker.Infrastructure.Tests/` — empty project
  - `tests/FactChecker.Web.Tests/` — empty project
  - `Dockerfile` (placeholder — builds but doesn't run the full app yet)
  - `.gitignore`
- Out-of-scope: Any implementation classes in Infrastructure or Web
- Dependencies: None
- Stack: .NET 9, C# 13

### Acceptance Criteria

- [ ] Solution builds with zero errors and zero warnings
- [ ] `Directory.Build.props` enables `TreatWarningsAsErrors`, `Nullable`, `ImplicitUsings`
- [ ] All domain models from architecture doc section 3 are implemented as records/classes
- [ ] All interfaces from architecture doc section 4 are defined
- [ ] All event types from architecture doc section 5 are defined
- [ ] `AnalysisResult` enforces state transitions (methods throw if called in wrong state)
- [ ] `AnalysisOptions` and `AnthropicOptions` configuration classes are defined
- [ ] Dependency direction enforced: Core has no project references; Infrastructure references Core; Web references both
- [ ] Test projects reference their corresponding source projects
- [ ] Unit tests verify `AnalysisResult` state transition logic (valid transitions succeed, invalid throw)
- [ ] Unit tests verify enum coverage (no missing values in switch expressions)

### Constraints

- Core project must have zero NuGet package dependencies (no external packages)
- Use `IReadOnlyList<T>` for all collection properties on immutable types
- All enums must be exhaustively handled in any switch expression (enforced by compiler warnings-as-errors)
- Records for immutable value objects; class for `AnalysisResult` (mutable aggregate)

### Test Expectations

- Unit tests for: `AnalysisResult` state transitions (happy path: full lifecycle; error path: invalid transitions; edge: `AddFactCheck` called multiple times)
- Unit tests for: Model validation (e.g. `Claim.Importance` range if enforced, `VideoInfo` with null checks)
- Edge cases: Empty collections, null optionals, maximum importance values

### Reference

- Architecture doc sections 3, 4, 5, 11 (configuration)

---

## Task 2: Transcript Extraction

```
---
status: pending
branch: task/02-transcript-extraction
---
```

### Objective

Implement YouTube transcript extraction and video metadata retrieval using YoutubeExplode.

### Context

- Module/area: FactChecker.Infrastructure
- In-scope files:
  - `src/FactChecker.Infrastructure/YouTube/YouTubeTranscriptExtractor.cs`
  - `src/FactChecker.Infrastructure/YouTube/YouTubeMetadataProvider.cs`
  - `src/FactChecker.Infrastructure/YouTube/YouTubeUrlValidator.cs`
  - `src/FactChecker.Infrastructure/YouTube/TranscriptNotAvailableException.cs`
  - `tests/FactChecker.Infrastructure.Tests/YouTube/` — unit tests
- Out-of-scope: LLM integration, pipeline orchestration, API endpoints
- Dependencies: Task 1 (interfaces and models must exist)
- Stack: .NET 9, YoutubeExplode NuGet package

### Acceptance Criteria

- [ ] `YouTubeTranscriptExtractor` implements `ITranscriptExtractor`
- [ ] Tries manual captions first, falls back to auto-generated
- [ ] Sets `TranscriptQuality` correctly based on caption source
- [ ] Throws `TranscriptNotAvailableException` when no captions exist
- [ ] `YouTubeMetadataProvider` implements `IVideoMetadataProvider`
- [ ] Extracts title, channel name, duration, and thumbnail URL
- [ ] `YouTubeUrlValidator` validates YouTube URL formats (youtube.com/watch?v=, youtu.be/, etc.)
- [ ] Handles edge cases: private videos, age-restricted, non-existent video IDs
- [ ] All new code compiles with zero warnings
- [ ] Unit tests pass

### Constraints

- Follow the interface contracts from Core exactly
- Use `CancellationToken` throughout for cooperative cancellation
- YouTube API calls should use `IHttpClientFactory` for proper connection management
- Transcript text should be cleaned: remove timing markers, join into continuous text, normalise whitespace

### Test Expectations

- Unit tests for: URL validation (valid formats, invalid formats, edge cases like mobile URLs, shortened URLs, playlist URLs)
- Unit tests for: Transcript extraction with mocked HTTP responses (manual captions found, auto-only, no captions)
- Unit tests for: Metadata extraction with mocked responses
- Edge cases: Very long video metadata, special characters in titles, missing optional fields

### Reference

- Architecture doc sections 7.1, 12 (error handling for transcript stage)

---

## Task 3: LLM Integration Foundation

```
---
status: pending
branch: task/03-llm-foundation
---
```

### Objective

Build the shared Anthropic API client wrapper with retry logic, structured JSON output parsing, and model tier routing.

### Context

- Module/area: FactChecker.Infrastructure
- In-scope files:
  - `src/FactChecker.Infrastructure/Anthropic/AnthropicClientWrapper.cs`
  - `src/FactChecker.Infrastructure/Anthropic/ModelTier.cs`
  - `src/FactChecker.Infrastructure/Anthropic/AnthropicException.cs`
  - `src/FactChecker.Infrastructure/Anthropic/StructuredOutputParser.cs`
  - `tests/FactChecker.Infrastructure.Tests/Anthropic/` — unit tests
- Out-of-scope: Specific LLM stage implementations (Task 4), web search tool use (Task 5)
- Dependencies: Task 1 (AnthropicOptions, domain models)
- Stack: .NET 9, Anthropic .NET SDK (NuGet), Polly for retry

### Acceptance Criteria

- [ ] `AnthropicClientWrapper` provides a `SendAsync<T>` method that sends a prompt and deserialises the JSON response to `T`
- [ ] Supports `ModelTier.Fast` (Haiku) and `ModelTier.Standard` (Sonnet) routing based on `AnthropicOptions`
- [ ] Retry logic: exponential backoff on transient errors (429, 500, 503), configurable max retries
- [ ] On JSON parse failure: one automatic retry with a system prompt nudge to respond in valid JSON
- [ ] Structured output parser handles common LLM response quirks (markdown code fences around JSON, leading/trailing whitespace)
- [ ] Throws `AnthropicException` with clear message on non-transient failures (400, 401, 403)
- [ ] Respects `CancellationToken` throughout
- [ ] Uses `IHttpClientFactory` for connection management
- [ ] All new code compiles with zero warnings
- [ ] Unit tests pass

### Constraints

- The wrapper must not contain any prompt content — prompts belong in stage implementations (Task 4/5)
- Must support both simple completions and tool-use completions (needed by Task 5 for web search)
- JSON deserialisation should use `System.Text.Json` with strict settings (no missing members allowed)
- Log all API calls at Debug level (model used, token counts, latency) via `ILogger<T>`

### Test Expectations

- Unit tests for: Successful request/response cycle with mocked HTTP handler
- Unit tests for: Retry behaviour (transient error → retry → success; permanent error → throw)
- Unit tests for: JSON parsing (clean JSON, JSON in markdown fences, malformed JSON triggers retry)
- Unit tests for: Model tier routing (Fast → correct model string, Standard → correct model string)
- Unit tests for: Cancellation token respected
- Edge cases: Empty response body, response with unexpected fields (should be ignored), very large responses

### Reference

- Architecture doc sections 7.2, 11 (AnthropicOptions)

---

## Task 4: Pipeline Stage Implementations (LLM)

```
---
status: pending
branch: task/04-llm-pipeline-stages
---
```

### Objective

Implement the four LLM pipeline stages: domain detection, summarisation, claim extraction, and assessment generation. Each with its own prompt and structured output parsing.

### Context

- Module/area: FactChecker.Infrastructure
- In-scope files:
  - `src/FactChecker.Infrastructure/Anthropic/Stages/AnthropicDomainDetector.cs`
  - `src/FactChecker.Infrastructure/Anthropic/Stages/AnthropicSummariser.cs`
  - `src/FactChecker.Infrastructure/Anthropic/Stages/AnthropicClaimExtractor.cs`
  - `src/FactChecker.Infrastructure/Anthropic/Stages/AnthropicAssessmentGenerator.cs`
  - `src/FactChecker.Infrastructure/Anthropic/Prompts/` — prompt templates (as embedded resources or constants)
  - `tests/FactChecker.Infrastructure.Tests/Anthropic/Stages/` — unit tests
  - `tests/FactChecker.Infrastructure.Tests/Anthropic/Stages/Fixtures/` — recorded API response JSON files
- Out-of-scope: ClaimVerifier (Task 5 — requires web search tool), pipeline orchestration (Task 7)
- Dependencies: Task 3 (AnthropicClientWrapper)
- Stack: .NET 9, Anthropic API via wrapper from Task 3

### Acceptance Criteria

- [ ] `AnthropicDomainDetector` implements `IDomainDetector`, uses `ModelTier.Fast`
- [ ] Sends first ~1000 words of transcript for domain classification
- [ ] Returns one of the `ContentDomain` enum values
- [ ] `AnthropicSummariser` implements `ISummariser`, uses `ModelTier.Standard`
- [ ] Produces a thesis statement + 3-7 key points
- [ ] Prompt includes domain context to adjust summarisation style
- [ ] `AnthropicClaimExtractor` implements `IClaimExtractor`, uses `ModelTier.Standard`
- [ ] Extracts falsifiable factual claims only (Rule R1 — excludes opinions, speculation, rhetorical questions)
- [ ] Each claim includes surrounding context snippet and importance ranking (1-5)
- [ ] Respects `maxClaims` parameter; prioritises by centrality to thesis
- [ ] `AnthropicAssessmentGenerator` implements `IAssessmentGenerator`, uses `ModelTier.Fast`
- [ ] Produces watch recommendation, reasoning, information density assessment, and caveats
- [ ] Distinguishes "accurate but low-density" from "dense but unreliable" (Rule R6)
- [ ] All prompts instruct the model to respond in valid JSON matching the expected schema
- [ ] All new code compiles with zero warnings
- [ ] Unit tests pass using recorded API response fixtures

### Constraints

- Prompts should be clear, explicit, and include the expected JSON schema in the system message
- Each stage class should be stateless — all state flows through method parameters
- Domain detection prompt should be concise (this is a fast/cheap call)
- Claim extraction prompt is the most critical — invest in clarity around what constitutes a "claim" vs "opinion"
- Claim extraction must include domain-aware guidance (e.g. for health: "include specific statistics and dosage claims")

### Test Expectations

- Unit tests for: Each stage with 2-3 recorded API responses (happy path, edge cases)
- Unit tests for: Domain detector returns correct enum for each domain type
- Unit tests for: Claim extractor respects maxClaims limit
- Unit tests for: Claim extractor assigns importance scores
- Unit tests for: Summariser produces thesis + key points structure
- Unit tests for: Assessment generator handles various input combinations (all supported, all refuted, mixed)
- Edge cases: Very short transcript, transcript with no factual claims, transcript with excessive claims

### Reference

- Architecture doc sections 4, 7.2, 14 (rules R1, R6)

---

## Task 5: Fact-Check Engine

```
---
status: pending
branch: task/05-fact-check-engine
---
```

### Objective

Implement the claim verifier (with Anthropic web_search tool use) and the HTTP source URL validator.

### Context

- Module/area: FactChecker.Infrastructure
- In-scope files:
  - `src/FactChecker.Infrastructure/Anthropic/Stages/AnthropicClaimVerifier.cs`
  - `src/FactChecker.Infrastructure/Validation/HttpSourceValidator.cs`
  - `tests/FactChecker.Infrastructure.Tests/Anthropic/Stages/AnthropicClaimVerifierTests.cs`
  - `tests/FactChecker.Infrastructure.Tests/Validation/HttpSourceValidatorTests.cs`
  - `tests/FactChecker.Infrastructure.Tests/Anthropic/Stages/Fixtures/` — recorded responses
- Out-of-scope: Pipeline orchestration (Task 7), scoring (Task 6)
- Dependencies: Task 3 (AnthropicClientWrapper — must support tool-use responses)
- Stack: .NET 9, Anthropic API with web_search tool

### Acceptance Criteria

- [ ] `AnthropicClaimVerifier` implements `IClaimVerifier`, uses `ModelTier.Standard`
- [ ] Sends claim text + context snippet + summary + domain to the LLM with web_search tool enabled
- [ ] Prompt instructs model to: search for evidence, cite only pages actually retrieved, provide verdict and reasoning
- [ ] Parses tool-use response to extract verdict, confidence, reasoning, and source list
- [ ] Returns a `FactCheck` with populated `Sources` (URL, title, snippet from search results)
- [ ] Handles claims the model cannot verify — returns `Verdict.Unverifiable` with explanation
- [ ] Handles claims the model reclassifies as opinion — returns `Verdict.NotAClaim` (Rule R3)
- [ ] Every FactCheck has at least one source OR explicit "no reliable source found" reasoning (Rule R2)
- [ ] `HttpSourceValidator` implements `ISourceValidator`
- [ ] Sends HTTP HEAD request to source URL with configurable timeout
- [ ] Sets `IsAccessible = true` on 2xx response, `false` on any error/timeout
- [ ] Does not follow more than 3 redirects
- [ ] Uses `IHttpClientFactory`
- [ ] All new code compiles with zero warnings
- [ ] Unit tests pass

### Constraints

- The verifier prompt must explicitly instruct: "Only cite URLs from pages you retrieved via search. Do not generate URLs from memory."
- Domain-aware prompts: health claims should prompt for peer-reviewed sources; news claims for primary reporting
- Source validator must not throw — always returns a Source with IsAccessible set
- Source validator timeout: configurable, default 5 seconds
- ClaimVerifier must handle Anthropic API responses with interleaved text and tool_use/tool_result blocks

### Test Expectations

- Unit tests for: ClaimVerifier with recorded tool-use API responses (supported claim, refuted claim, unverifiable claim, opinion reclassification)
- Unit tests for: Response parsing with multiple sources per claim
- Unit tests for: HttpSourceValidator (200 OK, 404, timeout, redirect chain, connection refused)
- Edge cases: Claim with no searchable terms, source URL with non-standard characters, very slow source response

### Reference

- Architecture doc sections 4, 7.2, 7.3, 12, 14 (rules R2, R3)

---

## Task 6: Scoring Engine

```
---
status: pending
branch: task/06-scoring-engine
---
```

### Objective

Implement the deterministic scoring engine that calculates accuracy, source quality, verifiability, and aggregate scores.

### Context

- Module/area: FactChecker.Core
- In-scope files:
  - `src/FactChecker.Core/Scoring/DefaultScoringEngine.cs`
  - `tests/FactChecker.Core.Tests/Scoring/DefaultScoringEngineTests.cs`
- Out-of-scope: LLM calls, pipeline orchestration
- Dependencies: Task 1 (domain models only — Claim, FactCheck, Source, ScoreBreakdown, Verdict, Confidence, ContentDomain)
- Stack: .NET 9, pure C# (no external dependencies)

### Acceptance Criteria

- [ ] `DefaultScoringEngine` implements `IScoringEngine`
- [ ] `AccuracyScore` calculated as importance-weighted verdict scores per architecture doc section 10.1
- [ ] `AccuracyScore` includes worst-claim penalty: -15 per refuted claim with Importance ≥ 4, capped at -30 (Rule R4)
- [ ] Claims with `Verdict.NotAClaim` are excluded from all scoring (Rule R3)
- [ ] `SourceQualityScore` calculated as ratio of accessible sources
- [ ] `VerifiabilityScore` calculated as ratio of verifiable claims
- [ ] `AggregateScore` uses weighted formula: 0.60 accuracy + 0.20 source + 0.15 verifiability + 0.05 transcript bonus
- [ ] `ScoreMethod` string identifies the algorithm version (e.g. "v1.0-weighted")
- [ ] All scores clamped to 0-100 range
- [ ] All new code compiles with zero warnings
- [ ] Comprehensive unit tests pass

### Constraints

- Pure domain logic — no async, no I/O, no external dependencies
- All arithmetic must handle edge cases: division by zero (no claims, no sources), empty lists
- Weights should be defined as named constants, not magic numbers
- The transcript quality bonus requires knowing the TranscriptQuality — either pass it as an additional parameter or include it in the method signature

### Test Expectations

- Unit tests for: All claims supported → high score
- Unit tests for: All claims refuted → low score
- Unit tests for: Mixed verdicts → proportional score
- Unit tests for: Worst-claim penalty (1 high-importance refuted, 2 high-importance refuted, 3+ capped at -30)
- Unit tests for: NotAClaim exclusion (opinions don't affect scores)
- Unit tests for: Source quality with all accessible, none accessible, mixed
- Unit tests for: Verifiability with all verifiable, all unverifiable, mixed
- Unit tests for: Aggregate score weights sum correctly
- Unit tests for: Edge cases — no claims at all, single claim, all claims are NotAClaim, no sources on any fact-check
- Unit tests for: Score clamping (penalty doesn't push below 0)

### Reference

- Architecture doc section 10 (full scoring algorithm specification)

---

## Task 7: Pipeline Orchestrator

```
---
status: pending
branch: task/07-pipeline-orchestrator
---
```

### Objective

Implement the analysis pipeline orchestrator that coordinates all stages, manages parallelism, publishes events, and handles errors.

### Context

- Module/area: FactChecker.Core (orchestration logic), FactChecker.Infrastructure (event transport)
- In-scope files:
  - `src/FactChecker.Core/Pipeline/AnalysisPipeline.cs`
  - `src/FactChecker.Core/Pipeline/IAnalysisStore.cs` (in-memory analysis tracking)
  - `src/FactChecker.Infrastructure/Events/ChannelEventTransport.cs` (implements IAnalysisEventSink + IAnalysisEventStream)
  - `src/FactChecker.Infrastructure/Storage/InMemoryAnalysisStore.cs`
  - `tests/FactChecker.Core.Tests/Pipeline/AnalysisPipelineTests.cs`
  - `tests/FactChecker.Infrastructure.Tests/Events/ChannelEventTransportTests.cs`
- Out-of-scope: API endpoints (Task 8), UI (Task 9)
- Dependencies: Task 1 (all interfaces and models)
- Stack: .NET 9, System.Threading.Channels

### Acceptance Criteria

- [ ] `AnalysisPipeline` orchestrates the full execution flow per architecture doc section 6
- [ ] Stages execute in correct order with correct dependencies
- [ ] Domain detection completes before summarisation and claim extraction start
- [ ] Summarisation and claim extraction run in parallel after domain detection
- [ ] Claim verification runs with bounded parallelism (`MaxConcurrentVerifications` from options)
- [ ] Source URLs validated after each claim verification completes
- [ ] Scoring runs after all claims verified
- [ ] Assessment runs after scoring
- [ ] Each stage completion publishes the corresponding event via `IAnalysisEventSink`
- [ ] Pipeline respects `CancellationToken` and `PipelineTimeoutSeconds`
- [ ] Individual claim verification failure marks claim as `Unverifiable` — does not fail pipeline
- [ ] Domain detection failure defaults to `General` — does not fail pipeline
- [ ] Assessment failure skips assessment — does not fail pipeline
- [ ] Summarisation or claim extraction failure fails the pipeline with `AnalysisFailedEvent`
- [ ] `ChannelEventTransport` creates one channel per analysis ID
- [ ] `IAnalysisEventStream.SubscribeAsync` yields events as they are published
- [ ] Channel is completed when analysis finishes (success or failure)
- [ ] `InMemoryAnalysisStore` provides `ConcurrentDictionary`-backed storage for `AnalysisResult` by ID
- [ ] All new code compiles with zero warnings
- [ ] Unit tests pass

### Constraints

- Pipeline orchestration is in Core — it depends only on interfaces, not implementations
- Event transport is in Infrastructure — it's an implementation detail
- Use `Parallel.ForEachAsync` for claim verification parallelism
- Use `Task.WhenAll` for summary + claim extraction parallelism
- Pipeline must update `AnalysisResult` state as it progresses (via the store)
- All exceptions within the pipeline must be caught and translated to `AnalysisFailedEvent` — the pipeline must never throw to the caller

### Test Expectations

- Unit tests for: Full happy-path pipeline (all stages succeed, all events published in correct order)
- Unit tests for: Parallel execution (summary and claims start concurrently — verify via timing or call ordering)
- Unit tests for: Individual claim failure (one claim throws, others succeed, pipeline continues)
- Unit tests for: Domain detection failure (defaults to General, pipeline continues)
- Unit tests for: Summarisation failure (pipeline fails, AnalysisFailedEvent published)
- Unit tests for: Cancellation (token cancelled mid-pipeline, pipeline stops cleanly)
- Unit tests for: Timeout (pipeline exceeds timeout, fails with clear error)
- Unit tests for: ChannelEventTransport (publish → subscribe yields events; completion signal works)
- Edge cases: Zero claims extracted (pipeline should handle gracefully — skip verification, score as N/A)

### Reference

- Architecture doc sections 6, 5, 12 (error handling)

---

## Task 8: API Endpoints and SSE Streaming

```
---
status: pending
branch: task/08-api-endpoints-sse
---
```

### Objective

Implement the REST API endpoints and SSE streaming, wire up dependency injection, and configure the ASP.NET Core host.

### Context

- Module/area: FactChecker.Web
- In-scope files:
  - `src/FactChecker.Web/Program.cs` (DI wiring, configuration, middleware)
  - `src/FactChecker.Web/Controllers/AnalysisController.cs` (or minimal API endpoints)
  - `src/FactChecker.Web/Models/AnalyseRequest.cs` (request DTO)
  - `src/FactChecker.Web/appsettings.json` (non-secret configuration)
  - `src/FactChecker.Web/appsettings.Development.json`
  - `tests/FactChecker.Web.Tests/AnalysisEndpointTests.cs`
- Out-of-scope: Razor pages and HTMX (Task 9)
- Dependencies: Task 7 (pipeline, event transport, analysis store)
- Stack: .NET 9, ASP.NET Core

### Acceptance Criteria

- [ ] `POST /api/analyse` accepts `{ "url": "..." }`, validates URL format, returns `202 { "analysisId": "..." }`
- [ ] Returns 400 for invalid URLs, non-YouTube URLs, or missing URL field
- [ ] Starts pipeline execution on a background task after returning 202
- [ ] `GET /api/analyse/{id}/stream` returns `text/event-stream` with SSE events
- [ ] SSE events formatted as `event: {type}\ndata: {json}\n\n`
- [ ] SSE stream completes when analysis finishes (success or failure)
- [ ] `GET /api/analyse/{id}` returns full `AnalysisResult` as JSON (200 if complete, 202 if running, 404 if unknown)
- [ ] All dependencies registered in DI container (`Program.cs`)
- [ ] `AnalysisOptions` and `AnthropicOptions` bound from configuration
- [ ] API key loaded from environment variable `ANTHROPIC_API_KEY`
- [ ] CORS configured for local development
- [ ] All new code compiles with zero warnings
- [ ] Integration tests pass using `WebApplicationFactory`

### Constraints

- Use minimal API or controllers — either is acceptable, choose based on readability
- SSE endpoint must handle client disconnection gracefully (respect CancellationToken from `HttpContext.RequestAborted`)
- JSON serialisation should use `camelCase` property naming
- Do not add authentication/authorisation in v1
- Background pipeline execution via `Task.Run` — do not use `IHostedService` or queue for v1 (unnecessary complexity for single-instance)

### Test Expectations

- Integration tests for: POST with valid URL → 202 + analysisId
- Integration tests for: POST with invalid URL → 400
- Integration tests for: GET stream → receives SSE events (mock pipeline stages for predictable output)
- Integration tests for: GET by ID → 200/202/404 based on state
- Integration tests for: SSE event format validation (correct `event:` and `data:` lines)
- Edge cases: Concurrent analyses, very fast pipeline completion (SSE client connects after pipeline done)

### Reference

- Architecture doc sections 8, 9 (lifecycle management), 11 (configuration)

---

## Task 9: Web UI (Razor + HTMX)

```
---
status: pending
branch: task/09-web-ui
---
```

### Objective

Build the Razor Pages web interface with HTMX for progressive result rendering via SSE.

### Context

- Module/area: FactChecker.Web
- In-scope files:
  - `src/FactChecker.Web/Pages/Index.cshtml` + `.cshtml.cs` (home page with URL input)
  - `src/FactChecker.Web/Pages/Analysis.cshtml` + `.cshtml.cs` (results page)
  - `src/FactChecker.Web/Pages/Shared/` — partial views:
    - `_VideoHeader.cshtml`
    - `_TranscriptInfo.cshtml`
    - `_DomainBadge.cshtml`
    - `_Summary.cshtml`
    - `_ClaimsList.cshtml`
    - `_ClaimVerdict.cshtml`
    - `_Score.cshtml`
    - `_Assessment.cshtml`
    - `_Error.cshtml`
    - `_Loading.cshtml` (pipeline progress indicator)
  - `src/FactChecker.Web/wwwroot/` — static assets (HTMX JS from CDN link, minimal CSS overrides)
  - `src/FactChecker.Web/Pages/_Layout.cshtml` — shared layout with Pico CSS
- Out-of-scope: Custom JavaScript beyond HTMX, complex CSS, mobile-specific layouts
- Dependencies: Task 8 (working API + SSE endpoints)
- Stack: ASP.NET Core Razor Pages, HTMX (CDN), HTMX SSE extension, Pico CSS (CDN)

### Acceptance Criteria

- [ ] Home page (`/`) displays a clean URL input form with a submit button
- [ ] Form submission calls `POST /api/analyse` via Razor page handler, redirects to results page
- [ ] Results page (`/analysis/{id}`) connects to SSE endpoint via HTMX SSE extension
- [ ] Each SSE event type renders the corresponding partial view into the correct page section
- [ ] Progressive rendering: page starts with a loading state and fills in as events arrive
- [ ] `_VideoHeader` shows title, channel, duration, and thumbnail
- [ ] `_TranscriptInfo` shows word count and quality indicator (manual vs auto-generated)
- [ ] `_Summary` shows thesis and key points
- [ ] `_ClaimsList` shows extracted claims; `_ClaimVerdict` partials append as verifications complete
- [ ] Each verdict displays: claim text, verdict badge (colour-coded), confidence, reasoning, and source links
- [ ] Source links are clickable, open in new tab, and show accessibility indicator
- [ ] `_Score` shows aggregate score and breakdown
- [ ] `_Assessment` shows watch recommendation, reasoning, and caveats
- [ ] `_Error` shows clear error message when pipeline fails
- [ ] Layout uses Pico CSS from CDN; page is readable and clean without custom CSS
- [ ] Works in modern browsers (Chrome, Firefox, Safari)
- [ ] All new code compiles with zero warnings

### Constraints

- No npm, no bundler, no JavaScript build pipeline
- HTMX and Pico CSS loaded from CDN only
- Custom CSS limited to layout tweaks (grid/flex for results sections) — no design system
- Razor partials receive strongly-typed models matching the domain event payloads
- Verdict badges colour-coded: green (Supported), red (Refuted), amber (PartiallySupported), grey (Unverifiable)
- No client-side JavaScript beyond HTMX attribute configuration
- SSE connection should show a reconnection message if the connection drops

### Test Expectations

- Manual verification only for v1 (Razor + HTMX is thin enough to test by running)
- Verify: full happy path with a real video URL
- Verify: error state display (invalid URL, no captions available)
- Verify: progressive rendering looks correct as events arrive over time
- Verify: page is responsive and readable on both desktop and mobile viewport

### Reference

- Architecture doc section 9

---

## Task 10: End-to-End Validation (Spike 1)

```
---
status: pending
branch: task/10-e2e-validation-spike
---
```

### Objective

Validate the full pipeline against 5 real YouTube videos across content domains. Assess fact-check quality, source reliability, scoring credibility, and overall user experience.

### Context

- Module/area: Whole system
- In-scope files:
  - `.ai/design/spike-1-results.md` — documented findings
  - Test execution against live system
- Out-of-scope: Code changes (this task is evaluation only; findings feed into iteration)
- Dependencies: Tasks 1-9 (fully working system)
- Stack: Running application + manual evaluation

### Acceptance Criteria

- [ ] Test 5 real videos: 1 news, 1 science explainer, 1 finance, 1 health, 1 mixed/general
- [ ] For each video, evaluate and document:
  - Transcript quality: Was the transcript usable? Any critical errors?
  - Summary quality: Does the summary accurately capture the video's thesis and key points?
  - Claim extraction quality: Are extracted claims genuinely falsifiable? % opinions misclassified as claims?
  - Fact-check quality: Are verdicts defensible? Would a knowledgeable person agree?
  - Source quality: Are cited URLs real, accessible, and relevant to the claims?
  - Scoring quality: Does the aggregate score match intuitive quality assessment?
  - Assessment quality: Is the watch recommendation reasonable?
  - Performance: Wall-clock time from URL submission to final result
- [ ] Pass criteria (from problem statement spike definition):
  - ≥80% of extracted claims are genuinely falsifiable
  - ≥80% of fact-check verdicts are defensible when manually reviewed
  - ≥90% of cited source URLs are real and accessible
  - Fact-check quality doesn't collapse for any single domain
  - Scoring aligns with intuitive assessment on ≥4 of 5 videos
  - Pipeline completes within 90 seconds for a 15-minute video
- [ ] Results documented in `.ai/design/spike-1-results.md` with per-video breakdown
- [ ] Issues and improvement opportunities catalogued with severity

### Constraints

- Select videos that are representative: not cherry-picked for easy verification
- Videos should be 8-20 minutes in length (the common range)
- At least one video should have auto-generated captions to test that path
- At least one video should contain clearly false claims to test refutation capability
- Evaluation is manual but structured — use the criteria above as a rubric

### Test Expectations

- This IS the test — it validates assumptions A2, A3, A4, and A6 from the problem statement
- If pass criteria are not met, document specific failure modes and propose mitigations
- Spike 2 (scoring model credibility) is included in this task — evaluate scoring as part of the per-video assessment

### Reference

- Problem statement sections 4.3 (spikes), 5 (success criteria)
- Architecture doc section 13 (test strategy — end-to-end row)

---

_All task contracts reference `.ai/design/architecture.md` as the authoritative design document. Deviations discovered during execution should be flagged in the task's change summary and proposed as design amendments if they affect module boundaries or public contracts._