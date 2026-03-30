#!/usr/bin/env bash
set -euo pipefail

echo "=== Restore ==="
dotnet restore youtube-fact-checker.sln

echo "=== Build ==="
dotnet build youtube-fact-checker.sln --no-restore

echo "=== Tests ==="
dotnet test youtube-fact-checker.sln --no-build --verbosity normal

echo "=== All checks passed ==="
