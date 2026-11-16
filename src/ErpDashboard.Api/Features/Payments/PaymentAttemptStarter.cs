namespace ErpDashboard.Api.Features.Payments;

public interface IPaymentAttemptStarter
{
    Task<CheckoutSession> StartAsync(PaymentAttempt attempt, string description, PayerDetails payer, CancellationToken ct);
}

public sealed class PaymentAttemptStarter(AppDbContext dbContext, IPaymentGateway gateway) : IPaymentAttemptStarter
{
    public async Task<CheckoutSession> StartAsync(
        PaymentAttempt attempt, string description, PayerDetails payer, CancellationToken ct)
    {
        attempt.Provider = gateway.Name;
        if (dbContext.Entry(attempt).State == EntityState.Detached)
            dbContext.PaymentAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(ct);

        try
        {
            var session = await gateway.CreateCheckoutAsync(
                new CheckoutRequest(attempt.Id, attempt.AmountMinor, attempt.Currency, description, payer), ct);

            attempt.ProviderReference = session.ProviderReference;
            attempt.CheckoutUrl = session.CheckoutUrl.ToString();
            await dbContext.SaveChangesAsync(ct);
            return session;
        }
        catch (PaymentProviderUnavailableException ex)
        {
            attempt.Status = PaymentAttemptStatus.Failed;
            attempt.FailureReason = ex.Message;
            attempt.CompletedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}