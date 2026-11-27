using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using System.Xml.Linq;
using StockDesk.Core;
using StockDesk.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

var parentOption = Array.IndexOf(args, "--parent-pid");
if (parentOption >= 0 && parentOption + 1 < args.Length && int.TryParse(args[parentOption + 1], out int parentId))
{
    args = args.Where((_, index) => index != parentOption && index != parentOption + 1).ToArray();
    _ = Task.Run(async () =>
    {
        try
        {
            using var parent = System.Diagnostics.Process.GetProcessById(parentId);
            await parent.WaitForExitAsync();
        }
        catch (ArgumentException) { }
        Environment.Exit(0);
    });
}
var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Environment.GetEnvironmentVariable("STOCKDESK_DATA");
var defaultDatabase = string.IsNullOrWhiteSpace(dataDirectory);
if (string.IsNullOrWhiteSpace(dataDirectory))
    dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockDesk");
Directory.CreateDirectory(dataDirectory);
builder.Services.AddDbContext<CleanDb>(options => options
    .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(dataDirectory, "stockdesk.db"), ForeignKeys = true
    }.ToString())
    );
builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
var validation = new TokenValidationParameters
{
    ValidIssuer = "StockDesk", ValidAudience = "StockDesk.Desktop", IssuerSigningKey = key,
    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
    ValidateIssuerSigningKey = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
    ClockSkew = TimeSpan.FromSeconds(5)
};

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CleanDb>();
    await db.Database.MigrateAsync();
    if (!await db.Users.AnyAsync())
    {
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordHasher<User>>();
        foreach (var (login, role) in new[] { ("admin", Role.Administrator), ("manager", Role.Manager), ("worker", Role.WarehouseWorker) })
        {
            var user = new User { Login = login, Role = role };
            user.PasswordHash = passwords.HashPassword(user, "StockDemo17!");
            db.Users.Add(user);
        }
        db.Customers.AddRange(
            new Customer { Name = "ООО Север", Address = "ул. Лесная, склад 14" },
            new Customer { Name = "ООО Маяк", Address = "пр. Мира, 28, ворота 5" });
        await db.SaveChangesAsync();
        db.Orders.Add(new Shipment
        {
            CustomerId = db.Customers.Local.First().Id,
            Description = "Бумага A4, 20 коробок",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
    if (defaultDatabase || Environment.GetEnvironmentVariable("STOCKDESK_DEMO") == "1")
        await DemoData.PopulateAsync(db);
}

app.Use(async (context, next) =>
{
    var elapsed = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await next();
    }
    catch (RuleException ex)
    {
        await Results.BadRequest(new ApiError(ex.Message)).ExecuteAsync(context);
    }
    catch (DbUpdateConcurrencyException)
    {
        await Results.Conflict(new ApiError("Заказ уже изменён. Обновите список.")).ExecuteAsync(context);
    }
    catch (DbUpdateException ex)
    {
        app.Logger.LogWarning(ex, "Ошибка сохранения данных");
        await Results.Conflict(new ApiError("Не удалось сохранить запись. Проверьте, нет ли дубликата.")).ExecuteAsync(context);
    }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        app.Logger.LogError(ex, "Ошибка обработки запроса");
        await Results.Json(new ApiError("Внутренняя ошибка сервера."), statusCode: 500).ExecuteAsync(context);
    }
    finally
    {
        app.Logger.LogInformation("{Method} {Path} → {Status} за {Elapsed} мс",
            context.Request.Method, context.Request.Path, context.Response.StatusCode, elapsed.ElapsedMilliseconds);
    }
});
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "StockDesk" }));
app.MapPost("/api/login", async (LoginRequest input, CleanDb db, PasswordHasher<User> hasher) =>
{
    var login = input.Login?.Trim().ToLowerInvariant();
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Login == login);
    if (user is null || string.IsNullOrEmpty(input.Password) || input.Password.Length > 128 ||
        hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password) == PasswordVerificationResult.Failed)
        return Results.Json(new ApiError("Неверный логин или пароль."), statusCode: 401);
    var token = new JwtSecurityToken("StockDesk", "StockDesk.Desktop",
        [new Claim("sub", user.Id.ToString(CultureInfo.InvariantCulture))],
        expires: DateTime.UtcNow.AddHours(4), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new LoginReply(tokenHandler.WriteToken(token), user.Role));
}).RequireRateLimiting("login");

var api = app.MapGroup("/api").AddEndpointFilter(async (invocation, next) =>
{
    var header = invocation.HttpContext.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();
    ClaimsPrincipal claims;
    try { claims = tokenHandler.ValidateToken(header[7..], validation, out _); }
    catch (Exception ex) when (ex is SecurityTokenException or ArgumentException) { return Results.Unauthorized(); }
    if (!int.TryParse(claims.FindFirst("sub")?.Value, out int id)) return Results.Unauthorized();
    var db = invocation.HttpContext.RequestServices.GetRequiredService<CleanDb>();
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
    if (user is null) return Results.Unauthorized();
    invocation.HttpContext.Items["user"] = user;
    return await next(invocation);
});

api.MapGet("/customers", async (HttpContext http, CleanDb db) =>
    Actor(http).Role == Role.WarehouseWorker ? Results.Json(new ApiError("Недостаточно прав."), statusCode: 403) : Results.Ok(
        await db.Customers.AsNoTracking().OrderBy(c => c.Name).Select(c => new CustomerRow(c.Id, c.Name, c.Address)).ToListAsync()));
api.MapPost("/customers", async (CustomerInput input, HttpContext http, CleanDb db) =>
{
    if (Actor(http).Role == Role.WarehouseWorker) return Results.Json(new ApiError("Недостаточно прав."), statusCode: 403);
    var customer = new Customer { Name = Required(input.Name, "Имя", 120), Address = Required(input.Address, "Адрес", 250) };
    db.Add(customer);
    await db.SaveChangesAsync();
    return Results.Created($"/api/customers/{customer.Id}", new CustomerRow(customer.Id, customer.Name, customer.Address));
});
api.MapPost("/customers/import", async (HttpContext http, CleanDb db) =>
{
    if (Actor(http).Role == Role.WarehouseWorker) return Results.Json(new ApiError("Недостаточно прав."), statusCode: 403);
    if (http.Request.ContentLength > 262144) return Results.BadRequest(new ApiError("Файл должен быть меньше 256 КБ."));
    using var reader = new StreamReader(http.Request.Body);
    char[] buffer = new char[262145];
    var length = await reader.ReadBlockAsync(buffer.AsMemory(), http.RequestAborted);
    if (length == buffer.Length) return Results.BadRequest(new ApiError("Файл должен быть меньше 256 КБ."));
    var (customers, issues) = CustomerCsv.Parse(new string(buffer, 0, length));
    var existing = (await db.Customers.AsNoTracking().Select(c => new { c.Name, c.Address }).ToListAsync())
        .Select(c => (c.Name, c.Address)).ToHashSet();
    int count = 0;
    foreach (var (line, input) in customers)
    {
        if (!existing.Add((input.Name, input.Address)))
        {
            issues.Add(new ImportIssue(line, "Покупатель с таким адресом уже существует."));
            continue;
        }
        db.Customers.Add(new Customer { Name = input.Name, Address = input.Address });
        count++;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new ImportReply(count, issues.OrderBy(e => e.Line).ToList()));
});
api.MapGet("/users", async (HttpContext http, CleanDb db) =>
    Actor(http).Role != Role.Administrator ? Results.Json(new ApiError("Недостаточно прав."), statusCode: 403) : Results.Ok(await db.Users.AsNoTracking()
        .OrderBy(u => u.Login).Select(u => new UserRow(u.Id, u.Login, u.Role)).ToListAsync()));
api.MapPost("/users", async (UserInput input, HttpContext http, CleanDb db, PasswordHasher<User> hasher) =>
{
    if (Actor(http).Role != Role.Administrator) return Results.Json(new ApiError("Недостаточно прав."), statusCode: 403);
    if (!Enum.IsDefined(input.Role) || input.Password?.Length is not (>= 10 and <= 128))
        throw new RuleException("Выберите роль и введите пароль длиной 10–128 символов.");
    var user = new User { Login = Required(input.Login, "Логин", 60).ToLowerInvariant(), Role = input.Role };
    user.PasswordHash = hasher.HashPassword(user, input.Password);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Created($"/api/users/{user.Id}", new UserRow(user.Id, user.Login, user.Role));
});
api.MapGet("/workers", async (HttpContext http, CleanDb db) =>
    Actor(http).Role == Role.WarehouseWorker ? Results.Json(new ApiError("Недостаточно прав."), statusCode: 403) : Results.Ok(await db.Users.AsNoTracking()
        .Where(u => u.Role == Role.WarehouseWorker).OrderBy(u => u.Login).Select(u => new UserRow(u.Id, u.Login, u.Role)).ToListAsync()));

api.MapGet("/orders", async (HttpContext http, CleanDb db) =>
    Results.Ok(await OrderRows(db, Actor(http)).ToListAsync()));
api.MapPost("/orders", async (NewOrder input, HttpContext http, CleanDb db) =>
{
    if (Actor(http).Role == Role.WarehouseWorker) return Results.Json(new ApiError("Недостаточно прав."), statusCode: 403);
    if (!await db.Customers.AnyAsync(c => c.Id == input.CustomerId)) throw new RuleException("Выберите клиента.");
    var order = new Shipment
    {
        CustomerId = input.CustomerId, Description = Required(input.Description, "Описание", 500), CreatedAt = DateTime.UtcNow
    };
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return Results.Created($"/api/orders/{order.Id}", new { order.Id });
});
api.MapPatch("/orders/{id:int}", async (int id, OrderChange input, HttpContext http, CleanDb db) =>
{
    var actor = Actor(http);
    var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id &&
        (actor.Role != Role.WarehouseWorker || o.WarehouseWorkerId == actor.Id));
    if (order is null) return Results.NotFound(new ApiError("Заказ не найден."));
    if (!Workflow.CanChange(actor.Role, input.Status)) return Results.Json(new ApiError("Недостаточно прав."), statusCode: 403);
    if (order.Revision != input.Revision) return Results.Conflict(new ApiError("Заказ уже изменён. Обновите список."));
    if (input.Status == Status.Picking && !await db.Users.AnyAsync(u => u.Id == input.WarehouseWorkerId && u.Role == Role.WarehouseWorker))
        throw new RuleException("Выберите действующего кладовщика.");
    order.Advance(input.Status, input.WarehouseWorkerId, input.Price, DateTime.UtcNow);
    await db.SaveChangesAsync();
    return Results.NoContent();
});
api.MapGet("/statistics", async (HttpContext http, CleanDb db) =>
{
    var actor = Actor(http);
    var orders = await OrderRows(db, actor).ToListAsync();
    var customerCount = actor.Role == Role.WarehouseWorker
        ? await db.Orders.Where(o => o.WarehouseWorkerId == actor.Id).Select(o => o.CustomerId).Distinct().CountAsync()
        : await db.Customers.CountAsync();
    var today = DateTime.UtcNow;
    var current = new DateTime(today.Year, today.Month, 1);
    var revenue = orders.Where(o => o.Status == Status.Shipped && o.ShippedAt.HasValue)
        .GroupBy(o => (o.ShippedAt!.Value.Year, o.ShippedAt.Value.Month))
        .ToDictionary(g => g.Key, g => g.Sum(o => o.Price));
    var months = Enumerable.Range(-5, 6).Select(i => current.AddMonths(i))
        .Select(m => new MonthlyRevenue(m, revenue.GetValueOrDefault((m.Year, m.Month)))).ToList();
    var counts = Enum.GetValues<Status>().ToDictionary(Labels.StatusName, s => orders.Count(o => o.Status == s));
    return Results.Ok(new Statistics(orders.Count,
        orders.Count(o => o.Status is Status.New or Status.Picking or Status.Packed),
        orders.Count(o => o.PackedAt.HasValue), counts["Отгружена"], counts["Отменена"],
        orders.Where(o => o.Status == Status.Shipped).Sum(o => o.Price),
        orders.Where(o => o.ShippedAt.HasValue).Select(o => (o.ShippedAt!.Value - o.CreatedAt).TotalHours)
            .DefaultIfEmpty(0).Average(), counts, customerCount, months));
});
api.MapGet("/export/{format}", async (string format, HttpContext http, CleanDb db) =>
{
    if (Actor(http).Role != Role.Administrator) return Results.Json(new ApiError("Недостаточно прав."), statusCode: 403);
    var orders = await OrderRows(db, Actor(http)).ToListAsync();
    if (format == "json") return Results.Json(orders);
    if (format == "xml")
    {
        var root = new XElement("orders", orders.Select(o => new XElement("order", new XAttribute("id", o.Id),
            new XElement("customer", o.Customer), new XElement("address", o.Address),
            new XElement("description", o.Description), new XElement("worker", o.WarehouseWorker),
            new XElement("status", o.Status), new XElement("price", o.Price),
            new XElement("createdAt", Utc(o.CreatedAt)), new XElement("packedAt", Utc(o.PackedAt)), new XElement("shippedAt", Utc(o.ShippedAt)))));
        return Results.Text(root.ToString(), "application/xml", Encoding.UTF8);
    }
    if (format != "csv") return Results.BadRequest(new ApiError("Доступны форматы CSV, JSON и XML."));
    var csv = new StringBuilder("id;customer;address;description;worker;status;price;createdAt;packedAt;shippedAt\r\n");
    foreach (var o in orders)
        csv.AppendJoin(';', o.Id.ToString(CultureInfo.InvariantCulture), CustomerCsv.Cell(o.Customer),
            CustomerCsv.Cell(o.Address), CustomerCsv.Cell(o.Description), CustomerCsv.Cell(o.WarehouseWorker ?? ""),
            o.Status.ToString(), o.Price.ToString(CultureInfo.InvariantCulture), Utc(o.CreatedAt), Utc(o.PackedAt), Utc(o.ShippedAt)).Append("\r\n");
    return Results.Text("\uFEFF" + csv, "text/csv", Encoding.UTF8);
});
app.Run();

static User Actor(HttpContext context) => (User)context.Items["user"]!;
static string Required(string? value, string label, int limit)
{
    var trimmed = value?.Trim();
    if (string.IsNullOrEmpty(trimmed) || trimmed.Length > limit || trimmed.Any(char.IsControl))
        throw new RuleException($"{label}: заполните поле, максимум {limit} символов без переносов строк.");
    return trimmed;
}
static string Utc(DateTime? date) => date.HasValue ? DateTime.SpecifyKind(date.Value, DateTimeKind.Utc).ToString("O") : "";
static IQueryable<OrderRow> OrderRows(CleanDb db, User user) => db.Orders.AsNoTracking()
    .Where(o => user.Role != Role.WarehouseWorker || o.WarehouseWorkerId == user.Id).OrderByDescending(o => o.Id)
    .Select(o => new OrderRow(o.Id, o.Customer!.Name, o.Customer.Address, o.Description,
        o.WarehouseWorker == null ? null : o.WarehouseWorker.Login, o.Status, o.Price, o.CreatedAt, o.PackedAt, o.ShippedAt, o.Revision));

public partial class Program;
