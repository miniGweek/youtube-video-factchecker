using FactChecker.Core.Enums;
using FactChecker.Infrastructure.Anthropic.Stages;

namespace FactChecker.Infrastructure.Tests.Anthropic.Stages;

public class AnthropicSummariserTests : StageTestBase
{
    [Fact]
    public async Task SummariseAsync_ValidResponse_ReturnsThesisAndKeyPoints()
    {
        var wrapper = CreateWrapper(LoadFixture("summary_response.json"));
        var summariser = new AnthropicSummariser(wrapper);

        var result = await summariser.SummariseAsync("Transcript about exercise...", ContentDomain.Health);

        Assert.False(string.IsNullOrEmpty(result.Thesis));
        Assert.NotEmpty(result.KeyPoints);
        Assert.Equal(ContentDomain.Health, result.Domain);
    }

    [Fact]
    public async Task SummariseAsync_ValidResponse_ThesisMatchesFixture()
    {
        var wrapper = CreateWrapper(LoadFixture("summary_response.json"));
        var summariser = new AnthropicSummariser(wrapper);

        var result = await summariser.SummariseAsync("Transcript about exercise...", ContentDomain.Health);

        Assert.Equal(
            "Regular exercise significantly reduces the risk of cardiovascular disease.",
            result.Thesis);
        Assert.Equal(3, result.KeyPoints.Count);
    }

    [Fact]
    public async Task SummariseAsync_DomainPassedInContext_PreservedOnResult()
    {
        var wrapper = CreateWrapper(LoadFixture("summary_response.json"));
        var summariser = new AnthropicSummariser(wrapper);

        var result = await summariser.SummariseAsync("Some transcript", ContentDomain.Finance);

        Assert.Equal(ContentDomain.Finance, result.Domain);
    }
}
