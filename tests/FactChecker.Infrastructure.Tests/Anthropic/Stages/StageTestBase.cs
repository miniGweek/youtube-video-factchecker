using System.Net;
using System.Text;
using FactChecker.Infrastructure.Anthropic;
using FactChecker.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.Tests.Anthropic.Stages;

/// <summary>Shared helpers for stage unit tests that use recorded API response fixtures.</summary>
public abstract class StageTestBase
{
    protected static string LoadFixture(string filename)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Anthropic", "Stages", "Fixtures", filename);
        return File.ReadAllText(path);
    }

    protected static AnthropicClientWrapper CreateWrapper(string fixtureJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StaticHandler(status, fixtureJson);
        var factory = new FakeHttpClientFactory(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new AnthropicOptions
        {
            ApiKey = "test-key",
            FastModel = "claude-haiku-4-5-20251001",
            StandardModel = "claude-sonnet-4-20250514",
            MaxRetries = 0
        });
        return new AnthropicClientWrapper(factory, options, NullLogger<AnthropicClientWrapper>.Instance);
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class StaticHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
