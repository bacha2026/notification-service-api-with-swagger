using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface IOrderService
{
    Task<IReadOnlyList<OrderDto>> GetOrdersAsync(string visitorEmail, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderDto>> GetOrdersForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<OrderDto?> GetOrderAsync(int id, CancellationToken cancellationToken);
    Task<OrderDto?> GetOrderForUserAsync(int id, Guid userId, CancellationToken cancellationToken);
    Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken);
    Task<CreateOrderResult> CreateOrderAsync(
        CreateOrderRequest request,
        IdempotencyContext? idempotency,
        CancellationToken cancellationToken);
    Task<CreateOrderResult> CreateOrderForUserAsync(Guid userId, string visitorEmail, CancellationToken cancellationToken);
    Task<CreateOrderResult> CreateOrderForUserAsync(
        Guid userId,
        string visitorEmail,
        IdempotencyContext? idempotency,
        CancellationToken cancellationToken);
    Task<OrderDto?> CancelOrderAsync(int id, CancelOrderRequest request, CancellationToken cancellationToken);
    Task<OrderDto?> UpdateStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken);
}
