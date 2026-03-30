# Task 004 — LLM Determinism + JSON Retry & Logging

Status: complete

## Goal

Two related reliability issues with claim verification:

1. **Non-deterministic LLM outputs** — No `temperature` parameter sent to Anthropic or Gemini APIs.
   Both default to temperature=1.0, causing the same video to produce different claim counts
   (e.g. 14 vs 15) and different credibility scores across runs.

2. **Silent JSON parse failures** — When claim verification returns non-JSON, `ClaimVerifierStage`
   catches `JsonException` silently (no logging, no retry) and returns Unverifiable immediately.
   There is no way to observe or diagnose this failure.

## Acceptance Criteria

- [x] All LLM API calls include `temperature=0` (Anthropic and Gemini)
- [x] `LlmRequest` record has a `Temperature` property defaulting to `0.0`
- [x] `ClaimVerifierStage` logs a Warning when JSON parsing fails on the first attempt
- [x] `ClaimVerifierStage` retries once with a JSON nudge in the system prompt
- [x] `ClaimVerifierStage` logs an Error if retry also fails, before returning Unverifiable
- [x] `./verify.sh` passes with zero warnings

## In-Scope Files

- `src/FactChecker.Infrastructure/LlmProviders/Common/LlmRequest.cs`
- `src/FactChecker.Infrastructure/Options/AnthropicOptions.cs`
- `src/FactChecker.Infrastructure/LlmProviders/Gemini/GeminiOptions.cs`
- `src/FactChecker.Infrastructure/LlmProviders/Anthropic/AnthropicLlmClient.cs`
- `src/FactChecker.Infrastructure/LlmProviders/Gemini/GeminiLlmClient.cs`
- `src/FactChecker.Infrastructure/Anthropic/AnthropicClientWrapper.cs`
- `src/FactChecker.Web/appsettings.json`
- `src/FactChecker.Infrastructure/LlmProviders/Stages/ClaimVerifierStage.cs`
- `tests/FactChecker.Infrastructure.Tests/LlmProviders/Stages/StubLlmClient.cs`
- `tests/FactChecker.Infrastructure.Tests/LlmProviders/Stages/ClaimVerifierStageTests.cs`
- `tests/FactChecker.Infrastructure.Tests/LlmProviders/Anthropic/AnthropicLlmClientTests.cs`
- `tests/FactChecker.Infrastructure.Tests/LlmProviders/Gemini/GeminiLlmClientTests.cs`

## Out of Scope

- Changes to other stage classes
- Changes to `ILlmClient` interface
- Changes to pipeline orchestration

## Dependencies

- Task sets 000–003 complete ✓
