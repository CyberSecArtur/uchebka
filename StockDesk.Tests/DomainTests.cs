using StockDesk.Core;
using Xunit;

namespace StockDesk.Tests;

public class DomainTests
{
    public static IEnumerable<object[]> StatusPairs =>
        from source in Enum.GetValues<Status>()
        from target in Enum.GetValues<Status>()
        select new object[] { source, target };

    private static readonly HashSet<(Status, Status)> Valid =
    [
        (Status.New, Status.Picking), (Status.New, Status.Cancelled),
        (Status.Picking, Status.Packed), (Status.Picking, Status.Cancelled),
        (Status.Packed, Status.Shipped), (Status.Packed, Status.Cancelled)
    ];

    [Theory, MemberData(nameof(StatusPairs))]
    public void AllowedTransitionsMatchLifecycle(Status source, Status target) =>
        Assert.Equal(Valid.Contains((source, target)), Workflow.Allows(source, target));

    [Theory, InlineData(null), InlineData(0), InlineData(-5)]
    public void AssignRequiresWarehouseWorker(int? id)
    {
        var order = new Shipment();
        Assert.Throws<RuleException>(() => order.Advance(Status.Picking, id, null, DateTime.UtcNow));
        Assert.Equal(Status.New, order.Status);
        Assert.Equal(0, order.Revision);
    }

    [Theory, InlineData("0"), InlineData("-1"), InlineData("1000001"), InlineData("12.345"), InlineData(null)]
    public void CompletingRequiresValidPrice(string? amount)
    {
        var order = new Shipment { Status = Status.Picking, WarehouseWorkerId = 1 };
        decimal? price = amount is null ? null : decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<RuleException>(() => order.Advance(Status.Packed, null, price, DateTime.UtcNow));
        Assert.Null(order.PackedAt);
    }

    [Fact]
    public void CompletionAndPaymentPreservePriceAndDate()
    {
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var order = new Shipment();
        order.Advance(Status.Picking, 3, null, now);
        order.Advance(Status.Packed, null, 2500.50m, now);
        order.Advance(Status.Shipped, null, null, now.AddHours(2));
        Assert.Equal((Status.Shipped, 3, 2500.50m, now, 3),
            (order.Status, order.WarehouseWorkerId!.Value, order.Price, order.PackedAt!.Value, order.Revision));
    }

    [Fact]
    public void DirectPaymentIsRejected()
    {
        var order = new Shipment();
        Assert.Throws<RuleException>(() => order.Advance(Status.Shipped, null, 100m, DateTime.UtcNow));
    }

    [Fact]
    public void CsvKeepsValidRowsAndReportsLineNumbers()
    {
        var (customers, issues) = CustomerCsv.Parse("name;address\nАнна;\"Дом; офис 5\"\n;Дом\nСергей;Дом 4");
        Assert.Equal(2, customers.Count);
        Assert.Equal("Дом; офис 5", customers[0].Input.Address);
        Assert.Equal(3, Assert.Single(issues).Line);
    }

    [Theory]
    [InlineData("=1+2", "\"'=1+2\"")]
    [InlineData("ООО \"Маяк\"", "\"ООО \"\"Маяк\"\"\"")]
    public void ExportQuotesTextAndProtectsFormula(string input, string expected) =>
        Assert.Equal(expected, CustomerCsv.Cell(input));

    [Theory]
    [InlineData(Status.Picking, false)]
    [InlineData(Status.Packed, true)]
    [InlineData(Status.Shipped, false)]
    [InlineData(Status.Cancelled, false)]
    public void WarehouseWorkerHasLimitedActions(Status status, bool expected) =>
        Assert.Equal(expected, Workflow.CanChange(Role.WarehouseWorker, status));
    [Theory]
    [InlineData(Status.New)]
    [InlineData(Status.Picking)]
    [InlineData(Status.Packed)]
    public void CancellationBeforeShippingNeverSetsShippingTime(Status status)
    {
        var shipment = new Shipment { Status = status, WarehouseWorkerId = 3 };
        shipment.Advance(Status.Cancelled, null, null, DateTime.UtcNow);
        Assert.Equal(Status.Cancelled, shipment.Status);
        Assert.Null(shipment.ShippedAt);
    }

    [Fact]
    public void ShippingKeepsSeparatePackingAndShippingTimes()
    {
        var packed = DateTime.UtcNow;
        var shipped = packed.AddHours(2);
        var shipment = new Shipment { Status = Status.Packed, PackedAt = packed, Price = 1200 };
        shipment.Advance(Status.Shipped, null, null, shipped);
        Assert.Equal(packed, shipment.PackedAt);
        Assert.Equal(shipped, shipment.ShippedAt);
    }

}