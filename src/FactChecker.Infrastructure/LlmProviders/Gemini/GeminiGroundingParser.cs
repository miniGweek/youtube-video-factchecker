using System.Text.Json;
using FactChecker.Infrastructure.LlmProviders.Common;

namespace FactChecker.Infrastructure.LlmProviders.Gemini;

/// <summary>
/// Extracts <see cref="SearchResultSource"/> records from Gemini's groundingMetadata response.
/// Handles responses with no grounding metadata (returns empty list).
/// </summary>
public static class GeminiGroundingParser
{
    /// <summary>
    /// Parses grounding metadata from a Gemini API response and returns search result sources.
    /// </summary>
    /// <param name="responseJson">The full raw JSON response from the Gemini API.</param>
    /// <returns>A list of sources extracted from grounding chunks, or an empty list if none.</returns>
    public static IReadOnlyList<SearchResultSource> ExtractSources(JsonElement responseJson)
    {
        if (!TryGetGroundingMetadata(responseJson, out var groundingMetadata))
            return [];

        var chunks = GetGroundingChunks(groundingMetadata);
        if (chunks.Count == 0)
            return [];

        var snippetsByIndex = GetSnippetsByChunkIndex(groundingMetadata);

        var sources = new List<SearchResultSource>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (!chunk.TryGetProperty("web", out var web))
                continue;

            var uri = web.TryGetProperty("uri", out var uriProp) ? uriProp.GetString() : null;
            var title = web.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;

            if (string.IsNullOrEmpty(uri))
                continue;

            var snippet = snippetsByIndex.TryGetValue(i, out var s) ? s : string.Empty;

            sources.Add(new SearchResultSource(
                Url: new Uri(uri),
                Title: title ?? string.Empty,
                Snippet: snippet));
        }

        return sources;
    }

    private static bool TryGetGroundingMetadata(JsonElement responseJson, out JsonElement groundingMetadata)
    {
        groundingMetadata = default;

        if (!responseJson.TryGetProperty("candidates", out var candidates))
            return false;

        if (candidates.GetArrayLength() == 0)
            return false;

        var firstCandidate = candidates[0];
        return firstCandidate.TryGetProperty("groundingMetadata", out groundingMetadata);
    }

    private static List<JsonElement> GetGroundingChunks(JsonElement groundingMetadata)
    {
        if (!groundingMetadata.TryGetProperty("groundingChunks", out var chunks))
            return [];

        var result = new List<JsonElement>(chunks.GetArrayLength());
        foreach (var chunk in chunks.EnumerateArray())
            result.Add(chunk);

        return result;
    }

    /// <summary>
    /// Builds a mapping from grounding chunk index to concatenated snippet text
    /// derived from <c>groundingSupports</c> segments.
    /// </summary>
    private static Dictionary<int, string> GetSnippetsByChunkIndex(JsonElement groundingMetadata)
    {
        var snippets = new Dictionary<int, string>();

        if (!groundingMetadata.TryGetProperty("groundingSupports", out var supports))
            return snippets;

        foreach (var support in supports.EnumerateArray())
        {
            var segmentText = string.Empty;
            if (support.TryGetProperty("segment", out var segment) &&
                segment.TryGetProperty("text", out var textProp))
            {
                segmentText = textProp.GetString() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(segmentText))
                continue;

            if (!support.TryGetProperty("groundingChunkIndices", out var indices))
                continue;

            foreach (var indexElement in indices.EnumerateArray())
            {
                var chunkIndex = indexElement.GetInt32();
                if (snippets.TryGetValue(chunkIndex, out var existing))
                    snippets[chunkIndex] = existing + " " + segmentText;
                else
                    snippets[chunkIndex] = segmentText;
            }
        }

        return snippets;
    }
}
