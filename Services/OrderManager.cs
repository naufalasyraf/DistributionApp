using DistributionApp.Data;
using DistributionApp.Domain;
using DistributionApp.Dtos;
using DistributionApp.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace DistributionApp.Services;

public class OrderManager(AppDbContext db, ILogger<OrderManager> log)
{
    public async Task<(Order Order, bool Created)> CreateOrder(
    CreateOrderRequest req, string idempotencyKey, CancellationToken ct)
    {
        var hash = IdempotencyHelper.ComputeHash(req);

        var replay = await FindReplayAsync(idempotencyKey, hash, ct);
        if (replay != null) return (replay, false);

        var lines = req.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .OrderBy(x => x.ProductId)
            .ToList();

        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var missing = ids.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException(string.Format("Product(s) not found: {0}", string.Join(", ", missing)));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = new Order { CustomerId = req.CustomerId, ShippingAddress = req.ShippingAddress };

        try
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = idempotencyKey,
                RequestHash = hash,
                OrderId = order.Id
            });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await FindReplayAsync(idempotencyKey, hash, ct)
                ?? throw new InvalidOperationException("Idempotency record disappeared.");
            return (winner, false);
        }

        foreach (var line in lines)
        {
            var affected = await db.Products
                .Where(p => p.Id == line.ProductId && p.StockQuantity >= line.Quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    p => p.StockQuantity, p => p.StockQuantity - line.Quantity), ct);

            if (affected == 0)
                throw new InsufficientStockException(line.ProductId, line.Quantity);

            var price = products[line.ProductId].Price;
            order.Items.Add(new OrderItem { ProductId = line.ProductId, Quantity = line.Quantity, UnitPrice = price });
            order.TotalAmount += price * line.Quantity;
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        log.LogInformation("Order {OrderId} created for {CustomerId}, total {Total}",
            order.Id, order.CustomerId, order.TotalAmount);
        return (order, true);
    }

    private async Task<Order?> FindReplayAsync(string key, string hash, CancellationToken ct)
    {
        var rec = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (rec == null) return null;
        if (rec.RequestHash != hash) throw new IdempotencyKeyReusedException(key);

        log.LogInformation("Idempotent replay for key {Key}, order {OrderId}", key, rec.OrderId);
        return await GetAsync(rec.OrderId, ct);
    }

    public async Task<Order> GetAsync(Guid id, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new NotFoundException(string.Format("Order {0} not found.", id));

    public async Task<PagedResult<OrderResponse>> ListOrder(OrderQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 100);

        var query = db.Orders.AsNoTracking().AsQueryable();
        if (q.Status.HasValue) query = query.Where(o => o.Status == q.Status);
        if (!string.IsNullOrWhiteSpace(q.CustomerId)) query = query.Where(o => o.CustomerId == q.CustomerId);
        if (q.From.HasValue) query = query.Where(o => o.CreatedAt >= q.From);
        if (q.To.HasValue) query = query.Where(o => o.CreatedAt <= q.To);

        var total = await query.CountAsync(ct);
        var data = await query.Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);

        return new PagedResult<OrderResponse>(data.Select(OrderResponse.From).ToList(), page, size, total);
    }

    public async Task<Order> UpdateStatusAsync(Guid id, OrderStatus target, CancellationToken ct)
    {
        if (target == OrderStatus.Cancelled)
            return await CancelAsync(id, ct);

        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException($"Order {id} not found.");

        if (!OrderStatusRules.CanChangeStatus(order.Status, target))
            throw new InvalidStatusTransitionException(order.Status.ToString(), target.ToString());

        var from = order.Status;
        order.Status = target;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Order {OrderId} status {From} -> {To}", id, from, target);
        return order;
    }

    public async Task<Order> CancelAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException($"Order {id} not found.");

        if (order.Status is not (OrderStatus.Pending or OrderStatus.Confirmed))
            throw new InvalidStatusTransitionException(order.Status.ToString(), nameof(OrderStatus.Cancelled));

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        foreach (var item in order.Items.OrderBy(i => i.ProductId))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE Products SET StockQuantity = StockQuantity + {item.Quantity}
                WHERE Id = {item.ProductId}", ct);
        }

        await tx.CommitAsync(ct);
        log.LogInformation("Order {OrderId} cancelled, stock restored", id);
        return order;
    }
}