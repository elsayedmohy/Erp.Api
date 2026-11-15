
namespace ErpDashboard.Api.Features.Payments.Webhooks;

public sealed class PaymentWebhookWorkerOptions
{
    public const string Section = "Payments:Webhooks";
    public bool Enabled { get; init; } = true;
    [Range(1, 300)] public int PollSeconds { get; init; } = 5;
}

public sealed class PaymentWebhookWorker(
    IServiceScopeFactory scopes,
    IOptions<PaymentWebhookWorkerOptions> options,
    ILogger<PaymentWebhookWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Payment webhook worker is disabled.");
            return;
        }

        var idle = TimeSpan.FromSeconds(options.Value.PollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var didWork = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                didWork = await scope.ServiceProvider
                    .GetRequiredService<PaymentWebhookProcessor>()
                    .ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Payment webhook worker iteration failed.");
            }

            if (!didWork)
            {
                try { await Task.Delay(idle, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}