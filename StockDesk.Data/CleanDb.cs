using StockDesk.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace StockDesk.Data;

public sealed class CleanDb(DbContextOptions<CleanDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Shipment> Orders => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>().HasIndex(u => u.Login).IsUnique();
        model.Entity<Customer>().HasIndex(c => new { c.Name, c.Address }).IsUnique();
        model.Entity<Shipment>().Property(o => o.Revision).IsConcurrencyToken();
        model.Entity<Shipment>().HasIndex(o => new { o.WarehouseWorkerId, o.Status });
        model.Entity<Shipment>().HasIndex(o => o.Status);
    }
}

[DbContext(typeof(CleanDb))]
[Migration("202611130001_Initial")]
public sealed class Initial : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        CREATE TABLE Users (
            Id INTEGER PRIMARY KEY AUTOINCREMENT, Login TEXT NOT NULL,
            PasswordHash TEXT NOT NULL, Role INTEGER NOT NULL);
        CREATE UNIQUE INDEX IX_Users_Login ON Users(Login);
        CREATE TABLE Customers (
            Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Address TEXT NOT NULL);
        CREATE UNIQUE INDEX IX_Customers_Name_Address ON Customers(Name, Address);
        CREATE TABLE Orders (
            Id INTEGER PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL,
            WarehouseWorkerId INTEGER NULL, Description TEXT NOT NULL, Status INTEGER NOT NULL,
            Price TEXT NOT NULL, CreatedAt TEXT NOT NULL, PackedAt TEXT NULL, ShippedAt TEXT NULL,
            Revision INTEGER NOT NULL,
            FOREIGN KEY(CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
            FOREIGN KEY(WarehouseWorkerId) REFERENCES Users(Id));
        CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId);
        CREATE INDEX IX_Orders_WarehouseWorkerId_Status ON Orders(WarehouseWorkerId, Status);
        CREATE INDEX IX_Orders_Status ON Orders(Status);
        """);

    protected override void Down(MigrationBuilder migration)
    {
        migration.DropTable("Orders");
        migration.DropTable("Customers");
        migration.DropTable("Users");
    }
}