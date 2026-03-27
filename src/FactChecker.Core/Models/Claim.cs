namespace FactChecker.Core.Models;

public record Claim(
    string Id,
    string Text,
    string Context,
    int Importance);  // 1-5, centrality to video thesis
