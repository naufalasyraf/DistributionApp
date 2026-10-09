using DistributionApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace DistributionApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.ToTable(t => t.HasCheckConstraint("CK_Products_StockQuantity", "[StockQuantity] >= 0"));
            e.HasData(
                new Product { Id = 1, Name = "Keyboard", StockQuantity = 100, Price = 250_000 },
                new Product { Id = 2, Name = "Mouse", StockQuantity = 50, Price = 120_000 },
                new Product { Id = 3, Name = "Monitor", StockQuantity = 10, Price = 2_000_000 });
        });

        b.Entity<Order>(e =>
        {
            e.Property(o => o.CustomerId).HasMaxLength(100).IsRequired();
            e.Property(o => o.ShippingAddress).HasMaxLength(500).IsRequired();
            e.Property(o => o.TotalAmount).HasPrecision(18, 2);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.RowVersion).IsRowVersion();
            e.HasIndex(o => new { o.CustomerId, o.CreatedAt });
            e.HasIndex(o => new { o.Status, o.CreatedAt });
            e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
        });

        b.Entity<OrderItem>(e =>
        {
            e.Property(i => i.UnitPrice).HasPrecision(18, 2);
            e.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.HasKey(r => r.Key);
            e.Property(r => r.Key).HasMaxLength(100);
            e.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
        });
    }
}