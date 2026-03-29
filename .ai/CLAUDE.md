# CLAUDE.md — YouTube Video Fact-Checker

## Commit Protocol (applies to ALL commits)

This is the most frequently missed step. Execute it on every commit without exception.

1. Stage only files relevant to this change.
2. Write a commit message following Conventional Commits: `type(scope): description`
   - Types: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`
3. Always use the correct author identity:
   ```bash
   git -c user.name="Rahul Sarkar via Claude Code" \
       -c user.email="rahul.sarkar-claudecode@gmail.com" \
       commit -m "type(scope): description"
   ```
4. Never use bare `git commit` without the `-c` flags above.
5. If fixing a bug or making an improvement, use the `fix` or `refactor` type accordingly.

Human commits use the global git config (`dedlok@gmail.com`) — do not touch it.

---

## Codebase & Architecture References

Before any task, read these documents — do not rely on summaries below:

- **`.ai/design/architecture.md`** — Authoritative architecture reference (domain models, interfaces, pipeline, scoring, LLM provider architecture, cost estimates, error handling strategy, test strategy).
- **`CODEBASE.md`** — File-by-file breakdown of every class, interface, and its role.

These documents are the source of truth for project structure, pipeline flow, configuration shape, and model tier mappings. This file (CLAUDE.md) governs **how you behave**, not what the system looks like.

---

## Memory

Save all memory files to `.ai/memory/` (in the project root).
The index file is `.ai/memory/MEMORY.md`. Use the same frontmatter format (name, description, type) and content conventions.
Task progress is tracked in `.ai/memory/progress.json` — see Progress Tracking section below.

---

## Build & Run

```bash
dotnet build youtube-fact-checker.sln          # Must produce zero errors AND zero warnings
dotnet test                                     # All tests must pass before any PR
dotnet run --project src/FactChecker.Web        # Runs on https://localhost:5001
```

**Warnings are errors.** `Directory.Build.props` enforces `TreatWarningsAsErrors`. Fix warnings, don't suppress them.

---

## Package Management

When adding any NuGet package:

1. Always use the CLI to resolve the latest stable version:
   ```bash
   dotnet add <project> package <PackageName>
   ```
   This defaults to latest stable. Never hand-edit `.csproj` with a version number from memory.
2. Do not use pre-release versions unless explicitly requested.
3. When initialising a new project or after adding multiple packages, verify:
   ```bash
   dotnet list package --outdated
   ```
   Update any outdated packages before proceeding.
4. After adding a package, confirm the project still builds with zero warnings.

---

## Task Management

- Task files live in `.ai/tasks/`, numbered: `000-name.md`, `001-name.md`, etc.
- **Only load task files with `Status: active` in their header.**
- Never load complete/draft/superseded task files unless explicitly asked.
- Each task file is self-contained — don't cross-reference completed task files.

---

## Task Execution Protocol

When picking up any work (task, bug fix, or improvement):

1. **Read the architecture doc** (`.ai/design/architecture.md`).
2. **For tasks:** find the active task file in `.ai/tasks/`. Read the contract — note dependencies, in-scope files, acceptance criteria.
3. **Implement code + tests together** — not code first, tests later.
4. **Verify:** `dotnet build` (zero warnings) → `dotnet test` (all pass) → diff is scoped to the work.
5. **Commit** using the Commit Protocol above. No exceptions.
6. **Update progress** — see Progress Tracking below. This is mandatory.
7. **Update documentation if the change:**
   - Adds, renames, or deletes a file → update `CODEBASE.md` (the relevant table only).
   - Changes a public interface, model, or module boundary → flag whether `architecture.md` needs revision. Propose the change, don't decide alone.
   - Is implementation-only (bug fix inside a method, refactor with no interface change) → no doc update required.
8. **Stop.** Do not chain into the next task or auto-evaluate your own output.

---

## Progress Tracking

**File:** `.ai/memory/progress.json` — update after completing every piece of work.

### For planned tasks

Update the matching task entry:
- `"status"`: `"complete"`
- `"commit"`: short hash
- `"date"`: `YYYY-MM-DD`
- `"tests"`: total test count after this task
- `"notes"`: deviations, additions, or issues — `null` if none

### For bug fixes and improvements

Append to the `"fixes"` array (create it if it doesn't exist):
```json
{
  "id": "fix-001",
  "description": "Short description of what broke and what changed",
  "date": "YYYY-MM-DD",
  "commit": "short hash",
  "filesChanged": ["src/path/to/File.cs"],
  "architectureImpact": false
}
```

Increment the `"id"` sequentially. Set `"architectureImpact": true` only if a public interface, model, or module boundary changed.

### General rules

- Update `"lastUpdated"` at the root level to the current ISO timestamp.
- If all tasks in a task set are complete, set the task set's `"status"` to `"complete"`.
- Allowed status values: `"pending"`, `"in-progress"`, `"complete"`, `"blocked"`, `"superseded"`.
- This update must happen in the same commit as the implementation.

---

## Critical Rules (Non-Negotiable)

1. **Core has zero NuGet dependencies.** Models, interfaces, scoring — all pure C#.
2. **Scoring is deterministic, not LLM-generated.** `IScoringEngine` is pure computation.
3. **Every FactCheck must cite sources or explicitly state none found.**
4. **Claims must be falsifiable assertions.** Opinions, speculation, rhetorical questions are excluded.
5. **Aggregate score penalises worst claims, not just average.**
6. **Source URLs must be validated** with HTTP HEAD requests. Mark `IsAccessible` true/false.
7. **Never commit API keys.** `GEMINI_API_KEY` and `ANTHROPIC_API_KEY` from environment variables or user secrets.
8. **Warnings are errors.** Fix them, don't suppress them.
9. **`ILlmClient` lives in Infrastructure, not Core.** It is an implementation detail.
10. **Stages are provider-agnostic.** Stage classes must not import any Gemini or Anthropic namespace.

---

## Patterns to Follow

- **Records** for immutable value objects. **Class** for `AnalysisResult` (mutable aggregate with state transition methods).
- **IReadOnlyList\<T\>** for all collection properties on immutable types.
- **CancellationToken** on every async method.
- **IHttpClientFactory** for all HTTP clients (never `new HttpClient()`).
- **Options pattern** for configuration binding.
- **System.Text.Json** with strict settings for all serialisation.
- **System.Threading.Channels** for in-process event transport.
- **ILlmClient** for all LLM calls — stages never reference provider-specific types.

---

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
- Don't use bare `git commit` without the author override flags.
- Don't chain into the next task after completing one — hard stop at completion.
- Don't self-assess your own output quality — that is the human's job.
- Don't override repo-level git config — identity is set per-commit via `-c` flags.
- Don't hand-edit `.csproj` package versions from memory — use `dotnet add package` to resolve latest.

---

## Documentation Maintenance

After creating or modifying any source file, check whether `CODEBASE.md` needs updating.
Only update the sections affected — do not rewrite unchanged sections.
If you add a new file, add it to the relevant table. If you rename or delete one, remove it.

Architecture changes (interface, model, or module boundary modifications) require flagging for review — propose the change in your output, do not unilaterally update `architecture.md`.