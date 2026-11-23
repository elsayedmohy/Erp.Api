using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Features.Finance.Invoices;

namespace ErpDashboard.Api.UnitTests;

public class InvoicePaymentStatusTests
{
    private static readonly DateOnly Due = new(2026, 12, 14);

    private static InvoicePaymentStatus? Status(
        decimal paid, decimal credited, string today, InvoiceStatus status = InvoiceStatus.Issued) =>
        InvoicePaymentRules.StatusOf(new InvoiceBalance
        {
            Invoice = new Invoice { Status = status, TotalAmount = 1000m, DueDate = Due },
            Paid = paid,
            Credited = credited
        }, DateOnly.Parse(today));

    [Theory]
    [InlineData(0, 0, "2026-12-14", InvoicePaymentStatus.Due)]
    [InlineData(0, 0, "2026-12-15", InvoicePaymentStatus.Overdue)]
    [InlineData(400, 0, "2026-12-01", InvoicePaymentStatus.PartiallyPaid)]
    [InlineData(400, 0, "2026-12-15", InvoicePaymentStatus.Overdue)]
    [InlineData(1000, 0, "2026-12-01", InvoicePaymentStatus.Paid)]
    [InlineData(1000, 0, "2027-03-01", InvoicePaymentStatus.Paid)]
    [InlineData(999.99, 0, "2026-12-01", InvoicePaymentStatus.PartiallyPaid)]
    [InlineData(0, 250, "2026-12-01", InvoicePaymentStatus.PartiallyPaid)]   // was Due: credits count
    [InlineData(500, 250, "2026-12-01", InvoicePaymentStatus.PartiallyPaid)] // list said Paid: credits counted twice
    [InlineData(500, 500, "2026-12-15", InvoicePaymentStatus.Paid)]
    [InlineData(900, 200, "2026-12-01", InvoicePaymentStatus.Paid)]          // over-covered: refund due
    public void Issued_Invoice(decimal paid, decimal credited, string today, InvoicePaymentStatus expected) =>
        Assert.Equal(expected, Status(paid, credited, today));

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void NonIssued_Invoice_HasNoPaymentStatus_EvenWhenCovered(InvoiceStatus status) =>
        Assert.Null(Status(1000m, 0m, "2027-01-01", status));   // the old calculator said Paid
}