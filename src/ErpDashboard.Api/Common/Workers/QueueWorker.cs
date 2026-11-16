namespace ErpDashboard.Api.Common.Workers;

public interface IQueueProcessor
{
    Task<bool> ProcessNextAsync(CancellationToken ct);
}

public sealed class QueueWorkerOptions
{
    public bool Enabled { get; init; } = true;
    [Range(1, 300)] public int PollSeconds { get; init; } = 5;
}

public sealed class QueueWorker<TProcessor>(
    IServiceScopeFactory scopes,
    IOptionsMonitor<QueueWorkerOptions> options,
    ILogger<QueueWorker<TProcessor>> logger) : BackgroundService
    where TProcessor : IQueueProcessor
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Get(typeof(TProcessor).Name);
        if (!settings.Enabled)
        {
            logger.LogInformation("{Worker} is disabled.", typeof(TProcessor).Name);
            return;
        }

        var idle = TimeSpan.FromSeconds(settings.PollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var didWork = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                didWork = await scope.ServiceProvider.GetRequiredService<TProcessor>().ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Worker} iteration failed.", typeof(TProcessor).Name);
            }

            if (!didWork)
            {
                try { await Task.Delay(idle, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}

public static class QueueWorkerRegistration
{
    public static IServiceCollection AddQueueWorker<TProcessor>(
        this IServiceCollection services, IConfiguration configuration, string section)
        where TProcessor : class, IQueueProcessor
    {
        services.AddScoped<TProcessor>();
        services.AddOptions<QueueWorkerOptions>(typeof(TProcessor).Name)
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHostedService<QueueWorker<TProcessor>>();
        return services;
    }
}