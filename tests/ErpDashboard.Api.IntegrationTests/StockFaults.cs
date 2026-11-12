using ErpDashboard.Api.Features.Inventory.Stock;

namespace ErpDashboard.Api.IntegrationTests;

public sealed class InjectedFaultException() : Exception("Injected failure for testing.");

public sealed class StockFaults
{
    private int _failOnCall;
    private int _calls;

    public void FailAfterChange(int n)
    {
        _calls = 0;
        _failOnCall = n;
    }

    public void Reset() => _failOnCall = 0;

    internal void OnChangeApplied()
    {
        if (_failOnCall > 0 && Interlocked.Increment(ref _calls) == _failOnCall)
            throw new InjectedFaultException();
    }
}

public sealed class FaultInjectingStockService(StockService inner, StockFaults faults) : IStockService
{
    public async Task<bool> TryApplyAsync(StockChange change, CancellationToken ct)
    {
        var applied = await inner.TryApplyAsync(change, ct);
        if (applied)
            faults.OnChangeApplied();
        return applied;
    }
}