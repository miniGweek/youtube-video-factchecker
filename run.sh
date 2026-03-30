#!/usr/bin/env bash
set -euo pipefail

echo "=== Restore ==="
dotnet restore youtube-fact-checker.sln

echo "=== Build ==="
dotnet build youtube-fact-checker.sln --no-restore

echo "=== Running FactChecker.Web ==="
dotnet run --project src/FactChecker.Web --no-build
