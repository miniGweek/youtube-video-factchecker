using System.Text.Json;
using FactChecker.Infrastructure.LlmProviders.Common;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Common;

public class StructuredOutputParserTests
{
    private record TestPayload(string Name, int Value);

    // ── happy path parsing ───────────────────────────────────────────────────

    [Fact]
    public void Parse_CleanJson_Deserializes()
    {
        var json = """{"name":"test","value":42}""";

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Parse_JsonWithLeadingAndTrailingWhitespace_Deserializes()
    {
        var json = """   {"name":"spaced","value":1}   """;

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("spaced", result.Name);
    }

    [Fact]
    public void Parse_JsonInMarkdownFenceWithLanguage_Deserializes()
    {
        var json = """
            ```json
            {"name":"fenced","value":7}
            ```
            """;

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("fenced", result.Name);
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public void Parse_JsonInMarkdownFenceWithoutLanguage_Deserializes()
    {
        var json = """
            ```
            {"name":"plain","value":3}
            ```
            """;

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("plain", result.Name);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnPropertyNames()
    {
        var json = """{"Name":"case","Value":99}""";

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("case", result.Name);
        Assert.Equal(99, result.Value);
    }

    // ── ExtractJson ──────────────────────────────────────────────────────────

    [Fact]
    public void ExtractJson_NoFences_ReturnsTrimmed()
    {
        var input = "  {\"key\":\"val\"}  ";
        Assert.Equal("{\"key\":\"val\"}", StructuredOutputParser.ExtractJson(input));
    }

    [Fact]
    public void ExtractJson_WithJsonFence_ReturnsInnerContent()
    {
        var input = "```json\n{\"key\":\"val\"}\n```";
        Assert.Equal("{\"key\":\"val\"}", StructuredOutputParser.ExtractJson(input));
    }

    [Fact]
    public void ExtractJson_WithPlainFence_ReturnsInnerContent()
    {
        var input = "```\n{\"key\":\"val\"}\n```";
        Assert.Equal("{\"key\":\"val\"}", StructuredOutputParser.ExtractJson(input));
    }

    [Fact]
    public void ExtractJson_FenceWithTrailingText_ReturnsInnerContent()
    {
        var input = "```json\n{\"key\":\"val\"}\n```\n\nNote: this is extra text the LLM appended.";
        Assert.Equal("{\"key\":\"val\"}", StructuredOutputParser.ExtractJson(input));
    }

    [Fact]
    public void ExtractJson_FenceWithLeadingProse_ReturnsInnerContent()
    {
        var input = "Here is the verification result:\n```json\n{\"key\":\"val\"}\n```";
        Assert.Equal("{\"key\":\"val\"}", StructuredOutputParser.ExtractJson(input));
    }

    [Fact]
    public void ExtractJson_NoFence_JsonEmbeddedInProse_ExtractsJsonObject()
    {
        var input = "The answer is {\"name\":\"test\",\"value\":1} as shown.";
        Assert.Equal("{\"name\":\"test\",\"value\":1}", StructuredOutputParser.ExtractJson(input));
    }

    [Fact]
    public void Parse_JsonWithTrailingComma_Deserializes()
    {
        var json = """{"name":"trailing","value":5,}""";

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("trailing", result.Name);
        Assert.Equal(5, result.Value);
    }

    // ── brace-counting edge cases ──────────────────────────────────────────

    [Fact]
    public void ExtractJson_EscapedBackslashBeforeQuote_HandledCorrectly()
    {
        // \\" in JSON means escaped backslash followed by unescaped quote (end of string)
        // The parser must not treat the quote as still inside the string
        var input = """Some text {"name":"path\\","value":1} done""";
        Assert.Equal("""{"name":"path\\","value":1}""", StructuredOutputParser.ExtractJson(input));
    }

    [Fact]
    public void ExtractJson_SingleEscapedQuoteInString_StaysInString()
    {
        // \" is an escaped quote — should stay inside the string
        var input = """{"name":"say \"hello\"","value":2}""";
        Assert.Equal("""{"name":"say \"hello\"","value":2}""", StructuredOutputParser.ExtractJson(input));
    }

    // ── unquoted property names ────────────────────────────────────────────

    [Fact]
    public void Parse_UnquotedPropertyNames_QuotesAndDeserializes()
    {
        var json = """{ name: "test", value: 42 }""";

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Parse_MixedQuotedAndUnquotedKeys_Deserializes()
    {
        var json = """
            {
              "name": "mixed",
              value: 7
            }
            """;

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("mixed", result.Name);
        Assert.Equal(7, result.Value);
    }

    // ── error cases ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_InvalidJson_ThrowsJsonException()
    {
        var notJson = "This is not JSON at all.";

        Assert.Throws<JsonException>(() => StructuredOutputParser.Parse<TestPayload>(notJson));
    }

    [Fact]
    public void Parse_EmptyString_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => StructuredOutputParser.Parse<TestPayload>(string.Empty));
    }

    // ── comment stripping ────────────────────────────────────────────────────

    [Fact]
    public void Parse_JsonWithFullLineComment_StripsAndDeserializes()
    {
        var json = """
            // this is a full-line comment
            {"name":"commented","value":10}
            """;

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("commented", result.Name);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public void Parse_JsonWithCommentBetweenProperties_StripsAndDeserializes()
    {
        var json = """
            {
              // verdicts section
              "name": "multiline",
              // end
              "value": 42
            }
            """;

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("multiline", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Parse_JsonWithUrlContainingDoubleSlash_PreservesUrl()
    {
        // Ensure "//" inside a string value is NOT stripped as a comment
        var json = """{"name":"https://example.com","value":1}""";

        var result = StructuredOutputParser.Parse<TestPayload>(json);

        Assert.Equal("https://example.com", result.Name);
    }

    // ── TryParse ─────────────────────────────────────────────────────────────

    [Fact]
    public void TryParse_ValidJson_ReturnsSuccess()
    {
        var json = """{"name":"ok","value":7}""";

        var result = StructuredOutputParser.TryParse<TestPayload>(json);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("ok", result.Value.Name);
        Assert.Null(result.Error);
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsFailureWithError()
    {
        var result = StructuredOutputParser.TryParse<TestPayload>("this is not json");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsExtractedJsonFragment()
    {
        var result = StructuredOutputParser.TryParse<TestPayload>("not json at all");

        Assert.False(result.IsSuccess);
        // ExtractedJson holds what the parser actually tried to deserialize
        Assert.NotNull(result.ExtractedJson);
    }
}
