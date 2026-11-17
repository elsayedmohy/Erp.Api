namespace ErpDashboard.Api.Features.Notifications;

public interface IWhatsAppSender
{
    Task<WhatsAppSendResult> SendTextAsync(string recipient, string text, CancellationToken ct);
}

public sealed record WhatsAppSendResult(NotificationStatus Status, string? MessageId, string? Error)
{
    public static WhatsAppSendResult Sent(string? id) => new(NotificationStatus.Sent, id, null);
    public static WhatsAppSendResult Rejected(string error) => new(NotificationStatus.Failed, null, error);
    public static WhatsAppSendResult Skipped(string reason) => new(NotificationStatus.Skipped, null, reason);
}

public sealed class WhatsAppUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class NotConfiguredWhatsAppSender : IWhatsAppSender
{
    public Task<WhatsAppSendResult> SendTextAsync(string recipient, string text, CancellationToken ct) =>
        Task.FromResult(WhatsAppSendResult.Skipped("WhatsApp is not configured."));
}