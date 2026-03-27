using FactChecker.Core.Enums;

namespace FactChecker.Core.Models;

public record AnalysisError(AnalysisStage Stage, string Message, bool Recoverable);
