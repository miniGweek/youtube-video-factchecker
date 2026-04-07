namespace FactChecker.Web.Services;

/// <summary>
/// Abstraction over the channel that feeds work items to <see cref="BackgroundAnalysisRunner"/>.
/// </summary>
internal interface IAnalysisDispatcher
{
    ValueTask EnqueueAsync(string analysisId, Uri videoUri, CancellationToken ct = default);
}
