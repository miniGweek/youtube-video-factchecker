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
}
