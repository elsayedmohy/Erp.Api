namespace ErpDashboard.Api.Data.Entities;

public class ReminderRun
{
    public DateOnly Date { get; set; }
    public DateTime StartedAt { get; set; }
    public int Showrooms { get; set; }
    public int Skipped { get; set; }
}