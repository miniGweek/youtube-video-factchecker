---
Status: active
---

# Task 007: Codebase Audit Cleanup (P0 + P1)

## Goal

Address 11 findings from codebase audit — dead code removal, correctness fixes, operational safety improvements, and memory management.

## Acceptance Criteria

### Batch 1 — Trivial Fixes
- [ ] Dockerfile uses `dotnet/aspnet:9.0` and `dotnet/sdk:9.0`
- [ ] `DefaultTemperature` removed from `GeminiOptions`, `AnthropicOptions`, `appsettings.json`
- [ ] Placeholder tests deleted

### Batch 2 — Legacy Cleanup + Parser Fix
- [ ] Legacy `Anthropic/` directory deleted (6 files)
- [ ] `AnthropicException` moved to `LlmProviders/Anthropic/`
- [ ] Legacy test files deleted
- [ ] `StructuredOutputParser` escape handling fixed (consecutive backslash counting)
- [ ] Test case for `\\"` scenario added
- [ ] `CODEBASE.md` updated

### Batch 3 — Background Service
- [ ] `BackgroundAnalysisRunner` replaces `Task.Run()` fire-and-forget
- [ ] Graceful shutdown via `ApplicationStopping` cancellation
- [ ] Tests for the runner

### Batch 4 — Startup Safety + Monitoring
- [ ] API key validated at startup (fail fast if empty)
- [ ] `/health` endpoint added

### Batch 5 — Memory Management
- [ ] `InMemoryAnalysisStore` eviction (timer or max-entry)
- [ ] `ChannelEventTransport` cleanup after completion

### Batch 6 — Deduplication
- [ ] Same video ID returns existing in-progress analysis instead of starting new one

## Scope

- All items in P0 and P1 from audit plan
- P2 items deferred to `future-improvements.md`

## Verification

`./verify.sh` passes after each batch.
