using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;

namespace NSA.Presentation.Controllers;

[ApiController]
[ApiVersion("1.0", Deprecated = true)]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/products")]
[Route("api/products")]
public sealed class ProductsController(IProductService productService) : ControllerBase
{
    /// <summary>Gets the product catalog data, including image URLs, names, prices, descriptions, and available quantities.</summary>
    /// <response code="200">Returns all active products for display in a product grid.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(CancellationToken cancellationToken)
    {
        var products = await productService.GetProductsAsync(cancellationToken);
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
        var product = await productService.GetProductAsync(id, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    /// <summary>Creates a product catalog record.</summary>
    /// <response code="201">The product was created.</response>
    /// <response code="400">The product request is invalid.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await productService.CreateProductAsync(request, cancellationToken);
        var requestedVersion = HttpContext.GetRequestedApiVersion();
        var location = requestedVersion is null
            ? $"/api/products/{product.Id}"
            : $"/api/v{requestedVersion.MajorVersion}/products/{product.Id}";

        return Created(location, product);
    }

    /// <summary>Updates a product catalog record.</summary>
    /// <response code="200">The product was updated.</response>
    /// <response code="400">The product request is invalid.</response>
    /// <response code="404">The requested product does not exist.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> UpdateProduct(int id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await productService.UpdateProductAsync(id, request, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }
}
