# Task 005 — Show Transcript Text on Analysis Page

**Status: active**

## Goal

Display the full transcript text on the analysis page so users can verify that extracted claims accurately reflect what was said in the video.

## Acceptance Criteria

- [ ] Full transcript text is visible on the analysis page
- [ ] Transcript is displayed in a collapsible section (collapsed by default) beneath the existing quality badge and word count
- [ ] Long transcripts are scrollable (max height, overflow-y: auto)
- [ ] `./verify.sh` passes with zero errors and zero warnings
- [ ] All existing tests pass

## In-Scope Files

- `src/FactChecker.Core/Events/TranscriptExtractedEvent.cs` — add `Text` property
- `src/FactChecker.Core/Pipeline/AnalysisPipeline.cs` — pass `transcript.Text` when publishing event
- `src/FactChecker.Web/Pages/Shared/_TranscriptInfo.cshtml` — render transcript in collapsible section
- Any test files that construct `TranscriptExtractedEvent` — update to include `Text` parameter

## Out of Scope

- Timestamp-linked transcript display
- Transcript search or highlighting
- Architecture changes beyond adding `Text` to the event record
