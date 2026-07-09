using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSA.Application.Contracts;
using NSA.Persistence;

namespace NSA.Presentation.Controllers;

[ApiController]
[Route("api/products")]
[Produces("application/json")]
public sealed class ProductsController(NotificationDbContext dbContext) : ControllerBase
{
    /// <summary>Gets the product catalog data, including image URLs, names, prices, descriptions, and available quantities.</summary>
    /// <response code="200">Returns all active products for display in a product grid.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(CancellationToken cancellationToken)
    {
        var products = await dbContext.Products
            .OrderBy(product => product.Name)
            .Select(product => new ProductDto(product.Id, product.Name, product.ShortDescription, product.Description, product.Price, product.QuantityAvailable, product.ImageUrl))
            .ToListAsync(cancellationToken);

        return Ok(products);
    }

    /// <summary>Gets one product detail record for the product detail page.</summary>
    /// <response code="200">Returns the selected product detail.</response>
    /// <response code="404">The requested product does not exist.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetProduct(int id, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .Where(product => product.Id == id)
            .Select(product => new ProductDto(product.Id, product.Name, product.ShortDescription, product.Description, product.Price, product.QuantityAvailable, product.ImageUrl))
            .SingleOrDefaultAsync(cancellationToken);

        return product is null ? NotFound() : Ok(product);
    }
}
