using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface IOrderService
{
    Task<IReadOnlyList<OrderDto>> GetOrdersAsync(string visitorEmail, CancellationToken cancellationToken);
    Task<OrderDto?> GetOrderAsync(int id, CancellationToken cancellationToken);
    Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken);
    Task<OrderDto?> CancelOrderAsync(int id, CancelOrderRequest request, CancellationToken cancellationToken);
    Task<OrderDto?> UpdateStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken);
}
