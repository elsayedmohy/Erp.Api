using ErpDashboard.Api.Features.Payments.Webhooks;

namespace ErpDashboard.Api.UnitTests;

public class RetryScheduleTests
{
    [Fact]
    public void Delays_GrowAndCap()
    {
        var delays = Enumerable.Range(1, PaymentWebhookProcessor.MaxAttempts + 2).Select(RetrySchedule.Delay).ToList();

        Assert.Equal(TimeSpan.FromSeconds(10), delays[0]);
        Assert.True(delays.Zip(delays.Skip(1)).All(p => p.Second >= p.First));
        Assert.Equal(TimeSpan.FromHours(3), delays[^1]);
    }
}