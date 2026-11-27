using System.Diagnostics;

namespace StockDesk.Core;

public enum Role { Administrator, Manager, WarehouseWorker }
public enum Status { New, Picking, Packed, Shipped, Cancelled }

public sealed class User
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
}

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
}

[DebuggerDisplay("{Id}: {Status}")]
public sealed class Shipment
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? WarehouseWorkerId { get; set; }
    public User? WarehouseWorker { get; set; }
    public string Description { get; set; } = "";
    public Status Status { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PackedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public int Revision { get; set; }

    public void Advance(Status next, int? workerId, decimal? price, DateTime now)
    {
        if (!Workflow.Allows(Status, next))
            throw new RuleException("Такой переход между статусами невозможен.");
        if (next == Status.Picking && workerId is not > 0)
            throw new RuleException("Выберите кладовщика.");
        if (next == Status.Packed && !Workflow.ValidPrice(price))
            throw new RuleException("Укажите стоимость от 0,01 до 1 000 000 ₽ с точностью до копеек.");

        if (next == Status.Picking) WarehouseWorkerId = workerId;
        if (next == Status.Packed)
        {
            Price = price!.Value;
            PackedAt = now;
        }
        if (next == Status.Shipped) ShippedAt = now;
        Status = next;
        Revision++;
        Debug.Assert(Status is not (Status.Picking) || WarehouseWorkerId.HasValue);
        Debug.Assert(Status is not (Status.Packed or Status.Shipped) || PackedAt.HasValue && Price > 0);
    }
}

public sealed class RuleException(string message) : Exception(message);

public static class Workflow
{
    public static bool Allows(Status from, Status to) => (from, to) switch
    {
        (Status.New, Status.Picking or Status.Cancelled) => true,
        (Status.Picking, Status.Packed or Status.Cancelled) => true,
        (Status.Packed, Status.Shipped or Status.Cancelled) => true,
        _ => false
    };

    public static bool CanChange(Role role, Status next) => role switch
    {
        Role.Administrator or Role.Manager => Enum.IsDefined(next),
        Role.WarehouseWorker => next == Status.Packed,
        _ => false
    };

    public static bool ValidPrice(decimal? price) =>
        price is >= 0.01m and <= 1_000_000m && decimal.Round(price.Value, 2) == price;
}

public record LoginRequest(string Login, string Password);
public record LoginReply(string Token, Role Role);
public record UserInput(string Login, string Password, Role Role);
public record UserRow(int Id, string Login, Role Role)
{
    public override string ToString() => Login;
}
public record CustomerInput(string Name, string Address);
public record CustomerRow(int Id, string Name, string Address)
{
    public override string ToString() => $"{Name} · {Address}";
}
public record NewOrder(int CustomerId, string Description);
public record OrderChange(Status Status, int? WarehouseWorkerId, decimal? Price, int Revision);
public record OrderRow(int Id, string Customer, string Address, string Description,
    string? WarehouseWorker, Status Status, decimal Price, DateTime CreatedAt, DateTime? PackedAt, DateTime? ShippedAt, int Revision)
{
    public string StatusText => Labels.StatusName(Status);
}
public record Statistics(int Total, int Active, int Packed, int Shipped, int Cancelled,
    decimal Revenue, double AverageHours, Dictionary<string, int> ByStatus, int Customers = 0, IReadOnlyList<MonthlyRevenue>? Months = null);
public record MonthlyRevenue(DateTime Month, decimal Amount);
public record ImportIssue(int Line, string Message);
public record ImportReply(int Added, List<ImportIssue> Issues);
public record ApiError(string Message);

public static class Labels
{
    public static string StatusName(Status status) => status switch
    {
        Status.New => "Создана",
        Status.Picking => "Комплектуется",
        Status.Packed => "Собрана",
        Status.Shipped => "Отгружена",
        Status.Cancelled => "Отменена",
        _ => "Неизвестно"
    };
    public static string RoleName(Role role) => role switch
    {
        Role.Administrator => "Администратор",
        Role.Manager => "Менеджер",
        Role.WarehouseWorker => "Кладовщик",
        _ => "Неизвестно"
    };
}