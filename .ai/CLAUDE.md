# CLAUDE.md — YouTube Video Fact-Checker

## Codebase Reference

For a file-by-file breakdown of every class, interface, and its role, read this first:

@../CODEBASE.md

## Documentation Maintenance

After creating or modifying any source file, update `CODEBASE.md` to reflect the change.
Only update the sections affected — do not rewrite unchanged sections.
If you add a new file, add it to the relevant table. If you rename or delete one, remove it.

## Memory

Save all memory files to `.ai/memory/` (in the project root) instead of the default Claude memory path.
The index file is `.ai/memory/MEMORY.md`. Use the same frontmatter format (name, description, type) and content conventions.
This keeps memory version-controlled, visible, and shared across all agents working on this project.

## What This Is

A web app that takes a YouTube URL and produces a structured summary, per-claim fact-checks with source citations, and a watch-worthiness assessment. Friends-scale, stateless, single-instance.

## Architecture Summary

**Stack:** C# / .NET 9 / ASP.NET Core + Razor Pages + HTMX + Pico CSS
**LLM:** Anthropic Claude API (Sonnet for critical stages, Haiku for cheap ones)
**Deployment:** Docker, single container

**Three projects, strict dependency direction:**

```
FactChecker.Web → FactChecker.Infrastructure → FactChecker.Core
FactChecker.Web → FactChecker.Core
```

- **Core** — Domain models, interfaces, pipeline orchestration, scoring. Zero external NuGet dependencies.
- **Infrastructure** — Implements Core interfaces. Anthropic SDK, YoutubeExplode, HTTP clients.
- **Web** — Composition root. API endpoints, SSE streaming, Razor pages, DI wiring.

## Key Design Documents

- `.ai/design/architecture.md` — **Read this first before any task.** Authoritative architecture reference.
- `.ai/tasks/task-contracts.md` — Task breakdown with acceptance criteria and test expectations.

## Build & Run

```bash
dotnet build youtube-fact-checker.sln          # Must produce zero errors AND zero warnings
dotnet test                                     # All tests must pass before any PR
dotnet run --project src/FactChecker.Web        # Runs on https://localhost:5001
```

**Warnings are errors.** `Directory.Build.props` enforces `TreatWarningsAsErrors`. Fix warnings, don't suppress them.

## Project Layout

```
src/
  FactChecker.Core/
    Models/           # Records: VideoInfo, Transcript, Claim, FactCheck, Source, etc.
    Enums/            # Verdict, Confidence, ContentDomain, AnalysisStatus, etc.
    Interfaces/       # ITranscriptExtractor, ISummariser, IClaimVerifier, etc.
    Events/           # AnalysisEvent hierarchy (one record per pipeline stage)
    Pipeline/         # AnalysisPipeline orchestrator
    Scoring/          # DefaultScoringEngine (pure deterministic logic)
  FactChecker.Infrastructure/
    YouTube/          # YouTubeTranscriptExtractor, YouTubeMetadataProvider
    Anthropic/        # AnthropicClientWrapper, model tier routing
      Stages/         # DomainDetector, Summariser, ClaimExtractor, ClaimVerifier, AssessmentGenerator
      Prompts/        # Prompt templates
    Validation/       # HttpSourceValidator
    Events/           # ChannelEventTransport (System.Threading.Channels)
    Storage/          # InMemoryAnalysisStore
  FactChecker.Web/
    Controllers/      # AnalysisController (or minimal API endpoints)
    Pages/            # Razor: Index (URL input), Analysis (results + SSE)
      Shared/         # Partials: _VideoHeader, _Summary, _ClaimVerdict, _Score, etc.
    wwwroot/          # Static assets (HTMX + Pico CSS from CDN)
tests/
  FactChecker.Core.Tests/
  FactChecker.Infrastructure.Tests/
  FactChecker.Web.Tests/
```

## Pipeline Flow

```
URL → Validate → Extract Transcript → Detect Domain ─┬─ Summarise ────────┐
                                                      └─ Extract Claims ───┤
                                                                           ▼
                                              Verify Claims (parallel, max 4 concurrent)
                                                           │
                                                    Validate Source URLs
                                                           │
                                                    Score (deterministic)
                                                           │
                                                    Assess (LLM synthesis)
```

- Summarise + Extract Claims run **in parallel** (neither depends on the other).
- Claim verification is **embarrassingly parallel** — bounded by `MaxConcurrentVerifications`.
- Events stream to the client via SSE as each stage completes.

## API Endpoints

```
POST /api/analyse          → 202 { "analysisId": "guid" }
GET  /api/analyse/{id}/stream  → text/event-stream (SSE with JSON events)
GET  /api/analyse/{id}     → 200 (complete) | 202 (running) | 404 (unknown)
```

## Critical Rules (Non-Negotiable)

1. **Core has zero NuGet dependencies.** Models, interfaces, scoring — all pure C#.
2. **Scoring is deterministic, not LLM-generated.** The `IScoringEngine` is pure computation. LLMs do not score their own work.
3. **Every FactCheck must cite sources or explicitly state none found.** No silent gaps.
4. **Claims must be falsifiable assertions.** Opinions, speculation, rhetorical questions are excluded.
5. **Aggregate score penalises worst claims, not just average.** One fabricated stat tanks the score.
6. **Source URLs must be validated** with HTTP HEAD requests. Mark `IsAccessible` true/false.
7. **Never commit API keys.** `ANTHROPIC_API_KEY` comes from environment variables or user secrets.
8. **Warnings are errors.** Fix them, don't suppress them.

## Configuration (appsettings.json)

```json
{
  "AnalysisOptions": {
    "MaxVideoDurationMinutes": 45,
    "MaxClaimsToVerify": 15,
    "MaxConcurrentVerifications": 4,
    "SourceValidationTimeoutSeconds": 5,
    "PipelineTimeoutSeconds": 600
  },
  "AnthropicOptions": {
    "FastModel": "claude-haiku-4-5-20251001",
    "StandardModel": "claude-sonnet-4-20250514",
    "MaxRetries": 2
  }
}
```

`MaxVideoDurationMinutes` and `MaxClaimsToVerify` are designed to be surfaced in UI later.

## LLM Model Tiers

| Tier | Model | Used By |
|---|---|---|
| Fast | Haiku | Domain detection, assessment generation |
| Standard | Sonnet | Summarisation, claim extraction, fact verification (with web_search) |

## Error Handling Philosophy

- **Fail hard** on core-value stages: transcript extraction, claim extraction, verification.
- **Degrade gracefully** on enrichment: domain detection defaults to General, assessment can be skipped, individual source validation failures don't block.
- Individual claim verification failure → mark that claim `Unverifiable`, continue pipeline.
- All pipeline exceptions caught and translated to `AnalysisFailedEvent`.

## Testing Approach

- **Domain models & scoring:** Unit tests, no mocks, hand-crafted inputs.
- **Pipeline orchestrator:** Unit tests with mocked interfaces.
- **LLM integrations:** Unit tests with **recorded API response fixtures** (JSON files in test project). No live API calls in CI.
- **API endpoints:** Integration tests with `WebApplicationFactory`.
- **Razor UI:** Manual verification only in v1.

## Task Execution Protocol

When picking up a task from `.ai/tasks/task-contracts.md`:

1. **Read the architecture doc first** (`.ai/design/architecture.md`).
2. **Read the specific task contract** — note dependencies, in-scope files, acceptance criteria.
3. **Implement code + tests together** — not code first, tests later.
4. **Verify:** `dotnet build` (zero warnings) → `dotnet test` (all pass) → diff is scoped to task.
5. **Flag any deviations** from the architecture doc. If you need to change an interface, a model, or a module boundary — stop and propose, don't decide alone.

### Task Dependency Order

```
Task 1 (scaffolding)
  ├── Task 2 (transcript)  ──────────────────────┐
  ├── Task 3 (LLM foundation) ─┬── Task 4 ──────┤── Task 7 → Task 8 → Task 9 → Task 10
  │                             └── Task 5 ──────┘
  └── Task 6 (scoring) ─────────────────────────┘
```

Tasks 2, 3, 6 can execute in parallel after Task 1.
Tasks 4, 5 can execute in parallel after Task 3.

## Patterns to Follow

- **Records** for immutable value objects. **Class** for `AnalysisResult` (mutable aggregate with state transition methods).
- **IReadOnlyList<T>** for all collection properties on immutable types.
- **CancellationToken** on every async method.
- **IHttpClientFactory** for all HTTP clients (never `new HttpClient()`).
- **Options pattern** for configuration binding.
- **System.Text.Json** with strict settings for all serialisation.
- **System.Threading.Channels** for in-process event transport.

## Git Commit Convention

Two identities are used in this repo:

| Who | Author name | Author email |
|-----|-------------|--------------|
| Claude Code | `Rahul Sarkar via Claude Code` | `rahul.sarkar-claudecode@gmail.com` |
| Human (Rahul) | `Rahul Sarkar` | `dedlok@gmail.com` |

**When Claude Code makes a commit**, always override the author explicitly:

```bash
git -c user.name="Rahul Sarkar via Claude Code" \
    -c user.email="rahul.sarkar-claudecode@gmail.com" \
    commit -m "..."
```

Human commits use the global git config (`dedlok@gmail.com`) unchanged.

## What NOT to Do

- Don't add persistence/database — v1 is stateless, in-memory only.
- Don't add authentication — friends-scale, no auth needed.
- Don't add a JavaScript build pipeline — HTMX + Pico CSS from CDN only.
- Don't use LangChain or any LLM framework — direct Anthropic API calls via SDK.
- Don't over-engineer for scale — single instance, `ConcurrentDictionary`, no message broker.
- Don't let LLMs generate scores — scoring is deterministic domain logic in Core.
- Don't suppress compiler warnings — fix the underlying issue.