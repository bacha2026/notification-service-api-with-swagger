using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NSA.Presentation.OpenApi;

/// <summary>Adds the API's standard RFC 7807 response body to documented error responses.</summary>
public sealed class ProblemDetailsOperationFilter : IOperationFilter
{
    private static readonly IReadOnlyDictionary<string, string> ErrorDescriptions =
        new Dictionary<string, string>
        {
            ["400"] = "The request is invalid.",
            ["404"] = "The requested resource was not found.",
            ["405"] = "The HTTP method is not supported for this resource.",
            ["415"] = "The request media type is not supported.",
            ["500"] = "An unexpected server error occurred.",
            ["503"] = "A required downstream service is temporarily unavailable."
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var problemDetailsSchema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository);
        var validationProblemDetailsSchema = context.SchemaGenerator.GenerateSchema(typeof(ValidationProblemDetails), context.SchemaRepository);
        var hasModelValidationInput = HasModelValidationInput(operation);

        foreach (var response in operation.Responses.Where(response => IsErrorStatusCode(response.Key)))
        {
            var schema = response.Key == "400" && hasModelValidationInput
                ? validationProblemDetailsSchema
                : problemDetailsSchema;
            AddProblemDetailsContent(response.Value, schema);
        }

        if (hasModelValidationInput && !operation.Responses.ContainsKey("400"))
        {
            AddErrorResponse(operation, "400", validationProblemDetailsSchema);
        }

        if (operation.RequestBody is not null && !operation.Responses.ContainsKey("415"))
        {
            AddErrorResponse(operation, "415", problemDetailsSchema);
        }

        if (!operation.Responses.ContainsKey("500"))
        {
            AddErrorResponse(operation, "500", problemDetailsSchema);
        }
    }

    private static bool IsErrorStatusCode(string statusCode)
    {
        return statusCode.Length == 3 && (statusCode[0] == '4' || statusCode[0] == '5');
    }

    private static void AddProblemDetailsContent(OpenApiResponse response, OpenApiSchema schema)
    {
        // Keep response descriptions, headers, links, and explicitly documented media
        // types intact. Only supply the standard representation when one is absent.
        response.Content.TryAdd("application/problem+json", new OpenApiMediaType { Schema = schema });
    }

    private static bool HasModelValidationInput(OpenApiOperation operation) =>
        operation.RequestBody is not null
        || operation.Parameters.Any(parameter =>
            parameter.In is ParameterLocation.Query or ParameterLocation.Header);

    private static void AddErrorResponse(OpenApiOperation operation, string statusCode, OpenApiSchema schema)
    {
        var response = new OpenApiResponse { Description = ErrorDescriptions[statusCode] };
        AddProblemDetailsContent(response, schema);
        operation.Responses.Add(statusCode, response);
    }
}
