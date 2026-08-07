using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Persistence.Interfaces;

namespace NSA.Service;

public sealed class ProductService(IProductRepository productRepository) : IProductService
{
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken)
    {
        var products = await productRepository.GetAllAsync(cancellationToken);
        return products.Select(ToDto).ToList();
    }

    public async Task<ProductDto?> GetProductAsync(int id, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        return product is null ? null : ToDto(product);
    }

    public async Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = Product.Create(request.Name, request.ShortDescription, request.Description, request.Price, request.QuantityAvailable, request.ImageUrl);

        productRepository.Add(product);
        await productRepository.SaveChangesAsync(cancellationToken);
        return ToDto(product);
    }

    public async Task<ProductDto?> UpdateProductAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.Update(request.Name, request.ShortDescription, request.Description, request.Price, request.QuantityAvailable, request.ImageUrl);

        await productRepository.SaveChangesAsync(cancellationToken);
        return ToDto(product);
    }

    private static ProductDto ToDto(Product product)
    {
        return new ProductDto(product.Id, product.Name, product.ShortDescription, product.Description, product.Price, product.QuantityAvailable, product.ImageUrl);
    }
}
