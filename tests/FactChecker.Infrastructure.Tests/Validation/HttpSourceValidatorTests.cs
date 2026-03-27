using System.Net;
using System.Text;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.Validation;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.Tests.Validation;

public class HttpSourceValidatorTests
{
    private static Source TestSource(string url = "https://example.com") =>
        new(new Uri(url), "Test Page", "A snippet.", IsAccessible: false);

    private static HttpSourceValidator CreateValidator(HttpMessageHandler handler, int timeoutSeconds = 5)
    {
        var factory = new FakeHttpClientFactory(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new AnalysisOptions
        {
            SourceValidationTimeoutSeconds = timeoutSeconds
        });
        return new HttpSourceValidator(factory, options);
    }

    [Fact]
    public async Task ValidateAsync_OkResponse_SetsIsAccessibleTrue()
    {
        var validator = CreateValidator(new StaticHandler(HttpStatusCode.OK));

        var result = await validator.ValidateAsync(TestSource());

        Assert.True(result.IsAccessible);
    }

    [Fact]
    public async Task ValidateAsync_NotFoundResponse_SetsIsAccessibleFalse()
    {
        var validator = CreateValidator(new StaticHandler(HttpStatusCode.NotFound));

        var result = await validator.ValidateAsync(TestSource());

        Assert.False(result.IsAccessible);
    }

    [Fact]
    public async Task ValidateAsync_ServerErrorResponse_SetsIsAccessibleFalse()
    {
        var validator = CreateValidator(new StaticHandler(HttpStatusCode.InternalServerError));

        var result = await validator.ValidateAsync(TestSource());

        Assert.False(result.IsAccessible);
    }

    [Fact]
    public async Task ValidateAsync_NetworkFailure_SetsIsAccessibleFalse()
    {
        var validator = CreateValidator(new ThrowingHandler(new HttpRequestException("Connection refused")));

        var result = await validator.ValidateAsync(TestSource());

        Assert.False(result.IsAccessible);
    }

    [Fact]
    public async Task ValidateAsync_Timeout_SetsIsAccessibleFalse()
    {
        var validator = CreateValidator(new ThrowingHandler(new TaskCanceledException("Timeout")));

        var result = await validator.ValidateAsync(TestSource());

        Assert.False(result.IsAccessible);
    }

    [Fact]
    public async Task ValidateAsync_PreservesOtherSourceFields()
    {
        var source = new Source(
            new Uri("https://example.com"),
            Title: "My Title",
            Snippet: "My Snippet",
            IsAccessible: false);
        var validator = CreateValidator(new StaticHandler(HttpStatusCode.OK));

        var result = await validator.ValidateAsync(source);

        Assert.Equal(source.Url, result.Url);
        Assert.Equal("My Title", result.Title);
        Assert.Equal("My Snippet", result.Snippet);
        Assert.True(result.IsAccessible);
    }

    [Fact]
    public async Task ValidateAsync_SourceAlreadyAccessible_UpdatesToReflectCurrentState()
    {
        var source = new Source(new Uri("https://example.com"), "T", "S", IsAccessible: true);
        var validator = CreateValidator(new StaticHandler(HttpStatusCode.NotFound));

        // Even if source was previously accessible, a current 404 should mark it false
        var result = await validator.ValidateAsync(source);

        Assert.False(result.IsAccessible);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class StaticHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8)
            });
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromException<HttpResponseMessage>(exception);
    }
}
