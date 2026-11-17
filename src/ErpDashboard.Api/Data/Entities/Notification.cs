namespace ErpDashboard.Api.Data.Entities;

public class Notification
{
    public Guid Id { get; set; }
    public string Channel { get; set; } = "whatsapp";
    public string? Recipient { get; set; }
    public string Body { get; set; } = "";
    public Guid? CheckoutId { get; set; }
    public NotificationStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
}