namespace ErpDashboard.Api.Features.Finance.Invoices;

public static class InvoicePaymentStatusCalculator
{
    public static InvoicePaymentStatus? Compute(
        InvoiceStatus status, decimal total, decimal paid,decimal credited, DateOnly dueDate, DateOnly today)
    {
        if (paid + credited >= total) return InvoicePaymentStatus.Paid;
        if (status != InvoiceStatus.Issued) return null;
        if (paid >= total) return InvoicePaymentStatus.Paid;
        if (today > dueDate) return InvoicePaymentStatus.Overdue;
        if (paid > 0) return InvoicePaymentStatus.PartiallyPaid;
        return InvoicePaymentStatus.Due;
    }
}