namespace ErpDashboard.Api.Features.Notifications;

public interface ICheckoutNotifier
{
    Task SetMessageAsync(Guid checkoutId, string message, bool sendWhatsApp, CancellationToken ct);
}

public sealed class CheckoutNotifier(AppDbContext dbContext) : ICheckoutNotifier
{
    public async Task SetMessageAsync(Guid checkoutId, string message, bool sendWhatsApp, CancellationToken ct)
    {
        await dbContext.OrderCheckouts.Where(c => c.Id == checkoutId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CustomerMessage, message), ct);

        if (!sendWhatsApp)
            return;

        var phone = await (
                from c in dbContext.OrderCheckouts
                join o in dbContext.Orders.IgnoreQueryFilters([QueryFilters.Showroom]) on c.OrderId equals o.Id
                join s in dbContext.Showrooms.IgnoreQueryFilters([QueryFilters.Showroom, QueryFilters.SoftDelete])
                    on o.ShowroomId equals s.Id
                where c.Id == checkoutId
                select s.PhoneNumber)
            .SingleAsync(ct);

        var recipient = WhatsAppNumber.TryNormalize(phone);
        var now = DateTime.UtcNow;
        dbContext.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            Recipient = recipient,
            Body = message,
            CheckoutId = checkoutId,
            Status = recipient is null ? NotificationStatus.Skipped : NotificationStatus.Pending,
            Error = recipient is null ? "The showroom phone is not an Egyptian mobile number." : null,
            NextAttemptAt = recipient is null ? null : now,
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync(ct);
    }
}