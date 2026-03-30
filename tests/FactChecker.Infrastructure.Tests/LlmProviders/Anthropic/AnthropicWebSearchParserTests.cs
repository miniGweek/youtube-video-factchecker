using FactChecker.Infrastructure.LlmProviders.Anthropic;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Anthropic;

public class AnthropicWebSearchParserTests
{
    private static string LoadFixture(string filename)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "LlmProviders", "Anthropic", "Fixtures", filename);
        return File.ReadAllText(path);
    }

    // ── ExtractSources ──────────────────────────────────────────────────────

    [Fact]
    public void ExtractSources_MultipleSources_ReturnsAll()
    {
        var json = LoadFixture("search_multiple_sources.json");

        var sources = AnthropicWebSearchParser.ExtractSources(json);

        Assert.Equal(2, sources.Count);
        Assert.Equal(new Uri("https://www.example.com/article1"), sources[0].Url);
        Assert.Equal("Exercise and Heart Health Study", sources[0].Title);
        Assert.Equal(new Uri("https://www.example.org/research"), sources[1].Url);
        Assert.Equal("Global Health Research", sources[1].Title);
    }

    [Fact]
    public void ExtractSources_SingleSource_ReturnsOne()
    {
        var json = LoadFixture("search_single_source.json");

        var sources = AnthropicWebSearchParser.ExtractSources(json);

        Assert.Single(sources);
        Assert.Equal(new Uri("https://www.who.int/health-topics/data"), sources[0].Url);
        Assert.Equal("WHO Health Statistics", sources[0].Title);
    }

    [Fact]
    public void ExtractSources_NoSearchResults_ReturnsEmpty()
    {
        var json = LoadFixture("search_no_results.json");

        var sources = AnthropicWebSearchParser.ExtractSources(json);

        Assert.Empty(sources);
    }

    [Fact]
    public void ExtractSources_MixedTextAndToolBlocks_ReturnsAllSources()
    {
        var json = LoadFixture("search_mixed_blocks.json");

        var sources = AnthropicWebSearchParser.ExtractSources(json);

        Assert.Equal(2, sources.Count);
        Assert.Equal(new Uri("https://www.nature.com/articles/study123"), sources[0].Url);
        Assert.Equal(new Uri("https://www.science.org/doi/abc"), sources[1].Url);
    }

    [Fact]
    public void ExtractSources_EmptyString_ReturnsEmpty()
    {
        var sources = AnthropicWebSearchParser.ExtractSources(string.Empty);

        Assert.Empty(sources);
    }

    [Fact]
    public void ExtractSources_InvalidJson_ReturnsEmpty()
    {
        var sources = AnthropicWebSearchParser.ExtractSources("not valid json");

        Assert.Empty(sources);
    }

    [Fact]
    public void ExtractSources_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AnthropicWebSearchParser.ExtractSources(null!));
    }

    // ── ExtractTextContent ──────────────────────────────────────────────────

    [Fact]
    public void ExtractTextContent_MultipleSources_ReturnsLastTextBlock()
    {
        var json = LoadFixture("search_multiple_sources.json");

        var text = AnthropicWebSearchParser.ExtractTextContent(json);

        Assert.Contains("verdict", text);
        Assert.Contains("Supported", text);
    }

    [Fact]
    public void ExtractTextContent_MixedBlocks_ReturnsLastTextBlock()
    {
        var json = LoadFixture("search_mixed_blocks.json");

        var text = AnthropicWebSearchParser.ExtractTextContent(json);

        Assert.Contains("verdict", text);
        Assert.Contains("Supported", text);
    }

    [Fact]
    public void ExtractTextContent_NoResults_ReturnsTextDirectly()
    {
        var json = LoadFixture("search_no_results.json");

        var text = AnthropicWebSearchParser.ExtractTextContent(json);

        Assert.Contains("unable to find", text);
    }

    [Fact]
    public void ExtractTextContent_EmptyContent_ReturnsEmpty()
    {
        var json = LoadFixture("complete_empty.json");

        var text = AnthropicWebSearchParser.ExtractTextContent(json);

        Assert.Equal(string.Empty, text);
    }

    // ── ExtractUsage ────────────────────────────────────────────────────────

    [Fact]
    public void ExtractUsage_ValidResponse_ReturnsTokenCounts()
    {
        var json = LoadFixture("search_multiple_sources.json");

        var usage = AnthropicWebSearchParser.ExtractUsage(json);

        Assert.Equal(500, usage.InputTokens);
        Assert.Equal(180, usage.OutputTokens);
    }

    [Fact]
    public void ExtractUsage_EmptyString_ReturnsZeros()
    {
        var usage = AnthropicWebSearchParser.ExtractUsage(string.Empty);

        Assert.Equal(0, usage.InputTokens);
        Assert.Equal(0, usage.OutputTokens);
    }
}
