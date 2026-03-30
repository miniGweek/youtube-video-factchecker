using System.Text.Json;
using FactChecker.Infrastructure.LlmProviders.Gemini;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Gemini;

public sealed class GeminiGroundingParserTests
{
    private static JsonElement LoadFixture(string fileName)
    {
        var path = Path.Combine("LlmProviders", "Gemini", "Fixtures", fileName);
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ExtractSources_MultipleSources_ReturnsAllWithSnippets()
    {
        var response = LoadFixture("complete-with-grounding.json");

        var sources = GeminiGroundingParser.ExtractSources(response);

        Assert.Equal(3, sources.Count);

        Assert.Equal("https://example.com/article-1", sources[0].Url.ToString());
        Assert.Equal("Article One - Scientific Evidence", sources[0].Title);
        Assert.Contains("Multiple sources confirm this claim.", sources[0].Snippet);

        Assert.Equal("https://example.org/article-2", sources[1].Url.ToString());
        Assert.Equal("Article Two - Research Findings", sources[1].Title);
        Assert.Contains("Multiple sources confirm this claim.", sources[1].Snippet);

        Assert.Equal("https://example.net/article-3", sources[2].Url.ToString());
        Assert.Equal("Article Three - Expert Analysis", sources[2].Title);
        Assert.Contains("Expert analysis further supports the finding.", sources[2].Snippet);
    }

    [Fact]
    public void ExtractSources_SingleSource_ReturnsSingleSource()
    {
        var response = LoadFixture("complete-single-source.json");

        var sources = GeminiGroundingParser.ExtractSources(response);

        Assert.Single(sources);
        Assert.Equal("https://example.com/single-source", sources[0].Url.ToString());
        Assert.Equal("Single Source Article", sources[0].Title);
        Assert.Equal("One source partially confirms this.", sources[0].Snippet);
    }

    [Fact]
    public void ExtractSources_NoGroundingMetadata_ReturnsEmptyList()
    {
        var response = LoadFixture("complete-no-grounding.json");

        var sources = GeminiGroundingParser.ExtractSources(response);

        Assert.Empty(sources);
    }

    [Fact]
    public void ExtractSources_ZeroChunks_ReturnsEmptyList()
    {
        var response = LoadFixture("grounding-zero-chunks.json");

        var sources = GeminiGroundingParser.ExtractSources(response);

        Assert.Empty(sources);
    }

    [Fact]
    public void ExtractSources_PartialGrounding_SkipsNonWebChunks()
    {
        var response = LoadFixture("grounding-partial.json");

        var sources = GeminiGroundingParser.ExtractSources(response);

        Assert.Single(sources);
        Assert.Equal("https://example.com/valid-source", sources[0].Url.ToString());
        Assert.Equal("Valid Source", sources[0].Title);
    }

    [Fact]
    public void ExtractSources_EmptyCandidates_ReturnsEmptyList()
    {
        var response = LoadFixture("complete-empty-candidates.json");

        var sources = GeminiGroundingParser.ExtractSources(response);

        Assert.Empty(sources);
    }

    [Fact]
    public void ExtractSources_SuccessResponse_NoCandidatesProperty_ReturnsEmptyList()
    {
        using var doc = JsonDocument.Parse("{}");
        var response = doc.RootElement.Clone();

        var sources = GeminiGroundingParser.ExtractSources(response);

        Assert.Empty(sources);
    }
}
