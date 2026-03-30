using FactChecker.Core.Enums;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Stages;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Stages;

public class SummariserStageTests
{
    private const string ValidResponse = """
        {
          "thesis": "Regular exercise significantly reduces the risk of cardiovascular disease.",
          "keyPoints": [
            "30 minutes of moderate exercise 5 days a week reduces heart disease risk by 35%",
            "Both aerobic and resistance training provide cardiovascular benefits",
            "Exercise improves cholesterol levels and blood pressure"
          ]
        }
        """;

    [Fact]
    public async Task SummariseAsync_ValidResponse_ReturnsThesisAndKeyPoints()
    {
        var client = new StubLlmClient().WithCompleteResponse(ValidResponse);
        var summariser = new SummariserStage(client, StageTestHelper.CreateOptions());

        var result = await summariser.SummariseAsync("Transcript about exercise...", ContentDomain.Health);

        Assert.False(string.IsNullOrEmpty(result.Thesis));
        Assert.NotEmpty(result.KeyPoints);
        Assert.Equal(ContentDomain.Health, result.Domain);
    }

    [Fact]
    public async Task SummariseAsync_ValidResponse_ThesisMatchesResponse()
    {
        var client = new StubLlmClient().WithCompleteResponse(ValidResponse);
        var summariser = new SummariserStage(client, StageTestHelper.CreateOptions());

        var result = await summariser.SummariseAsync("Transcript about exercise...", ContentDomain.Health);

        Assert.Equal(
            "Regular exercise significantly reduces the risk of cardiovascular disease.",
            result.Thesis);
        Assert.Equal(3, result.KeyPoints.Count);
    }

    [Fact]
    public async Task SummariseAsync_DomainPassedInContext_PreservedOnResult()
    {
        var client = new StubLlmClient().WithCompleteResponse(ValidResponse);
        var summariser = new SummariserStage(client, StageTestHelper.CreateOptions());

        var result = await summariser.SummariseAsync("Some transcript", ContentDomain.Finance);

        Assert.Equal(ContentDomain.Finance, result.Domain);
    }

    [Fact]
    public async Task SummariseAsync_UsesCorrectModelTier()
    {
        var options = StageTestHelper.CreateOptions(new StageModelOptions { Summarisation = ModelTier.Standard });
        var client = new StubLlmClient().WithCompleteResponse(ValidResponse);
        var summariser = new SummariserStage(client, options);

        await summariser.SummariseAsync("Transcript...", ContentDomain.Health);

        Assert.NotNull(client.LastRequest);
        Assert.Equal(ModelTier.Standard, client.LastRequest.Tier);
    }

    [Fact]
    public async Task SummariseAsync_SetsCorrectStageId()
    {
        var client = new StubLlmClient().WithCompleteResponse(ValidResponse);
        var summariser = new SummariserStage(client, StageTestHelper.CreateOptions());

        await summariser.SummariseAsync("Transcript...", ContentDomain.Health);

        Assert.NotNull(client.LastRequest);
        Assert.Equal("Summarisation", client.LastRequest.StageId);
    }
}
