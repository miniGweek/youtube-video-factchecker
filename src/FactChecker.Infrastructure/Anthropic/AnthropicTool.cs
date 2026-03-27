using System.Text.Json.Serialization;

namespace FactChecker.Infrastructure.Anthropic;

public sealed record AnthropicTool(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("input_schema")] object InputSchema);
