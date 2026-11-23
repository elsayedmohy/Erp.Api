namespace ErpDashboard.Api.Features.Finance.Invoices;

public static class InvoicePaymentRules
{
    // Credit notes reduce what is owed exactly like payments do.
    private static readonly Expression<Func<InvoiceBalance, DateOnly, InvoicePaymentStatus?>> Rule =
        (b, today) =>
            b.Invoice.Status != InvoiceStatus.Issued ? (InvoicePaymentStatus?)null
            : b.Paid + b.Credited >= b.Invoice.TotalAmount ? InvoicePaymentStatus.Paid
            : today > b.Invoice.DueDate ? InvoicePaymentStatus.Overdue
            : b.Paid + b.Credited > 0 ? InvoicePaymentStatus.PartiallyPaid
            : InvoicePaymentStatus.Due;

    private static readonly Func<InvoiceBalance, DateOnly, InvoicePaymentStatus?> Compiled = Rule.Compile();

    /// Paid and credited summed in SQL.
    public static IQueryable<InvoiceBalance> WithBalance(this IQueryable<Invoice> invoices) =>
        invoices.Select(i => new InvoiceBalance
        {
            Invoice = i,
            Paid = i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m,
            Credited = i.CreditNotes.Sum(c => (decimal?)c.TotalAmount) ?? 0m
        });
    
    public static IQueryable<InvoiceBalance> AllBalances(this AppDbContext db)
    {
        var paid = db.InvoicePayments
            .GroupBy(p => p.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Amount = g.Sum(p => p.Amount) });
        var credited = db.CreditNotes
            .GroupBy(c => c.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Amount = g.Sum(c => c.TotalAmount) });

        return
            from i in db.Invoices.AsNoTracking()
            join p in paid on i.Id equals p.InvoiceId into ps
            from p in ps.DefaultIfEmpty()
            join c in credited on i.Id equals c.InvoiceId into cs
            from c in cs.DefaultIfEmpty()
            select new InvoiceBalance
            {
                Invoice = i,
                Paid = (decimal?)p.Amount ?? 0m,
                Credited = (decimal?)c.Amount ?? 0m
            };
    }

    /// For SQL: b => Rule(b, today) == status, with the rule written into the query.
    public static Expression<Func<InvoiceBalance, bool>> HasStatus(InvoicePaymentStatus status, DateOnly today)
    {
        var balance = Rule.Parameters[0];
        var body = new Replace(Rule.Parameters[1], Expression.Constant(today)).Visit(Rule.Body);
        return Expression.Lambda<Func<InvoiceBalance, bool>>(
            Expression.Equal(body, Expression.Constant(status, typeof(InvoicePaymentStatus?))), balance);
    }

    /// For memory: an invoice loaded with its Payments and CreditNotes.
    public static InvoiceBalance BalanceOf(Invoice invoice) => new()
    {
        Invoice = invoice,
        Paid = invoice.Payments.Sum(p => p.Amount),
        Credited = invoice.CreditNotes.Sum(c => c.TotalAmount)
    };

    public static InvoicePaymentStatus? StatusOf(InvoiceBalance balance, DateOnly today) => Compiled(balance, today);

    /// Positive: still owed. Negative: more came in than the total, and the difference is a refund due.
    public static decimal Remaining(InvoiceBalance b) => b.Invoice.TotalAmount - b.Paid - b.Credited;

    private sealed class Replace(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}