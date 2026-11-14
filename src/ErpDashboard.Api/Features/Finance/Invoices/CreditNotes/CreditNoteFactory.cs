namespace ErpDashboard.Api.Features.Finance.Invoices.CreditNotes;


public static class CreditNoteFactory
{
    public static List<CreditNoteLine> BuildLines(
        IReadOnlyCollection<InvoiceLine> invoiceLines,
        IReadOnlyDictionary<Guid, decimal> alreadyCredited,
        IReadOnlyList<CreditLineInput> requested)
    {
        if (requested.Count == 0)
            throw new RequestValidationException("lines", "A credit note must have at least one line.");

        var errors = new Dictionary<string, string[]>();
        var byId = invoiceLines.ToDictionary(l => l.Id);
        var seen = new HashSet<Guid>();

        for (var i = 0; i < requested.Count; i++)
        {
            var r = requested[i];
            var key = $"lines[{i}]";

            if (!seen.Add(r.InvoiceLineId))
                errors[$"{key}.invoiceLineId"] = ["Each invoice line can appear only once."];
            else if (!byId.TryGetValue(r.InvoiceLineId, out var line))
                errors[$"{key}.invoiceLineId"] = ["The line does not belong to this invoice."];
            else if (r.NetAmount <= 0 || r.NetAmount != Math.Round(r.NetAmount, 2))
                errors[$"{key}.netAmount"] = ["Amount must be positive with at most 2 decimal places."];
            else
            {
                var remaining = InvoiceFactory.LineNet(line) - alreadyCredited.GetValueOrDefault(line.Id);
                if (r.NetAmount > remaining)
                    errors[$"{key}.netAmount"] = [$"Only {remaining:0.00} of this line can still be credited."];
            }
        }

        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        return requested.Select(r => new CreditNoteLine
        {
            Id = Guid.NewGuid(),
            InvoiceLineId = r.InvoiceLineId,
            NetAmount = r.NetAmount,
            TaxRate = byId[r.InvoiceLineId].TaxRate
        }).ToList();
    }

    public static decimal LineTax(CreditNoteLine l) =>
        Math.Round(l.NetAmount * l.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);

    public static decimal Total(IEnumerable<CreditNoteLine> lines) =>
        lines.Sum(l => l.NetAmount + LineTax(l));
}