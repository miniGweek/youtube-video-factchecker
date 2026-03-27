using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactChecker.Infrastructure.Anthropic;

/// <summary>
/// Parses structured JSON output from LLM responses.
/// Handles common quirks: markdown code fences, leading/trailing whitespace.
/// </summary>
public static class StructuredOutputParser
{
    private static readonly Regex MarkdownFencePattern =
        new(@"^```(?:json)?\s*\n?([\s\S]*?)\n?```\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
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

        var match = MarkdownFencePattern.Match(trimmed);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return trimmed;
    }
}
