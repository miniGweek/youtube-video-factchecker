# Task Set 001: Multi-Provider LLM Integration

**Status:** complete
**Date:** 2026-03-29
**Design Reference:** `.ai/design/architecture.md` (v1.1, sections 7.2, 11)
**Predecessor:** `000-initial-build.md` (complete)
**Motivation:** Anthropic API costs ~$0.65-1.00/analysis (dominated by Sonnet + web_search tool use for claim verification). Gemini with Google Search grounding reduces this to ~$0.08-0.12/analysis — a 10x cost reduction.

---

## Execution Order

```
Task 1 (ILlmClient abstraction + shared types)
  ├── Task 2 (Gemini provider client + grounding parser)
  ├── Task 3 (Anthropic provider client + refactor existing wrapper)
  └── Task 4 (provider-agnostic stage refactor)
        └── Task 5 (DI wiring + config + provider switching)
              └── Task 6 (quality validation — rerun Spike 1 with Gemini)
```

**Tasks 2 and 3 can run in parallel** after Task 1.
**Task 4 depends on Task 1** (needs `ILlmClient` interface) but NOT on Tasks 2/3 (stages are coded against the interface, not implementations).
**Task 5** wires everything together.
**Task 6** validates quality.

---

## Task 1: Provider Client Abstraction and Shared Types

```
---
status: pending
branch: task/011-llm-client-abstraction
---
```

### Objective

Introduce the `ILlmClient` interface, shared request/response types, `ModelTier` enum (expanded to three tiers), and `StageModelOptions` configuration class.

### Context

- Module/area: FactChecker.Infrastructure
- In-scope files:
  - `src/FactChecker.Infrastructure/LlmProviders/Shared/ILlmClient.cs`
  - `src/FactChecker.Infrastructure/LlmProviders/Shared/ModelTier.cs`
  - `src/FactChecker.Infrastructure/LlmProviders/Shared/LlmRequest.cs`
  - `src/FactChecker.Infrastructure/LlmProviders/Shared/LlmResponse.cs`
  - `src/FactChecker.Infrastructure/LlmProviders/Shared/LlmSearchResponse.cs`
  - `src/FactChecker.Infrastructure/LlmProviders/Shared/SearchResultSource.cs`
  - `src/FactChecker.Infrastructure/LlmProviders/Shared/TokenUsage.cs`
  - `src/FactChecker.Infrastructure/LlmProviders/Shared/StructuredOutputParser.cs` (moved from Anthropic-specific, now shared)
  - `src/FactChecker.Core/Configuration/StageModelOptions.cs` (or wherever config classes live)
- Out-of-scope: Provider implementations, stage refactoring
- Dependencies: None — purely additive
- Stack: .NET 9, C# 13

### Acceptance Criteria

- [ ] `ILlmClient` interface defined with `CompleteAsync` and `CompleteWithSearchAsync` methods per architecture doc 7.2.1
- [ ] `ModelTier` enum has three values: `Fast`, `Standard`, `Premium`
- [ ] All shared types defined as records: `LlmRequest`, `LlmResponse`, `LlmSearchResponse`, `SearchResultSource`, `TokenUsage`
- [ ] `StageModelOptions` class defined with configurable tier per stage (defaults per architecture doc 7.2.6)
- [ ] `StructuredOutputParser` extracted as shared utility (handles markdown fences, whitespace, JSON parsing quirks)
- [ ] Existing code still compiles and all existing tests pass (this is purely additive)
- [ ] All new code compiles with zero warnings

### Constraints

- `ILlmClient` lives in Infrastructure, NOT Core — it is an implementation concern
- `StageModelOptions` lives alongside other configuration classes (Core or wherever `AnalysisOptions` lives)
- Do not modify any existing files in this task — only add new files
- The old `ModelTier` enum (if it exists with 2 values) will be replaced in Task 3/4 — for now, add the new one in the new namespace without removing the old one

### Test Expectations

- Unit tests for: `StructuredOutputParser` (clean JSON, markdown-fenced JSON, whitespace, malformed input)
- No other tests needed — these are types and interfaces

### Reference

- Architecture doc section 7.2.1

---

## Task 2: Gemini Provider Client

```
---
status: pending
branch: task/012-gemini-provider
---
```

### Objective

Implement `GeminiLlmClient` (the `ILlmClient` implementation for Google Gemini API) and `GeminiGroundingParser` for extracting sources from Google Search grounding responses.

### Context

- Module/area: FactChecker.Infrastructure
- In-scope files:
  - `src/FactChecker.Infrastructure/LlmProviders/Gemini/GeminiLlmClient.cs`
  - `src/FactChecker.Infrastructure/LlmProviders/Gemini/GeminiOptions.cs`
  - `src/FactChecker.Infrastructure/LlmProviders/Gemini/GeminiGroundingParser.cs`
  - `src/FactChecker.Infrastructure/LlmProviders/Gemini/GeminiApiException.cs`
  - `tests/FactChecker.Infrastructure.Tests/LlmProviders/Gemini/GeminiLlmClientTests.cs`
  - `tests/FactChecker.Infrastructure.Tests/LlmProviders/Gemini/GeminiGroundingParserTests.cs`
  - `tests/FactChecker.Infrastructure.Tests/LlmProviders/Gemini/Fixtures/` — recorded Gemini API response JSON
- Out-of-scope: Anthropic client, stage implementations, DI wiring
- Dependencies: Task 1 (`ILlmClient`, shared types)
- Stack: .NET 9, Google Gemini REST API (direct HTTP — no official .NET SDK required), `System.Text.Json`

### Acceptance Criteria

- [ ] `GeminiLlmClient` implements `ILlmClient`
- [ ] `CompleteAsync` sends `generateContent` POST request to `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`
- [ ] Maps `ModelTier` to model string via `GeminiOptions` (Fast/Standard/Premium → configurable model names)
- [ ] Parses Gemini response: extracts text from `candidates[0].content.parts[0].text`
- [ ] Extracts `TokenUsage` from `usageMetadata` (promptTokenCount, candidatesTokenCount)
- [ ] `CompleteWithSearchAsync` adds `tools: [{ "google_search": {} }]` to the request
- [ ] `GeminiGroundingParser` extracts sources from `candidates[0].groundingMetadata.groundingChunks` (each has `web.uri` and `web.title`)
- [ ] `GeminiGroundingParser` maps `groundingSupports` segments to snippets (text linked to source indices)
- [ ] Handles responses with no grounding metadata (model decided not to search) — returns empty sources list
- [ ] Retry with exponential backoff on 429, 500, 503 errors
- [ ] On JSON parse failure: one retry with system prompt nudge for valid JSON
- [ ] Respects `CancellationToken`
- [ ] Uses `IHttpClientFactory`
- [ ] Logs API calls at Debug level (model, token counts, latency, grounding query count)
- [ ] All new code compiles with zero warnings
- [ ] Unit tests pass with recorded response fixtures

### Constraints

- Use direct HTTP calls to Gemini REST API — no Google Cloud SDK needed
- API key passed as query parameter: `?key={apiKey}` (standard for Gemini Developer API)
- System prompt goes in `systemInstruction.parts[0].text` in the Gemini request schema
- User prompt goes in `contents[0].parts[0].text`
- JSON deserialisation via `System.Text.Json` with `JsonNamingPolicy.CamelCase`
- Must handle Gemini's response structure which differs significantly from Anthropic's (nested candidates → content → parts)

### Test Expectations

- Unit tests for: `GeminiLlmClient.CompleteAsync` with mocked HTTP handler (success, parse, retry on 429)
- Unit tests for: `GeminiLlmClient.CompleteWithSearchAsync` with mocked grounded response
- Unit tests for: `GeminiGroundingParser` with real Gemini grounding metadata fixtures (multiple sources, single source, no grounding, partial grounding)
- Unit tests for: Model tier routing (Fast → correct model string)
- Unit tests for: Error handling (400 bad request, 403 forbidden, timeout)
- Edge cases: Response with empty candidates, response with no text part, grounding with zero chunks

### Reference

- Architecture doc sections 7.2.1, 7.2.2, 7.2.5
- Gemini API docs: `https://ai.google.dev/gemini-api/docs`

---

## Task 3: Anthropic Provider Client (Refactor)

```
---
status: pending
branch: task/013-anthropic-provider-refactor
---
```

### Objective

Wrap the existing `AnthropicClientWrapper` into `AnthropicLlmClient` implementing `ILlmClient`. Extract `AnthropicWebSearchParser`.

### Context

- Module/area: FactChecker.Infrastructure
- In-scope files:
  - `src/FactChecker.Infrastructure/LlmProviders/Anthropic/AnthropicLlmClient.cs` (new — wraps existing functionality)
  - `src/FactChecker.Infrastructure/LlmProviders/Anthropic/AnthropicWebSearchParser.cs` (new — extracted from existing verifier)
  - `src/FactChecker.Infrastructure/LlmProviders/Anthropic/AnthropicOptions.cs` (updated — add Premium model)
  - `tests/FactChecker.Infrastructure.Tests/LlmProviders/Anthropic/AnthropicLlmClientTests.cs`
  - `tests/FactChecker.Infrastructure.Tests/LlmProviders/Anthropic/AnthropicWebSearchParserTests.cs`
- Out-of-scope: Stage refactoring (Task 4), Gemini client (Task 2), DI wiring (Task 5)
- Dependencies: Task 1 (`ILlmClient`, shared types)
- Stack: .NET 9, existing Anthropic SDK or HTTP client

### Acceptance Criteria

- [ ] `AnthropicLlmClient` implements `ILlmClient`
- [ ] `CompleteAsync` delegates to existing Anthropic API call logic (reuse or wrap `AnthropicClientWrapper`)
- [ ] `CompleteWithSearchAsync` sends request with `tools: [{ type: "web_search_20250305", name: "web_search" }]`
- [ ] `AnthropicWebSearchParser` extracts `SearchResultSource` records from interleaved `tool_use`/`tool_result` content blocks
- [ ] Maps `ModelTier` (Fast/Standard/Premium) to model strings via updated `AnthropicOptions`
- [ ] `AnthropicOptions` updated: adds `PremiumModel`
- [ ] Retry with exponential backoff on 429, 500, 503 errors
- [ ] Existing Anthropic tests still pass
- [ ] All new code compiles with zero warnings
- [ ] New unit tests pass

### Constraints

- The existing `AnthropicClientWrapper` can be refactored, wrapped, or replaced — whatever is cleanest
- If Anthropic rate limits are hit (30k tokens/min), reduce `MaxConcurrentVerifications` in config as a stopgap — rate limiting can be added later as a separate task

### Test Expectations

- Unit tests for: `AnthropicLlmClient.CompleteAsync` with mocked HTTP handler
- Unit tests for: `AnthropicLlmClient.CompleteWithSearchAsync` with recorded tool-use responses
- Unit tests for: `AnthropicWebSearchParser` (multiple sources, single source, no search results, mixed text and tool blocks)
- Unit tests for: Retry behaviour (429 → backoff → retry → success; 400 → throw)
- Edge cases: Empty response, cancellation token respected, unexpected response fields

### Reference

- Architecture doc section 7.2.2

---

## Task 4: Provider-Agnostic Stage Refactor

```
---
status: pending
branch: task/014-stage-refactor
---
```

### Objective

Refactor all five LLM stage implementations to use `ILlmClient` instead of the Anthropic-specific wrapper. Move stages from `Anthropic/Stages/` to `LlmProviders/Stages/`. Stages become provider-agnostic.

### Context

- Module/area: FactChecker.Infrastructure
- In-scope files:
  - `src/FactChecker.Infrastructure/LlmProviders/Stages/DomainDetectorStage.cs` (new, replaces `AnthropicDomainDetector`)
  - `src/FactChecker.Infrastructure/LlmProviders/Stages/SummariserStage.cs` (new, replaces `AnthropicSummariser`)
  - `src/FactChecker.Infrastructure/LlmProviders/Stages/ClaimExtractorStage.cs` (new, replaces `AnthropicClaimExtractor`)
  - `src/FactChecker.Infrastructure/LlmProviders/Stages/ClaimVerifierStage.cs` (new, replaces `AnthropicClaimVerifier`)
  - `src/FactChecker.Infrastructure/LlmProviders/Stages/AssessmentGeneratorStage.cs` (new, replaces `AnthropicAssessmentGenerator`)
  - `tests/FactChecker.Infrastructure.Tests/LlmProviders/Stages/` — unit tests for all stages
  - Old `Anthropic/Stages/` files — delete after migration
- Out-of-scope: Provider clients (Tasks 2, 3), DI wiring (Task 5)
- Dependencies: Task 1 (`ILlmClient`, `LlmRequest`, `StageModelOptions`)
- Stack: .NET 9

### Acceptance Criteria

- [ ] All five stages implement their Core interfaces (`IDomainDetector`, `ISummariser`, `IClaimExtractor`, `IClaimVerifier`, `IAssessmentGenerator`)
- [ ] All stages depend on `ILlmClient` — not on any Anthropic or Gemini-specific type
- [ ] Each stage reads its model tier from `StageModelOptions` (injected via options pattern)
- [ ] `ClaimVerifierStage` uses `ILlmClient.CompleteWithSearchAsync`; all others use `CompleteAsync`
- [ ] `ClaimVerifierStage` maps `SearchResultSource` records to domain `Source` records
- [ ] Prompt content is preserved from the existing Anthropic implementations (same prompts work with both providers)
- [ ] JSON response parsing logic preserved (parse verdict, claims, summary, etc. from response content)
- [ ] Old `Anthropic/Stages/` files deleted
- [ ] All existing stage tests migrated to test against new stage classes with mocked `ILlmClient`
- [ ] All new code compiles with zero warnings
- [ ] All tests pass

### Constraints

- Stages must be stateless — all state flows through method parameters and injected options
- Prompts remain the same — do NOT rewrite prompts in this task (both providers consume identical instructions)
- Stage classes should not import any Anthropic or Gemini namespace — only `LlmProviders.Shared`
- Each stage constructs an `LlmRequest` with the appropriate `StageId` string for logging

### Test Expectations

- Unit tests for: Each stage with mocked `ILlmClient` returning pre-built `LlmResponse`/`LlmSearchResponse`
- Unit tests for: `ClaimVerifierStage` correctly maps `SearchResultSource` → `Source` records
- Unit tests for: Each stage uses the correct `ModelTier` from `StageModelOptions`
- Unit tests for: Error handling (LLM returns unparseable JSON, empty response, timeout)
- Tests should be structured so adding a new provider requires zero test changes (tests mock `ILlmClient`, not provider)
- Edge cases: Same as existing stage tests (short transcript, no claims, all refuted, etc.)

### Reference

- Architecture doc section 7.2.3
- Existing stage implementations for prompt content and response parsing logic

---

## Task 5: DI Wiring, Configuration, and Provider Switching

```
---
status: pending
branch: task/015-di-wiring-provider-switch
---
```

### Objective

Wire up DI to support provider switching via `appsettings.json`. Register the correct `ILlmClient` based on config. Update `Program.cs` and configuration files.

### Context

- Module/area: FactChecker.Web, FactChecker.Infrastructure
- In-scope files:
  - `src/FactChecker.Web/Program.cs` (updated DI registration)
  - `src/FactChecker.Infrastructure/LlmProviders/ServiceCollectionExtensions.cs` (new — `AddLlmProvider` extension method)
  - `src/FactChecker.Web/appsettings.json` (updated with new config shape)
  - `src/FactChecker.Web/appsettings.Development.json` (updated)
  - `tests/FactChecker.Web.Tests/ProviderRegistrationTests.cs`
- Out-of-scope: Provider implementations (Tasks 2, 3), stage implementations (Task 4)
- Dependencies: Tasks 1, 2, 3, 4 (all must be complete)
- Stack: .NET 9, ASP.NET Core DI

### Acceptance Criteria

- [ ] `AddLlmProvider(IConfiguration)` extension method reads `LlmProvider` config value
- [ ] `"Gemini"` → registers `GeminiLlmClient` as `ILlmClient` (singleton), binds `GeminiOptions`
- [ ] `"Anthropic"` → registers `AnthropicLlmClient` as `ILlmClient` (singleton), binds `AnthropicOptions`
- [ ] Both paths register `StageModelOptions` and all five stage implementations
- [ ] Unknown provider value throws `InvalidOperationException` with clear message
- [ ] `appsettings.json` updated with full config shape per architecture doc section 11
- [ ] API key env vars documented: `GEMINI_API_KEY`, `ANTHROPIC_API_KEY`
- [ ] Default provider set to `"Gemini"` in appsettings
- [ ] Old Anthropic-specific DI registrations removed from `Program.cs`
- [ ] Application starts and runs with Gemini provider
- [ ] Application starts and runs with Anthropic provider (switch config, verify)
- [ ] All new code compiles with zero warnings
- [ ] All existing tests pass (WebApplicationFactory tests may need config updates)

### Constraints

- Provider switching is whole-pipeline — all stages use the same `ILlmClient` instance
- `ILlmClient` registered as singleton (one instance per process, shared across all stages)
- `GeminiGroundingParser` and `AnthropicWebSearchParser` only registered with their respective providers
- `GeminiOptions.ApiKey` must NOT be in appsettings — env var only

### Test Expectations

- Integration tests for: Gemini provider resolves correctly from DI
- Integration tests for: Anthropic provider resolves correctly from DI
- Integration tests for: Unknown provider throws
- Integration tests for: Full pipeline runs with mocked `ILlmClient` (verifies all stages resolve and execute)
- Integration tests for: `StageModelOptions` binds correctly from config

### Reference

- Architecture doc sections 7.2, 11

---

## Task 6: Quality Validation — Gemini Provider

```
---
status: pending
branch: task/016-gemini-quality-validation
---
```

### Objective

Rerun the Spike 1 evaluation using the Gemini provider. Compare verdict quality, source reliability, and scoring accuracy against the existing Anthropic baseline.

### Context

- Module/area: Whole system
- In-scope files:
  - `.ai/design/spike-2-gemini-results.md` — documented findings
  - Comparison against `.ai/design/spike-1-results.md` (Anthropic baseline)
- Out-of-scope: Code changes (evaluation only — findings feed into iteration)
- Dependencies: Tasks 1-5 (fully working system with Gemini provider)
- Stack: Running application with `LlmProvider: Gemini`

### Acceptance Criteria

- [ ] Rerun same 5 videos from Spike 1 (or as close as possible) with Gemini provider
- [ ] For each video, evaluate and document:
  - Verdict quality: Are verdicts as defensible as the Anthropic baseline?
  - Source quality: Are Google Search grounding sources real, accessible, and relevant?
  - Source richness: Does Gemini grounding return sufficient sources per claim? (Compare count and quality vs Anthropic web search)
  - Scoring alignment: Does the aggregate score still match intuitive assessment?
  - Performance: Wall-clock time comparison (Gemini vs Anthropic)
  - Cost: Actual token usage and cost per analysis
- [ ] Side-by-side comparison table: Gemini vs Anthropic for each evaluation criterion
- [ ] Pass criteria (same as Spike 1):
  - ≥80% of fact-check verdicts defensible
  - ≥90% of cited source URLs real and accessible
  - Quality doesn't collapse for any single domain
  - Pipeline completes within 90 seconds
- [ ] If any domain shows quality degradation with Gemini, document which stages/tiers to adjust
- [ ] Results documented in `.ai/design/spike-2-gemini-results.md`

### Constraints

- Use the same videos as Spike 1 for direct comparison
- Run with default tier assignment (Fast/Fast/Standard/Premium/Fast)
- If Premium (2.5 Pro) verification quality is insufficient, test with Standard (3 Flash) for verification and document the difference
- Record actual API costs from Gemini billing dashboard

### Test Expectations

- This IS the test — validates that the Gemini provider meets quality bar
- If quality is lower but acceptable, document the trade-off (cost vs quality)
- If quality is unacceptable for specific domains, recommend tier adjustments or per-domain provider mixing as future work

### Reference

- Architecture doc section 16 (cost estimates)
- `.ai/design/spike-1-results.md` (Anthropic baseline)

---

_All task contracts reference `.ai/design/architecture.md` v1.1 as the authoritative design document. Deviations discovered during execution should be flagged in the task's change summary._