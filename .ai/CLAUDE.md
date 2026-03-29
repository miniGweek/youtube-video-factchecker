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
Task progress is tracked in `.ai/memory/progress.json` — see Progress Tracking section below.
This keeps memory version-controlled, visible, and shared across all agents working on this project.

## What This Is

A web app that takes a YouTube URL and produces a structured summary, per-claim fact-checks with source citations, and a watch-worthiness assessment. Friends-scale, stateless, single-instance.

## Architecture Summary

**Stack:** C# / .NET 9 / ASP.NET Core + Razor Pages + HTMX + Pico CSS
**LLM (default):** Google Gemini API (2.5 Flash, 3 Flash, 2.5 Pro) with Google Search grounding
**LLM (alternate):** Anthropic Claude API (Sonnet + Haiku) — switchable via config
**Deployment:** Docker, single container

**Three projects, strict dependency direction:**

```
FactChecker.Web → FactChecker.Infrastructure → FactChecker.Core
FactChecker.Web → FactChecker.Core
```

- **Core** — Domain models, interfaces, pipeline orchestration, scoring. Zero external NuGet dependencies.
- **Infrastructure** — Implements Core interfaces. LLM provider clients (Gemini, Anthropic), provider-agnostic stages, YouTube transcript extraction, HTTP source validation.
- **Web** — Composition root. API endpoints, SSE streaming, Razor pages, DI wiring (including provider selection).

## Key Design Documents

- `.ai/design/architecture.md` — **Read this first before any task.** Authoritative architecture reference.
- `.ai/tasks/` — Task contracts. **Only load files with `Status: active`.**

## Task Management

- Task files in `.ai/tasks/` are numbered: `000-name.md`, `001-name.md`, etc.
- **Only load task files with `Status: active` in their header.**
- Never load complete/draft/superseded task files unless explicitly asked.
- Each task file is self-contained — don't cross-reference completed task files.

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
    Options/          # AnalysisOptions, StageModelOptions
  FactChecker.Infrastructure/
    YouTube/          # YouTubeTranscriptExtractor, YouTubeMetadataProvider
    LlmProviders/
      Shared/         # ILlmClient, ModelTier, LlmRequest/Response, StructuredOutputParser
      Gemini/         # GeminiLlmClient, GeminiGroundingParser, GeminiOptions
      Anthropic/      # AnthropicLlmClient, AnthropicWebSearchParser, AnthropicOptions
      Stages/         # Provider-agnostic: DomainDetectorStage, SummariserStage, ClaimVerifierStage, etc.
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

## LLM Provider Architecture

**Provider switching:** Set `"LlmProvider": "Gemini"` or `"Anthropic"` in appsettings.json. No code changes.

**Three-tier model routing:** Each stage is assigned a model tier (Fast/Standard/Premium) via `StageModelOptions`. Tiers map to provider-specific model strings.

```
Pipeline stages → ILlmClient (provider-agnostic) → GeminiLlmClient or AnthropicLlmClient
```

Stages contain prompts and response parsing. Provider clients handle transport and search mechanics.

| Tier | Gemini (default) | Anthropic (alternate) | Used By |
|---|---|---|---|
| Fast | gemini-2.5-flash | claude-haiku-4-5 | Domain detection, summarisation, assessment |
| Standard | gemini-3-flash | claude-sonnet-4 | Claim extraction |
| Premium | gemini-2.5-pro | claude-sonnet-4 | Claim verification (with search) |

**Search mechanics differ by provider but are abstracted away:**
- Gemini: Google Search grounding (`groundingMetadata` in response)
- Anthropic: web_search tool use (`tool_use`/`tool_result` blocks in response)

Both produce the same `SearchResultSource` records. Stages never know which provider is active.

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
7. **Never commit API keys.** `GEMINI_API_KEY` and `ANTHROPIC_API_KEY` from environment variables or user secrets.
8. **Warnings are errors.** Fix them, don't suppress them.
9. **`ILlmClient` lives in Infrastructure, not Core.** It is an implementation detail — Core interfaces are provider-agnostic.
10. **Stages are provider-agnostic.** Stage classes must not import any Gemini or Anthropic namespace.

## Configuration (appsettings.json)

```json
{
  "LlmProvider": "Gemini",
  "StageModelOptions": {
    "DomainDetection": "Fast",
    "Summarisation": "Fast",
    "ClaimExtraction": "Standard",
    "ClaimVerification": "Premium",
    "Assessment": "Fast"
  },
  "GeminiOptions": {
    "FastModel": "gemini-2.5-flash",
    "StandardModel": "gemini-3-flash",
    "PremiumModel": "gemini-2.5-pro",
    "EnableSearchGrounding": true
  },
  "AnthropicOptions": {
    "FastModel": "claude-haiku-4-5-20251001",
    "StandardModel": "claude-sonnet-4-20250514",
    "PremiumModel": "claude-sonnet-4-20250514",
    "MaxRetries": 2
  },
  "AnalysisOptions": {
    "MaxVideoDurationMinutes": 45,
    "MaxClaimsToVerify": 15,
    "MaxConcurrentVerifications": 4,
    "SourceValidationTimeoutSeconds": 5,
    "PipelineTimeoutSeconds": 600
  }
}
```

`MaxVideoDurationMinutes` and `MaxClaimsToVerify` are designed to be surfaced in UI later.

## Error Handling Philosophy

- **Fail hard** on core-value stages: transcript extraction, claim extraction, verification.
- **Degrade gracefully** on enrichment: domain detection defaults to General, assessment can be skipped, individual source validation failures don't block.
- Individual claim verification failure → mark that claim `Unverifiable`, continue pipeline.
- All pipeline exceptions caught and translated to `AnalysisFailedEvent`.

## Testing Approach

- **Domain models & scoring:** Unit tests, no mocks, hand-crafted inputs.
- **Pipeline orchestrator:** Unit tests with mocked Core interfaces.
- **LLM provider clients:** Unit tests with **recorded API response fixtures** per provider. No live API calls in CI.
- **Provider-specific parsers:** Unit tests with recorded response fixtures (Gemini grounding metadata, Anthropic tool-use blocks).
- **Stage implementations:** Unit tests with mocked `ILlmClient`. Tests are provider-agnostic.
- **API endpoints:** Integration tests with `WebApplicationFactory`.
- **Razor UI:** Manual verification only in v1.

## Task Execution Protocol

When picking up a task from `.ai/tasks/`:

1. **Read the architecture doc first** (`.ai/design/architecture.md`).
2. **Find the active task file** — only load files with `Status: active`.
3. **Read the specific task contract** — note dependencies, in-scope files, acceptance criteria.
4. **Implement code + tests together** — not code first, tests later.
5. **Verify:** `dotnet build` (zero warnings) → `dotnet test` (all pass) → diff is scoped to task.
6. **Update progress** — see Progress Tracking below. This is mandatory, not optional.
7. **Flag any deviations** from the architecture doc. If you need to change an interface, a model, or a module boundary — stop and propose, don't decide alone.

## Progress Tracking

**File:** `.ai/memory/progress.json` — **you MUST update this file after completing every task.**

This is a structured JSON file. After each task, update the matching task entry:

```bash
# Read current state
cat .ai/memory/progress.json

# Update the task entry with:
#   "status": "complete"
#   "commit": "<short hash of the commit>"
#   "date": "<YYYY-MM-DD>"
#   "tests": <total test count after this task>
#   "notes": "<any deviations, additions, or issues — null if none>"
```

**Rules:**
- Update the specific task object by matching its `"id"` field. Do not rewrite other entries.
- Update `"lastUpdated"` at the root level to the current ISO timestamp.
- If all tasks in a task set are complete, set the task set's `"status"` to `"complete"`.
- If a task is blocked or has issues, set its `"status"` to `"blocked"` and explain in `"notes"`.
- Allowed status values: `"pending"`, `"in-progress"`, `"complete"`, `"blocked"`, `"superseded"`.
- This update must happen in the same commit as the task implementation.

## Patterns to Follow

- **Records** for immutable value objects. **Class** for `AnalysisResult` (mutable aggregate with state transition methods).
- **IReadOnlyList<T>** for all collection properties on immutable types.
- **CancellationToken** on every async method.
- **IHttpClientFactory** for all HTTP clients (never `new HttpClient()`).
- **Options pattern** for configuration binding.
- **System.Text.Json** with strict settings for all serialisation.
- **System.Threading.Channels** for in-process event transport.
- **ILlmClient** for all LLM calls — stages never reference provider-specific types.

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
- Don't use LangChain or any LLM framework — direct API calls via `ILlmClient`.
- Don't over-engineer for scale — single instance, `ConcurrentDictionary`, no message broker.
- Don't let LLMs generate scores — scoring is deterministic domain logic in Core.
- Don't suppress compiler warnings — fix the underlying issue.
- Don't import Gemini/Anthropic namespaces in stage classes — stages depend only on `ILlmClient`.
- Don't duplicate prompts per provider — one prompt per stage, works with both.