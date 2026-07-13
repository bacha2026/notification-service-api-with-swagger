using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken);
    Task<ProductDto?> GetProductAsync(int id, CancellationToken cancellationToken);
    Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken);
    Task<ProductDto?> UpdateProductAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken);
}
