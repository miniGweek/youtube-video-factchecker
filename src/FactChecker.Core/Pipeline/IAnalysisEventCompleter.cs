namespace FactChecker.Core.Pipeline;

/// <summary>
/// Signals that all events for an analysis have been published and the event channel can be closed.
/// Implemented by the event transport (e.g. ChannelEventTransport) alongside IAnalysisEventSink.
/// </summary>
public interface IAnalysisEventCompleter
{
    void Complete(string analysisId);
}
