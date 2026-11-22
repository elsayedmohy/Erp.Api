namespace ErpDashboard.Api.Common.Workers;

// How long a queue waits before trying a failed item again, by how many attempts it has had.
// Shared by every queue; how many attempts each queue allows is its own MaxAttempts.
public static class RetrySchedule
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(3)
    ];

    public static TimeSpan Delay(int attempts) => Delays[Math.Clamp(attempts - 1, 0, Delays.Length - 1)];
}