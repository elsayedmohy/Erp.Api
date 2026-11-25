namespace ErpDashboard.Api.Features.Finance.Invoices;

public interface IInvoiceSettlement
{
    Task<InvoiceDetailsResponse> RecordPaymentAsync(Guid id, RecordPaymentRequest request, Guid userId, CancellationToken ct);
    Task<CreditNoteResponse> IssueCreditNoteAsync(Guid invoiceId, CreateCreditNoteRequest request, Guid userId, CancellationToken ct);
}

public sealed class InvoiceSettlement(
    AppDbContext dbContext,
    IBusinessClock clock,
    IDocumentNumberGenerator numbers) : IInvoiceSettlement
{
    public async Task<InvoiceDetailsResponse> RecordPaymentAsync(
        Guid id, RecordPaymentRequest request, Guid userId, CancellationToken ct)
    {
        if (request.Amount != Math.Round(request.Amount, 2))
            throw new RequestValidationException("amount", "Amount cannot have more than 2 decimal places.");
        if (request.Method == PaymentMethod.Online)
            throw new RequestValidationException("method", "Online payments are recorded automatically.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await dbContext.LockInvoiceAsync(id, ct);

        var invoice = await dbContext.Invoices
                          .Include(i => i.InvoiceLines)
                          .Include(i => i.Payments)
                          .Include(i => i.CreditNotes).ThenInclude(c => c.Lines)
                          .FirstOrDefaultAsync(i => i.Id == id, ct)
                      ?? throw new NotFoundException(nameof(Invoice), id);

        if (invoice.Status != InvoiceStatus.Issued)
            throw new ConflictException($"Only issued invoices can be paid. Current status: {invoice.Status}.");

        var today = clock.Today;
        var paymentDate = request.PaymentDate ?? today;
        if (paymentDate < invoice.InvoiceDate)
            throw new RequestValidationException("paymentDate", "Payment date cannot be before the invoice date.");
        if (paymentDate > today)
            throw new RequestValidationException("paymentDate", "Payment date cannot be in the future.");

        var remaining = InvoicePaymentRules.Remaining(InvoicePaymentRules.BalanceOf(invoice));
        if (request.Amount > remaining)
            throw new ConflictException(
                $"Payment of {request.Amount:0.00} exceeds the remaining balance of {remaining:0.00}.");

        dbContext.Set<InvoicePayment>().Add(new InvoicePayment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Amount = request.Amount,
            PaymentDate = paymentDate,
            Method = request.Method!.Value,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            RecordedById = userId,
            RecordedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var showroomName = await dbContext.ShowroomNameAsync(invoice.ShowroomId, ct);
        return invoice.ToDetailsResponse(showroomName, today);
    }

    public async Task<CreditNoteResponse> IssueCreditNoteAsync(
        Guid invoiceId, CreateCreditNoteRequest request, Guid userId, CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await dbContext.LockInvoiceAsync(invoiceId, ct);

        var invoice = await dbContext.Invoices
                          .Include(i => i.InvoiceLines)
                          .Include(i => i.CreditNotes).ThenInclude(c => c.Lines)
                          .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException(nameof(Invoice), invoiceId);

        if (invoice.Status != InvoiceStatus.Issued)
            throw new ConflictException(
                $"Credit notes can only be issued for issued invoices. Current status: {invoice.Status}.");

        var alreadyCredited = invoice.CreditNotes
            .SelectMany(c => c.Lines)
            .GroupBy(l => l.InvoiceLineId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.NetAmount));

        var lines = CreditNoteFactory.BuildLines(
            invoice.InvoiceLines,
            alreadyCredited,
            request.Lines.Select(l => new CreditLineInput(l.InvoiceLineId!.Value, l.NetAmount)).ToList());

        var today = clock.Today;
        var creditNote = new CreditNote
        {
            Id = Guid.NewGuid(),
            Number = await numbers.NextAsync("CN", today.Year, ct),
            InvoiceId = invoiceId,
            CreditDate = today,
            Reason = request.Reason.Trim(),
            TotalAmount = CreditNoteFactory.Total(lines),
            IssuedById = userId,
            IssuedAt = DateTime.UtcNow,
            Lines = lines
        };

        dbContext.CreditNotes.Add(creditNote);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return creditNote.ToResponse();
    }
}
