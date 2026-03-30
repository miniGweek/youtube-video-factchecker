using FactChecker.Core.Options;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Stages;

/// <summary>
/// Shared helpers for provider-agnostic stage tests.
/// Provides factory methods to avoid namespace conflicts between
/// Microsoft.Extensions.Options.Options and FactChecker.Infrastructure.Options.
/// </summary>
internal static class StageTestHelper
{
    public static IOptions<StageModelOptions> CreateOptions(StageModelOptions? options = null) =>
        Microsoft.Extensions.Options.Options.Create(options ?? new StageModelOptions());

    public static IOptions<AnalysisOptions> CreateAnalysisOptions(AnalysisOptions? options = null) =>
        Microsoft.Extensions.Options.Options.Create(options ?? new AnalysisOptions());
}
