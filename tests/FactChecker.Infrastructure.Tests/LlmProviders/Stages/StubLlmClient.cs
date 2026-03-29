using FactChecker.Infrastructure.LlmProviders.Common;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Stages;

/// <summary>
/// A simple stub <see cref="ILlmClient"/> for unit testing stages.
/// Returns pre-configured responses and records the requests made.
/// </summary>
internal sealed class StubLlmClient : ILlmClient
{
    private LlmResponse? _completeResponse;
    private LlmSearchResponse? _searchResponse;
    private Exception? _exception;

    private readonly List<LlmRequest> _requests = [];

    public IReadOnlyList<LlmRequest> Requests => _requests;

    /// <summary>The most recent request made to this stub. Null if no calls have been made.</summary>
    public LlmRequest? LastRequest => _requests.Count > 0 ? _requests[^1] : null;

    public StubLlmClient WithCompleteResponse(string content)
    {
        _completeResponse = new LlmResponse(content, new TokenUsage(100, 50));
        return this;
    }

    public StubLlmClient WithSearchResponse(string content, IReadOnlyList<SearchResultSource>? sources = null)
    {
        _searchResponse = new LlmSearchResponse(
            content,
            sources ?? Array.Empty<SearchResultSource>(),
            new TokenUsage(200, 100));
        return this;
    }

    public StubLlmClient WithException(Exception exception)
    {
        _exception = exception;
        return this;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _requests.Add(request);

        if (_exception is not null)
            throw _exception;

        return Task.FromResult(_completeResponse
            ?? throw new InvalidOperationException("No CompleteAsync response configured on StubLlmClient."));
    }

    public Task<LlmSearchResponse> CompleteWithSearchAsync(LlmRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _requests.Add(request);

        if (_exception is not null)
            throw _exception;

        return Task.FromResult(_searchResponse
            ?? throw new InvalidOperationException("No CompleteWithSearchAsync response configured on StubLlmClient."));
    }
}
