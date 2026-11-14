namespace ErpDashboard.Api.Data.Entities;

public class DocumentNumberCounter
{
    public string Series { get; set; } = "";
    public int Year { get; set; }
    public int LastNumber { get; set; }
}