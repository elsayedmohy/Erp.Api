using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Features.Finance.Invoices;

namespace ErpDashboard.Api.UnitTests;

public class InvoicePaymentStatusTests
{
    private static readonly DateOnly Due = new(2026, 12, 14);

    [Theory]
    [InlineData(0, "2026-12-14", InvoicePaymentStatus.Due)]            
    [InlineData(0, "2026-12-15", InvoicePaymentStatus.Overdue)]
    [InlineData(400, "2026-12-01", InvoicePaymentStatus.PartiallyPaid)]
    [InlineData(400, "2026-12-15", InvoicePaymentStatus.Overdue)]
    [InlineData(1000, "2026-12-01", InvoicePaymentStatus.Paid)]
    [InlineData(1000, "2027-03-01", InvoicePaymentStatus.Paid)]
    [InlineData(999.99, "2026-12-01", InvoicePaymentStatus.PartiallyPaid)]
    public void Issued_Invoice(decimal paid, string today, InvoicePaymentStatus expected)
    {
        Assert.Equal(expected,
            InvoicePaymentStatusCalculator.Compute(InvoiceStatus.Issued, 1000m, paid, 0m,Due, DateOnly.Parse(today)));
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void NonIssued_Invoice_HasNoPaymentStatus(InvoiceStatus status)
    {
        Assert.Null(InvoicePaymentStatusCalculator.Compute(status, 1000m, 0m, 0m,Due, new DateOnly(2027, 1, 1)));
    }
}