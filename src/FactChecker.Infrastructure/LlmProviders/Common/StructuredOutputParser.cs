using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactChecker.Infrastructure.LlmProviders.Common;

/// <summary>
/// Parses structured JSON output from LLM responses.
/// Handles common quirks: markdown code fences, leading/trailing whitespace.
/// </summary>
public static class StructuredOutputParser
{
    private static readonly Regex MarkdownFencePattern =
        new(@"```(?:json)?\s*\n?([\s\S]*?)\n?```", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Extracts and deserializes JSON from an LLM response string.
    /// Strips markdown code fences if present.
    /// </summary>
    /// <exception cref="JsonException">Thrown when the response cannot be parsed as valid JSON.</exception>
    public static T Parse<T>(string responseText)
    {
        ArgumentNullException.ThrowIfNull(responseText);
        var json = ExtractJson(responseText);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new JsonException($"Deserialization returned null for type {typeof(T).Name}.");
    }

    /// <summary>
    /// Strips markdown code fences and trims whitespace from the response.
    /// </summary>
    public static string ExtractJson(string responseText)
    {
        ArgumentNullException.ThrowIfNull(responseText);
        var trimmed = responseText.Trim();

        // Try markdown fence first (works even with leading prose)
        var match = MarkdownFencePattern.Match(trimmed);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        // Fallback: extract outermost { ... } via brace counting
        int start = trimmed.IndexOf('{', StringComparison.Ordinal);
        if (start >= 0)
        {
            int depth = 0;
            bool inString = false;
            for (int i = start; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c == '"' && (i == 0 || trimmed[i - 1] != '\\'))
                    inString = !inString;
                if (!inString)
                {
                    if (c == '{') depth++;
                    else if (c == '}') { depth--; if (depth == 0) return trimmed[start..(i + 1)]; }
                }
            }
        }

        return trimmed;
    }
}
