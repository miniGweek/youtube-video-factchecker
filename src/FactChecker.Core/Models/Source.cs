namespace FactChecker.Core.Models;

public record Source(
    Uri Url,
    string Title,
    string Snippet,
    bool IsAccessible);
