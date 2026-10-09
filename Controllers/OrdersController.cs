using DistributionApp.Dtos;
using DistributionApp.Exceptions;
using DistributionApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace DistributionApp.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(OrderManager orders) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
    CreateOrderRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new BadRequestException("Header 'Idempotency-Key' is required (max 100 characters).");

        var (order, created) = await orders.CreateOrder(req, idempotencyKey, ct);
        var body = OrderResponse.From(order);

        if (!created)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
            return Ok(body);
        }
        return CreatedAtAction(nameof(Get), new { id = order.Id }, body);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken ct) =>
        OrderResponse.From(await orders.GetAsync(id, ct));

    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderResponse>>> List([FromQuery] OrderQuery q, CancellationToken ct) =>
        await orders.ListOrder(q, ct);

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(Guid id, UpdateStatusRequest req, CancellationToken ct) =>
        OrderResponse.From(await orders.UpdateStatusAsync(id, req.Status, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OrderResponse>> Cancel(Guid id, CancellationToken ct) =>
        OrderResponse.From(await orders.CancelAsync(id, ct));
}