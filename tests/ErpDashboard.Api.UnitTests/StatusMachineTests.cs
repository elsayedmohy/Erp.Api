using ErpDashboard.Api.Common;
using ErpDashboard.Api.Features.Finance.Invoices;
using ErpDashboard.Api.Features.Purchasing.PurchaseOrders;
using ErpDashboard.Api.Features.Sales.Orders;

namespace ErpDashboard.Api.UnitTests;

public class StatusMachineTests
{
    private enum Light { Red, Green, Off }

    private static StatusMachine<Light> Lights() => new("light", new Dictionary<Light, Light[]>
    {
        [Light.Red] = [Light.Green, Light.Off],
        [Light.Green] = [Light.Red],
        [Light.Off] = []
    });

    [Fact]
    public void MissingRow_FailsWhenBuilt()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StatusMachine<Light>("light", new Dictionary<Light, Light[]> { [Light.Red] = [Light.Green] }));

        Assert.Contains("Green", ex.Message);
        Assert.Contains("Off", ex.Message);
    }

    [Fact]
    public void AllowedMove_DoesNotThrow() => Lights().EnsureCanMove(Light.Red, Light.Green);

    [Fact]
    public void DisallowedMove_IsInvalidTransition_WithWhatIsAllowed()
    {
        var ex = Assert.Throws<ConflictException>(() => Lights().EnsureCanMove(Light.Green, Light.Off));

        Assert.Equal("invalid_transition", ex.Code);
        Assert.Equal("Green", ex.Extensions["currentStatus"]);
        Assert.Equal(new[] { "Red" }, (string[])ex.Extensions["allowedTransitions"]!);
    }

    [Fact]
    public void FinalStatus_SaysItCannotChange()
    {
        var ex = Assert.Throws<ConflictException>(() => Lights().EnsureCanMove(Light.Off, Light.Red));

        Assert.Contains("cannot change status", ex.Message);
    }

    // Touching each machine builds it: a status added to an enum without a row fails here, not in production.
    [Fact]
    public void RealMachines_CoverEveryStatus()
    {
        Assert.NotNull(OrderStatuses.Machine);
        Assert.NotNull(InvoiceStatuses.Machine);
        Assert.NotNull(PurchaseOrderStatuses.Machine);
    }
}