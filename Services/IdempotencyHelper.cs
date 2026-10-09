using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DistributionApp.Dtos;

namespace DistributionApp.Services;

public static class IdempotencyHelper
{
    public static string ComputeHash(CreateOrderRequest req)
    {
        var normalized = new
        {
            req.CustomerId,
            req.ShippingAddress,
            Items = req.Items
                .OrderBy(i => i.ProductId).ThenBy(i => i.Quantity)
                .Select(i => new { i.ProductId, i.Quantity })
        };
        var json = JsonSerializer.Serialize(normalized);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}