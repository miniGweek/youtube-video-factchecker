using FactChecker.Core.Enums;

namespace FactChecker.Core.Models;

public record Summary(
    string Thesis,
    IReadOnlyList<string> KeyPoints,
    ContentDomain Domain);
