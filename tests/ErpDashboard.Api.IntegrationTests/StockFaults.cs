using ErpDashboard.Api.Features.Finance.Invoices.DocumentNumber;
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
    
    public async Task<bool> TryReserveAsync(Guid w, Guid p, int q, CancellationToken ct)
    {
        var ok = await inner.TryReserveAsync(w, p, q, ct);
        if (ok) faults.OnChangeApplied();
        return ok;
    }

    public async Task ReleaseAsync(Guid w, Guid p, int q, CancellationToken ct)
    {
        await inner.ReleaseAsync(w, p, q, ct);
        faults.OnChangeApplied();
    }

    public async Task IssueReservedAsync(StockChange change, CancellationToken ct)
    {
        await inner.IssueReservedAsync(change, ct);
        faults.OnChangeApplied();
    }
}
public sealed class NumberFaults
{
    private int _armed;
    public void FailAfterNextNumber() => _armed = 1;
    public void Reset() => _armed = 0;
    internal void OnNumberTaken()
    {
        if (Interlocked.Exchange(ref _armed, 0) == 1)
            throw new InjectedFaultException();
    }
}

public sealed class FaultInjectingNumberGenerator(DocumentNumberGenerator inner, NumberFaults faults)
    : IDocumentNumberGenerator
{
    public async Task<string> NextAsync(string series, int year, CancellationToken ct)
    {
        var number = await inner.NextAsync(series, year, ct);
        faults.OnNumberTaken();
        return number;
    }
}