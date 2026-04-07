using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FactChecker.Infrastructure.LlmProviders.Common;

/// <summary>
/// Parses structured JSON output from LLM responses.
/// Handles common quirks: markdown code fences, leading/trailing whitespace,
/// full-line JavaScript-style comments, unquoted property names.
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
        // Strip full-line comments
        if (json.Contains("//", StringComparison.Ordinal))
        {
            var lines = json.Split('\n');
            var anyStripped = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    lines[i] = string.Empty;
                    anyStripped = true;
                }
            }
            if (anyStripped)
                json = string.Join('\n', lines);
        }

        // Quote unquoted property names using a context-aware scanner that
        // correctly skips content inside quoted string values. The previous
        // regex approach would corrupt strings like "study, paragraph: ..."
        // because it matched identifiers inside string values when preceded
        // by a comma, turning the value into invalid JSON.
        return QuoteUnquotedKeys(json);
    }

    /// <summary>
    /// Scans <paramref name="json"/> character-by-character, quoting any
    /// unquoted property name (identifier immediately followed by <c>:</c>)
    /// that appears after a <c>{</c> or <c>,</c> outside of a string value.
    /// </summary>
    private static string QuoteUnquotedKeys(string json)
    {
        var sb = new StringBuilder(json.Length + 64);
        int i = 0;
        int len = json.Length;

        while (i < len)
        {
            char c = json[i];

            // Copy quoted strings verbatim, respecting escape sequences.
            // This prevents the key-detection logic below from firing on
            // content like "some text, paragraph: details" where the comma
            // is inside a value, not a JSON property separator.
            if (c == '"')
            {
                sb.Append(c);
                i++;
                while (i < len)
                {
                    char sc = json[i];
                    sb.Append(sc);
                    if (sc == '\\' && i + 1 < len)
                    {
                        i++;
                        sb.Append(json[i]); // escaped character, copy verbatim
                    }
                    else if (sc == '"')
                    {
                        break; // end of string
                    }
                    i++;
                }
                i++;
                continue;
            }

            // After '{' or ',' we may be at the start of a property name.
            if (c == '{' || c == ',')
            {
                sb.Append(c);
                i++;

                // Capture any whitespace between delimiter and the next token.
                int wsStart = i;
                while (i < len && json[i] is ' ' or '\t' or '\r' or '\n')
                    i++;

                // If the next char is a letter or '_' and not a quote/bracket,
                // it could be an unquoted key.
                if (i < len && json[i] != '"' && json[i] != '}' && json[i] != ']'
                    && (char.IsLetter(json[i]) || json[i] == '_'))
                {
                    int idStart = i;
                    while (i < len && (char.IsLetterOrDigit(json[i]) || json[i] == '_'))
                        i++;
                    int idEnd = i;

                    // Skip optional whitespace between identifier and potential colon.
                    int ws2Start = i;
                    while (i < len && json[i] is ' ' or '\t')
                        i++;

                    if (i < len && json[i] == ':')
                    {
                        // Confirmed unquoted key — emit with surrounding quotes.
                        sb.Append(json, wsStart, idStart - wsStart); // leading whitespace
                        sb.Append('"');
                        sb.Append(json, idStart, idEnd - idStart);   // key text
                        sb.Append('"');
                        sb.Append(json, ws2Start, i - ws2Start);     // whitespace before ':'
                        // The ':' will be appended by the next outer-loop iteration.
                        continue;
                    }

                    // Not a key (no colon follows) — emit everything we consumed.
                    sb.Append(json, wsStart, i - wsStart);
                    continue;
                }

                // Next token is a quote, bracket, or non-identifier — emit whitespace as-is.
                sb.Append(json, wsStart, i - wsStart);
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
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
