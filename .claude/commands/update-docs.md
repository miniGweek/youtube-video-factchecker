Review the current git diff (staged and unstaged) to identify any source files that were added, modified, or deleted:

```
git diff HEAD
git status
```

Then open `CODEBASE.md` and update only the sections affected by those changes:

- New file added → add a row to the relevant table in CODEBASE.md (correct project section, correct sub-table)
- File modified with interface/signature changes → update its description in the table
- File deleted or renamed → remove or update its entry
- New section of the codebase introduced (e.g. new project, new namespace) → add a new section

Rules:
- Do not rewrite or reformat sections that were not affected by the diff
- Do not summarise what you changed at the end — the diff speaks for itself
- If nothing in the diff affects CODEBASE.md, say so and stop
