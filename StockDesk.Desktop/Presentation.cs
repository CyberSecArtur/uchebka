using StockDesk.Core;

namespace StockDesk.Desktop;

public record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public static class Presentation
{
    public static IEnumerable<OrderRow> Filter(IEnumerable<OrderRow> rows, Status? status, string search)
    {
        search = search.Trim();
        return rows.Where(o => (status is null || o.Status == status) &&
            (search.Length == 0 || o.Customer.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            o.Address.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            o.Description.Contains(search, StringComparison.OrdinalIgnoreCase) || o.Id.ToString() == search));
    }

    public static List<Choice<Status>> Next(Status current, Role role) => Enum.GetValues<Status>()
        .Where(s => Workflow.Allows(current, s) && Workflow.CanChange(role, s))
        .Select(s => new Choice<Status>(s, Labels.StatusName(s))).ToList();
}