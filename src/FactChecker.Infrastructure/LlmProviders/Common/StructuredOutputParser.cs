using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactChecker.Infrastructure.LlmProviders.Common;

/// <summary>
/// Parses structured JSON output from LLM responses.
/// Handles common quirks: markdown code fences, leading/trailing whitespace,
/// full-line JavaScript-style comments.
/// </summary>
public static class StructuredOutputParser
{
    private static readonly Regex MarkdownFencePattern =
        new(@"```(?:json)?\s*\n?([\s\S]*?)\n?```", RegexOptions.Compiled);

    // Matches unquoted property names: a bare identifier followed by ':'
    // in a property-name position (after '{', ',', or start of line).
    // Captures the key so it can be wrapped in quotes.
    private static readonly Regex UnquotedKeyPattern =
        new(@"(?<=[\{,]\s*\n?\s*)([a-zA-Z_]\w*)\s*(?=:)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Extracts and deserializes JSON from an LLM response string.
    /// Strips markdown code fences and full-line comments if present.
    /// </summary>
    /// <exception cref="JsonException">Thrown when the response cannot be parsed as valid JSON.</exception>
    public static T Parse<T>(string responseText)
    {
        ArgumentNullException.ThrowIfNull(responseText);
        var json = ExtractJson(responseText);
        json = RepairCommonQuirks(json);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new JsonException($"Deserialization returned null for type {typeof(T).Name}.");
    }

    /// <summary>
    /// Attempts to extract and deserialize JSON from an LLM response string.
    /// Returns a <see cref="ParseResult{T}"/> with structured error info on failure
    /// instead of throwing.
    /// </summary>
    public static ParseResult<T> TryParse<T>(string responseText)
    {
        ArgumentNullException.ThrowIfNull(responseText);
        var extracted = ExtractJson(responseText);
        var repaired = RepairCommonQuirks(extracted);
        try
        {
            var value = JsonSerializer.Deserialize<T>(repaired, SerializerOptions)
                ?? throw new JsonException($"Deserialization returned null for type {typeof(T).Name}.");
            return ParseResult.Success<T>(value);
        }
        catch (JsonException ex)
        {
            return ParseResult.Failure<T>(ex.Message, repaired);
        }
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
                if (c == '"')
                {
                    // Count consecutive preceding backslashes — quote is escaped only when count is odd
                    int backslashes = 0;
                    for (int j = i - 1; j >= start && trimmed[j] == '\\'; j--)
                        backslashes++;
                    if (backslashes % 2 == 0)
                        inString = !inString;
                }
                if (!inString)
                {
                    if (c == '{') depth++;
                    else if (c == '}') { depth--; if (depth == 0) return trimmed[start..(i + 1)]; }
                }
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Repairs common LLM JSON quirks that would otherwise cause parse failures:
    /// <list type="bullet">
    ///   <item>Strips full-line JavaScript-style comments (lines beginning with //).</item>
    ///   <item>Quotes unquoted property names (e.g., <c>name:</c> → <c>"name":</c>).</item>
    /// </list>
    /// Inline comments are intentionally NOT stripped to avoid corrupting URLs in string values.
    /// </summary>
    private static string RepairCommonQuirks(string json)
    {
        var changed = false;

        // Strip full-line comments
        if (json.Contains("//", StringComparison.Ordinal))
        {
            var lines = json.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    lines[i] = string.Empty;
                    changed = true;
                }
            }

            if (changed)
                json = string.Join('\n', lines);
        }

        // Quote unquoted property names (e.g., LLM emits JavaScript-style object literals)
        var quoted = UnquotedKeyPattern.Replace(json, "\"$1\"");
        if (!ReferenceEquals(quoted, json))
        {
            json = quoted;
            changed = true;
        }

        return json;
    }
}

/// <summary>
/// Result of a <see cref="StructuredOutputParser.TryParse{T}"/> call,
/// carrying either the deserialized value or structured failure diagnostics.
/// </summary>
public readonly record struct ParseResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    /// <summary>The <see cref="JsonException"/> message when parsing failed.</summary>
    public string? Error { get; init; }
    /// <summary>The JSON fragment that was passed to the deserializer (after extraction and repair).</summary>
    public string? ExtractedJson { get; init; }
}

/// <summary>
/// Non-generic factory methods for <see cref="ParseResult{T}"/> (avoids CA1000).
/// </summary>
public static class ParseResult
{
    public static ParseResult<T> Success<T>(T value) =>
        new() { IsSuccess = true, Value = value };

    public static ParseResult<T> Failure<T>(string error, string extractedJson) =>
        new() { IsSuccess = false, Error = error, ExtractedJson = extractedJson };
}
