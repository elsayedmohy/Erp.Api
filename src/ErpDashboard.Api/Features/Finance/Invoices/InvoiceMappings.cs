namespace ErpDashboard.Api.Features.Finance.Invoices;

public static class InvoiceMappings
{
    public static InvoiceResponse ToResponse(this InvoiceBalance b, DateOnly today)
    {
        var i = b.Invoice;
        var remaining = InvoicePaymentRules.Remaining(b);
        return new InvoiceResponse(
            i.Id, i.OrderId, i.ShowroomId, i.InvoiceDate, i.DueDate, i.Status, i.TotalAmount, i.Number,
            b.Paid, b.Credited, remaining, Math.Max(0, -remaining), InvoicePaymentRules.StatusOf(b, today));
    }

    public static InvoiceDetailsResponse ToDetailsResponse(this Invoice i, string showroomName, DateOnly today)
    {
        var lines = i.InvoiceLines.Select(l =>
        {
            var gross = InvoiceFactory.LineGross(l);
            var net = InvoiceFactory.LineNet(l);
            var tax = InvoiceFactory.LineTax(l);
            return new InvoiceLineResponse(l.Id, l.ProductId, l.Description, l.Quantity,
                l.UnitPrice, gross, l.Discount, l.TaxRate, net, tax, net + tax);
        }).ToList();

        var payments = i.Payments
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.RecordedAt)
            .Select(p => new InvoicePaymentResponse(
                p.Id, p.Amount, p.PaymentDate, p.Method, p.Reference, p.RecordedById, p.RecordedAt))
            .ToList();

        var creditNotes = i.CreditNotes.OrderBy(c => c.Number).Select(c => c.ToResponse()).ToList();

        var balance = InvoicePaymentRules.BalanceOf(i);
        var remaining = InvoicePaymentRules.Remaining(balance);
        return new InvoiceDetailsResponse(
            i.Id, i.OrderId, i.ShowroomId, showroomName,
            i.InvoiceDate, i.DueDate, i.Status,
            Subtotal: lines.Sum(x => x.LineGross),
            DiscountTotal: lines.Sum(x => x.Discount),
            TaxAmount: lines.Sum(x => x.LineTax),
            TotalAmount: i.TotalAmount,
            Number: i.Number,
            AmountPaid: balance.Paid,
            CreditedAmount: balance.Credited,
            Balance: remaining,
            RefundDue: Math.Max(0, -remaining),
            PaymentStatus: InvoicePaymentRules.StatusOf(balance, today),
            Lines: lines,
            Payments: payments,
            CreditNotes: creditNotes);
    }

    public static CreditNoteResponse ToResponse(this CreditNote c)
    {
        var net = c.Lines.Sum(l => l.NetAmount);
        var tax = c.Lines.Sum(CreditNoteFactory.LineTax);
        return new CreditNoteResponse(c.Id, c.Number, c.InvoiceId, c.CreditDate, c.Reason, net, tax, c.TotalAmount);
    }
}
