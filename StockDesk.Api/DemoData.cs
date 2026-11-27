using StockDesk.Core;
using StockDesk.Data;
using Microsoft.EntityFrameworkCore;

internal static class DemoData
{
    public static async Task PopulateAsync(CleanDb db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        // A persistent marker keeps repeated launches from adding duplicate demo records.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS DemoSeed (Id INTEGER PRIMARY KEY)");
        if (await db.Database.ExecuteSqlRawAsync("INSERT OR IGNORE INTO DemoSeed (Id) VALUES (1)") == 0)
            return;
        var executor = await db.Users.FirstAsync(u => u.Role == Role.WarehouseWorker);
        var customers = await db.Customers.OrderBy(c => c.Id).ToListAsync();
        var names = new[] { "Анна", "Иван", "Ольга", "Павел", "Мария", "Ирина", "Алексей", "Елена", "Виктор" };
        var surnames = new[] { "Соколова", "Орлов", "Миронова", "Лебедев", "Крылова", "Волкова", "Зайцев", "Петрова", "Смирнов" };
        var streets = new[] { "Лесная", "Садовая", "Северная", "Молодёжная", "Полевая", "Школьная" };
        for (int i = customers.Count; i < 45; i++)
        {
            var customer = new Customer
            {
                Name = $"{names[i % names.Length]} {surnames[(i + i / names.Length) % surnames.Length]}",
                Address = $"ул. {streets[i % streets.Length]}, {12 + i}"
            };
            db.Customers.Add(customer);
            customers.Add(customer);
        }
        await db.SaveChangesAsync();

        string[] descriptions = ["Бумага A4, 20 коробок", "Картриджи, 12 шт.", "Упаковочная плёнка, 30 рулонов", "Бытовая химия, 15 коробок", "Канцтовары, 18 коробок"];
        var occupied = (await db.Orders.Select(o => o.CustomerId).Distinct().ToListAsync()).ToHashSet();
        var now = DateTime.UtcNow;
        for (int i = 0; i < Math.Min(45, customers.Count); i++)
        {
            if (occupied.Contains(customers[i].Id)) continue;
            var outcome = i % 10;
            var age = outcome < 7 ? 2 + (i * 13 % 147) : 1 + i % 7;
            var created = now.AddDays(-age - 1).AddHours(-(i % 8));
            var order = new Shipment
            {
                CustomerId = customers[i].Id,
                Description = descriptions[i % descriptions.Length],
                CreatedAt = created
            };
            if (outcome == 9) order.Advance(Status.Cancelled, null, null, created.AddHours(2));
            else
            {
                order.Advance(Status.Picking, executor.Id, null, created.AddHours(1));
                
                if (outcome < 7)
                {
                    order.Advance(Status.Packed, null, 12400 + (i * 4130 % 78000), created.AddHours(6 + i % 16));
                    if (outcome < 6) order.Advance(Status.Shipped, null, null, created.AddHours(26));
                }
            }
            db.Orders.Add(order);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
