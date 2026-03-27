using FactChecker.Core.Models;

namespace FactChecker.Core.Pipeline;

public interface IAnalysisStore
{
    void Add(AnalysisResult result);
    AnalysisResult? TryGet(string id);
}
