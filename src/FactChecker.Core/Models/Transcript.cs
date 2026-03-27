using FactChecker.Core.Enums;

namespace FactChecker.Core.Models;

public record Transcript(
    string Text,
    TranscriptQuality Quality,
    int WordCount);
