# Future Improvements (P2 — from audit 2026-04-07)

Items identified during codebase audit. Not urgent for v1 friends-scale app. Pick up when convenient.

## P2-1: Move Gemini API key from query string to header
- `GeminiLlmClient.cs:117` puts API key in URL query string — appears in HTTP logs
- Google supports `x-goog-api-key` header as alternative
- Files: `GeminiLlmClient.cs`, `GeminiLlmClientTests.cs`

## P2-2: HTML SSE stream integration tests
- `GetHtmlStream` (main user-facing SSE endpoint) has no tests
- JSON SSE stream is tested but HTML variant is not
- Files: `AnalysisEndpointTests.cs`

## P2-3: Document Anthropic rate limiting decision
- Three options explored in `.ai/memory/draft_plan_search_replacement.md` (Brave Search, Playwright, Playwright MCP)
- No decision made — should be captured as known limitation in architecture.md
- Files: `.ai/design/architecture.md`

## Not Broken (confirmed acceptable for v1)
- Hardcoded localhost CORS — friends-scale, change at deploy time
- `Confidence` field unused in scoring — intentional (Rule #2: deterministic scoring)
- Anthropic API version `2023-06-01` — current stable version
- No rate limiting — bounded by `MaxConcurrentVerifications=4`
- No analysis cancellation — `PipelineTimeoutSeconds` + `CancellationToken` handles this
