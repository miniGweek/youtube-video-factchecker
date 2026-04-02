---
Status: active
---

# Task 006: Fix claim verification reliability (RECITATION retry + JSON mode)

## Problem

1. Gemini RECITATION filter returns empty responses — current retry sends identical request (fragile)
2. JSON parse failures when Gemini returns markdown instead of JSON — preventable via API config

## Acceptance Criteria

- [ ] `GeminiLlmClient.BuildRequestBody()` includes `responseMimeType: "application/json"` in `generationConfig`
- [ ] `ClaimVerifierStage` appends anti-recitation nudge on empty-response retry instead of resending identical request
- [ ] Log message updated to reflect new retry behaviour
- [ ] All existing tests updated to match new behaviour
- [ ] `./verify.sh` passes

## Scope

- `src/FactChecker.Infrastructure/LlmProviders/Gemini/GeminiLlmClient.cs`
- `src/FactChecker.Infrastructure/LlmProviders/Stages/ClaimVerifierStage.cs`
- `tests/FactChecker.Infrastructure.Tests/LlmProviders/Gemini/GeminiLlmClientTests.cs`
- `tests/FactChecker.Infrastructure.Tests/LlmProviders/Stages/ClaimVerifierStageTests.cs`
