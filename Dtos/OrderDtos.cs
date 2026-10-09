using System.ComponentModel.DataAnnotations;
using DistributionApp.Domain;

namespace DistributionApp.Dtos;

public class CreateOrderItemDto
{
    [Required] public int ProductId { get; set; }
    [Range(1, 10_000)] public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    [Required, MaxLength(100)] public string CustomerId { get; set; } = "";
    [Required, MaxLength(500)] public string ShippingAddress { get; set; } = "";
    [Required, MinLength(1)] public List<CreateOrderItemDto> Items { get; set; } = new();
}

public class UpdateStatusRequest
{
    [Required] public OrderStatus Status { get; set; }
}

public class OrderQuery
{
    public OrderStatus? Status { get; set; }
    public string? CustomerId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public record OrderItemResponse(int ProductId, int Quantity, decimal UnitPrice);

public record OrderResponse(Guid Id, string CustomerId, string ShippingAddress, string Status,
    decimal TotalAmount, DateTime CreatedAt, DateTime UpdatedAt, List<OrderItemResponse> Items)
{
    public static OrderResponse From(Order o) => new(o.Id, o.CustomerId, o.ShippingAddress,
        o.Status.ToString(), o.TotalAmount, o.CreatedAt, o.UpdatedAt,
        o.Items.Select(i => new OrderItemResponse(i.ProductId, i.Quantity, i.UnitPrice)).ToList());
}

public record PagedResult<T>(List<T> Data, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}