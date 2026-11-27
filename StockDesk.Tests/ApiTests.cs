using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StockDesk.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace StockDesk.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly string path = Path.Combine(Path.GetTempPath(), "StockDeskTests-" + Guid.NewGuid());

    public ApiTests()
    {
        Environment.SetEnvironmentVariable("STOCKDESK_DATA", path);
        factory = new WebApplicationFactory<Program>();
    }

    [Fact]
    public async Task OrderWorkflowEnforcesPermissionsAndReportsRevenue()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders")).StatusCode);
        using var manager = await Login("manager");
        using var worker = await Login("worker");
        using var admin = await Login("admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/export/csv")).StatusCode);
        Assert.Empty((await worker.GetFromJsonAsync<List<OrderRow>>("/api/orders"))!);
        var assignedWorker = Assert.Single((await manager.GetFromJsonAsync<List<UserRow>>("/api/workers"))!);
        var customer = (await manager.GetFromJsonAsync<List<CustomerRow>>("/api/customers"))!.First();
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync("/api/orders", new NewOrder(customer.Id, "Поставка бумаги, 20 коробок"))).StatusCode);
        var order = (await manager.GetFromJsonAsync<List<OrderRow>>("/api/orders"))!.First();
        var url = $"/api/orders/{order.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await worker.PatchAsJsonAsync(url, new OrderChange(Status.Picking, null, null, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PatchAsJsonAsync(url, new OrderChange(Status.Picking, null, null, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync(url, new OrderChange(Status.Picking, assignedWorker.Id, null, 0))).StatusCode);
        Assert.Equal(order.Id, Assert.Single((await worker.GetFromJsonAsync<List<OrderRow>>("/api/orders"))!).Id);
        Assert.Equal(HttpStatusCode.Conflict, (await worker.PatchAsJsonAsync(url, new OrderChange(Status.Packed, null, 1250, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await worker.PatchAsJsonAsync(url, new OrderChange(Status.Packed, null, 0, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await worker.PatchAsJsonAsync(url, new OrderChange(Status.Packed, null, 1250, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await worker.PatchAsJsonAsync(url, new OrderChange(Status.Shipped, null, null, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PatchAsJsonAsync(url, new OrderChange(Status.Shipped, null, null, 2))).StatusCode);
        Assert.Equal(1250, (await admin.GetFromJsonAsync<Statistics>("/api/statistics"))!.Revenue);
        foreach (var (format, media) in new[] { ("csv", "text/csv"), ("json", "application/json"), ("xml", "application/xml") })
        {
            using var export = await admin.GetAsync($"/api/export/{format}");
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            Assert.Equal(media, export.Content.Headers.ContentType!.MediaType);
        }
    }

    [Fact]
    public async Task ImportPersistsValidEntriesAndFlagsMalformedRows()
    {
        using var manager = await Login("manager");
        using var response = await manager.PostAsync("/api/customers/import",
            new StringContent("name;address\nСоздана клиент;Дом 2\nБез адреса;\nСоздана клиент;Дом 2"));
        var result = (await response.Content.ReadFromJsonAsync<ImportReply>())!;
        Assert.Equal(1, result.Added);
        Assert.Equal(new[] { 3, 4 }, result.Issues.Select(i => i.Line));
        var rows = (await manager.GetFromJsonAsync<List<CustomerRow>>("/api/customers"))!;
        Assert.Contains(rows, r => r.Name == "Создана клиент" && r.Address == "Дом 2");
    }

    private async Task<HttpClient> Login(string name)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/login", new LoginRequest(name, "StockDemo17!"));
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<LoginReply>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public void Dispose()
    {
        factory.Dispose();
        Environment.SetEnvironmentVariable("STOCKDESK_DATA", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}