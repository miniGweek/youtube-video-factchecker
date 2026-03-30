# Task 003 — Claim Verification Observability + UI Progress

Status: active

## Goal

Three observability gaps exist during the fact-checking stage:

1. Per-claim verification produces no console logs — 15 claims verify silently over ~75 seconds
2. The UI shows only a static spinner during claim verification with no progress indication
3. When a claim fails verification, the UI shows the generic message "Verification failed." rather than the actual reason

## Acceptance Criteria

- [ ] Console emits one INFO log per verified claim: `Analysis {id} claim {claimId} verified: {Verdict} ({n}/{total})`
- [ ] UI claims header shows a `<progress>` bar and `n / total` counter that updates as each claim is verified
- [ ] When fact-checking is complete, the progress bar is removed and the header shows "Fact-checked N claims"
- [ ] When a claim fails verification (Unverifiable verdict), the reasoning shown on the UI includes the actual error message, not just "Verification failed."
- [ ] `./verify.sh` passes with zero warnings and all tests green

## In-Scope Files

- `src/FactChecker.Core/Pipeline/AnalysisPipeline.cs`
- `src/FactChecker.Web/Models/ClaimsHeaderModel.cs`
- `src/FactChecker.Web/Pages/Shared/_ClaimsHeader.cshtml`
- `src/FactChecker.Web/Endpoints/AnalysisEndpoints.cs`

## Out of Scope

- Changes to Core event types
- Persistence or test additions (no new behaviour, observability only)
- Architecture changes

## Dependencies

- Task set 002 complete ✓
- Stage-level pipeline logs from `fix/missing-pipeline-stage-logs` branch (in progress)
