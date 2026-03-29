using System.Text.Json;
using FactChecker.Infrastructure.LlmProviders.Common;

namespace FactChecker.Infrastructure.LlmProviders.Anthropic;

/// <summary>
/// Extracts <see cref="SearchResultSource"/> records from Anthropic web search responses.
/// Anthropic's built-in web_search tool returns interleaved tool_use / tool_result / text
/// content blocks. This parser walks those blocks to extract cited source URLs.
/// </summary>
public static class AnthropicWebSearchParser
{
    /// <summary>
    /// Parses the raw Anthropic API JSON response and extracts search result sources
    /// from web_search tool_result content blocks.
    /// </summary>
    /// <param name="rawResponseJson">The complete JSON response from the Anthropic Messages API.</param>
    /// <returns>A list of search result sources found in the response. Empty if none found.</returns>
    public static IReadOnlyList<SearchResultSource> ExtractSources(string rawResponseJson)
    {
        ArgumentNullException.ThrowIfNull(rawResponseJson);

        if (string.IsNullOrWhiteSpace(rawResponseJson))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(rawResponseJson);
            return ExtractSourcesFromDocument(doc);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Extracts the text content from the last text block in the response.
    /// This is the model's final answer (typically structured JSON).
    /// </summary>
    /// <param name="rawResponseJson">The complete JSON response from the Anthropic Messages API.</param>
    /// <returns>The text of the last text block, or empty string if none found.</returns>
    public static string ExtractTextContent(string rawResponseJson)
    {
        ArgumentNullException.ThrowIfNull(rawResponseJson);

        if (string.IsNullOrWhiteSpace(rawResponseJson))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(rawResponseJson);
            return ExtractLastTextBlock(doc);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts token usage from the raw response JSON.
    /// </summary>
    public static TokenUsage ExtractUsage(string rawResponseJson)
    {
        ArgumentNullException.ThrowIfNull(rawResponseJson);

        if (string.IsNullOrWhiteSpace(rawResponseJson))
            return new TokenUsage(0, 0);

        try
        {
            using var doc = JsonDocument.Parse(rawResponseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage", out var usage))
            {
                var input = usage.TryGetProperty("input_tokens", out var inp) ? inp.GetInt32() : 0;
                var output = usage.TryGetProperty("output_tokens", out var outp) ? outp.GetInt32() : 0;
                return new TokenUsage(input, output);
            }
        }
        catch (JsonException)
        {
            // Fall through
        }

        return new TokenUsage(0, 0);
    }

    internal static string ExtractTextContent(JsonDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return ExtractLastTextBlock(doc);
    }

    internal static IReadOnlyList<SearchResultSource> ExtractSources(JsonDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return ExtractSourcesFromDocument(doc);
    }

    internal static TokenUsage ExtractUsage(JsonDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var root = doc.RootElement;
        if (root.TryGetProperty("usage", out var usage))
        {
            var input = usage.TryGetProperty("input_tokens", out var inp) ? inp.GetInt32() : 0;
            var output = usage.TryGetProperty("output_tokens", out var outp) ? outp.GetInt32() : 0;
            return new TokenUsage(input, output);
        }
        return new TokenUsage(0, 0);
    }

    private static List<SearchResultSource> ExtractSourcesFromDocument(JsonDocument doc)
    {
        var root = doc.RootElement;
        var sources = new List<SearchResultSource>();

        if (!root.TryGetProperty("content", out var contentArray))
            return sources;

        foreach (var block in contentArray.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeElement))
                continue;

            var blockType = typeElement.GetString();

            // web_search tool_result blocks contain the search results with URLs
            if (blockType == "web_search_tool_result" &&
                block.TryGetProperty("content", out var searchResults))
            {
                ExtractSourcesFromSearchResults(searchResults, sources);
            }
        }

        return sources;
    }

    private static void ExtractSourcesFromSearchResults(
        JsonElement searchResults, List<SearchResultSource> sources)
    {
        foreach (var result in searchResults.EnumerateArray())
        {
            if (!result.TryGetProperty("type", out var resultType))
                continue;

            if (resultType.GetString() != "web_search_result")
                continue;

            var url = result.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
            var title = result.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
            var snippet = result.TryGetProperty("encrypted_content", out var snippetProp)
                ? snippetProp.GetString()
                : result.TryGetProperty("page_content", out var pageProp)
                    ? pageProp.GetString()
                    : string.Empty;

            if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                sources.Add(new SearchResultSource(
                    uri,
                    title ?? string.Empty,
                    snippet ?? string.Empty));
            }
        }
    }

    private static string ExtractLastTextBlock(JsonDocument doc)
    {
        var root = doc.RootElement;

        if (!root.TryGetProperty("content", out var contentArray))
            return string.Empty;

        string? lastText = null;
        foreach (var block in contentArray.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) &&
                type.GetString() == "text" &&
                block.TryGetProperty("text", out var text))
            {
                lastText = text.GetString();
            }
        }

        return lastText ?? string.Empty;
    }
}
