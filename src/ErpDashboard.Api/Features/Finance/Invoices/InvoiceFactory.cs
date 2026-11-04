namespace ErpDashboard.Api.Features.Finance.Invoices;

public sealed record InvoiceLineInput(
    Guid? ProductId,
    string Description,
    int Quantity,
    decimal UnitPrice,
    decimal TaxRate);

public static class InvoiceFactory
{
    public static Invoice CreateDraft(Guid customerId, DateOnly invoiceDate, DateOnly dueDate,
        IReadOnlyCollection<InvoiceLineInput> lines)
    {
        ValidateDates(invoiceDate, dueDate);
        var invoiceLines = BuildLines(lines);

        return new Invoice
        {
            CustomerId = customerId,
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            Status = InvoiceStatus.Draft,
            InvoiceLines = invoiceLines,
            TotalAmount = CalculateTotal(invoiceLines)
        };
    }

    public static List<InvoiceLine> BuildLines(IReadOnlyCollection<InvoiceLineInput> lines)
    {
        if (lines.Count == 0)
            throw new BadRequestException("An invoice must have at least one line.");

        return lines.Select(l => new InvoiceLine
        {
            ProductId = l.ProductId,
            Description = l.Description.Trim(),
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            TaxRate = l.TaxRate
        }).ToList();
    }

    public static void ValidateDates(DateOnly invoiceDate, DateOnly dueDate)
    {
        if (dueDate < invoiceDate)
            throw new BadRequestException("Due date cannot be before the invoice date.");
    }

    public static decimal CalculateTotal(IEnumerable<InvoiceLine> lines) =>
        lines.Sum(l => LineNet(l) + LineTax(l));

    public static decimal LineNet(InvoiceLine l) =>
        Round(l.Quantity * l.UnitPrice);

    public static decimal LineTax(InvoiceLine l) =>
        Round(LineNet(l) * l.TaxRate / 100m);

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}