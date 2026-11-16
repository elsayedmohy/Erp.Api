using System.Collections.Concurrent;
using ErpDashboard.Api.Features.Sales.Orders;

namespace ErpDashboard.Api.IntegrationTests;

public sealed class OrderCancellationFaults
{
    public ConcurrentDictionary<Guid, byte> FailFor { get; } = new();
}

public sealed class FaultInjectingOrderCancellation(IOrderCancellation inner, OrderCancellationFaults faults)
    : IOrderCancellation
{
    public async Task<bool> CancelBySystemAsync(Guid orderId, string reason, CancellationToken ct)
    {
        var result = await inner.CancelBySystemAsync(orderId, reason, ct);   // let it write, then fail
        if (faults.FailFor.ContainsKey(orderId))
            throw new InvalidOperationException("Injected failure after cancelling.");
        return result;
    }
}