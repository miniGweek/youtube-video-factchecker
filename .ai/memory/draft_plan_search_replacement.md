---
name: Draft Plan — Replace Anthropic Web Search
description: Unfinished plan to replace Anthropic's built-in web search in claim verification due to 30k token/min rate limit
type: project
---

Draft plan saved at `/home/rahul/.claude/plans/crispy-drifting-wall.md`.

**Problem:** App hits Anthropic's 30k tokens/minute limit. Claim verification (Sonnet + built-in web search) is ~82% of token spend — ~32k tokens for a 10-claim video, 4 concurrent calls.

**Three options explored:**
- **Option A** — Brave Search API + Claude Haiku: fetch search results via Brave API, pass to Haiku for reasoning. ~400 tokens/claim vs ~2000. Recommended. Needs a Brave API key.
- **Option B** — Playwright .NET: headless browser search, pass results to Haiku. Free but fragile scraping, needs Chromium in Docker.
- **Option C** — Playwright MCP server (`@playwright/mcp`): spawn Node.js MCP server, call via JSON-RPC stdio from .NET. Same as B but with MCP indirection. Needs Node.js in Docker.

**Stopgap (no code change):** set `MaxConcurrentVerifications: 1` in appsettings.json.

**Status:** User interrupted before choosing an option. Resume by reading the full plan file and asking which option to proceed with.

**Why:** Rate limit hit during real video testing; user asked about local browser / MCP integration as an alternative.
**How to apply:** When user returns to this topic, read the plan file for full implementation details before proceeding.
