namespace ErpDashboard.Api.Features.Finance.Invoices;

public sealed record InvoiceLineInput(
    Guid? ProductId,
    string Description,
    int Quantity,
    decimal UnitPrice,
    decimal TaxRate,
    decimal Discount = 0m);

public static class InvoiceFactory
{
    public static Invoice CreateDraft(Guid ShowroomId, DateOnly invoiceDate, DateOnly dueDate,
        IReadOnlyCollection<InvoiceLineInput> lines)
    {
        ValidateDates(invoiceDate, dueDate);
        var invoiceLines = BuildLines(lines);

        return new Invoice
        {
            ShowroomId = ShowroomId,
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
            throw new RequestValidationException("lines", "An invoice must have at least one line.");

        var errors = new Dictionary<string, string[]>();
        var index = 0;
        foreach (var l in lines)
        {
            if (l.Discount != Math.Round(l.Discount, 2))
                errors[$"lines[{index}].discount"] = ["Discount cannot have more than 2 decimal places."];
            else if (l.Discount > Round(l.Quantity * l.UnitPrice))
                errors[$"lines[{index}].discount"] = ["Discount cannot exceed the line amount."];
            index++;
        }
        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        return lines.Select(l => new InvoiceLine
        {
            ProductId = l.ProductId,
            Description = l.Description.Trim(),
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            Discount = l.Discount,
            TaxRate = l.TaxRate
        }).ToList();
    }

    public static void ValidateDates(DateOnly invoiceDate, DateOnly dueDate)
    {
        if (dueDate < invoiceDate)
            throw new RequestValidationException("dueDate", "Due date cannot be before the invoice date.");
    }

    public static decimal CalculateTotal(IEnumerable<InvoiceLine> lines) =>
        lines.Sum(l => LineNet(l) + LineTax(l));

    public static decimal LineGross(InvoiceLine l) => Round(l.Quantity * l.UnitPrice);

    public static decimal LineNet(InvoiceLine l) => LineGross(l) - l.Discount;

    public static decimal LineTax(InvoiceLine l) => Round(LineNet(l) * l.TaxRate / 100m);

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}