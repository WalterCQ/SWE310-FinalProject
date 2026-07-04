namespace TaskFlow.AgentWorker;

public class AgentWorkerService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AgentWorkerService> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(2, configuration.GetValue("AgentWorker:PollIntervalSeconds", 5)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<AgentJobProcessor>();
                await processor.ProcessNextAsync(workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Agent worker loop failed.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}
