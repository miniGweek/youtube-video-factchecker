using System.Threading.Channels;
using FactChecker.Core.Pipeline;

namespace FactChecker.Web.Services;

/// <summary>
/// Hosted service that processes analysis work items from a bounded channel.
/// Replaces fire-and-forget <c>Task.Run()</c> with proper lifecycle management:
/// the host waits for in-flight work during graceful shutdown.
/// </summary>
#pragma warning disable CA1812 // Instantiated via DI (AddSingleton + AddHostedService in Program.cs)
internal sealed partial class BackgroundAnalysisRunner : BackgroundService, IAnalysisDispatcher
#pragma warning restore CA1812
{
    private readonly Channel<WorkItem> _channel = Channel.CreateBounded<WorkItem>(
        new BoundedChannelOptions(capacity: 20)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundAnalysisRunner> _logger;

    public BackgroundAnalysisRunner(
        IServiceScopeFactory scopeFactory,
        ILogger<BackgroundAnalysisRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public ValueTask EnqueueAsync(string analysisId, Uri videoUri, CancellationToken ct = default)
    {
        return _channel.Writer.TryWrite(new WorkItem(analysisId, videoUri))
            ? ValueTask.CompletedTask
            : SlowEnqueueAsync(analysisId, videoUri, ct);
    }

    private async ValueTask SlowEnqueueAsync(string analysisId, Uri videoUri, CancellationToken ct)
    {
        await _channel.Writer.WriteAsync(new WorkItem(analysisId, videoUri), ct).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogRunnerStarted();

        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var scope = _scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var pipeline = scope.ServiceProvider.GetRequiredService<AnalysisPipeline>();
                    await pipeline.RunAsync(item.AnalysisId, item.VideoUri, stoppingToken).ConfigureAwait(false);
                }
            }
#pragma warning disable CA1031 // Pipeline should never throw, but guard the reader loop
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogUnexpectedError(ex, item.AnalysisId);
            }
#pragma warning restore CA1031
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogRunnerStopping();
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct WorkItem(string AnalysisId, Uri VideoUri);

    [LoggerMessage(Level = LogLevel.Information, Message = "Background analysis runner started")]
    private partial void LogRunnerStarted();

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error processing analysis {AnalysisId}")]
    private partial void LogUnexpectedError(Exception ex, string analysisId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Background analysis runner stopping — draining queue")]
    private partial void LogRunnerStopping();
}
