using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using Microsoft.Extensions.Options;
using FactChecker.Core.Options;

namespace FactChecker.Infrastructure.Validation;

public sealed class HttpSourceValidator : ISourceValidator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AnalysisOptions _options;

    public HttpSourceValidator(IHttpClientFactory httpClientFactory, IOptions<AnalysisOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<Source> ValidateAsync(Source source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var isAccessible = await CheckAccessibleAsync(source.Url, ct).ConfigureAwait(false);
        return source with { IsAccessible = isAccessible };
    }

    private async Task<bool> CheckAccessibleAsync(Uri url, CancellationToken ct)
    {
        using var httpClient = _httpClientFactory.CreateClient(nameof(HttpSourceValidator));
        httpClient.Timeout = TimeSpan.FromSeconds(_options.SourceValidationTimeoutSeconds);

        // Allow up to 3 redirects
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            // Timeout or cancellation — not accessible
            return false;
        }
    }
}
