namespace ErpDashboard.Api.Data.Entities;

public class OverdueReminder
{
    public Guid Id { get; set; }
    public Guid ShowroomId { get; set; }

    public DateOnly Date { get; set; }

    public int InvoiceCount { get; set; }
    public decimal Amount { get; set; }
    public DateOnly OldestDueDate { get; set; }
    public Guid NotificationId { get; set; }
    public DateTime CreatedAt { get; set; }
}