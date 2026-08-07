using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NSA.Presentation.OpenApi;

/// <summary>Adds descriptions to the framework-owned error schemas shown in Swagger UI.</summary>
public sealed class ProblemDetailsSchemaFilter : ISchemaFilter
{
    private static readonly IReadOnlyDictionary<string, string> PropertyDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = "URI reference that identifies the type of problem.",
            ["title"] = "Short, human-readable summary of the problem.",
            ["status"] = "HTTP status code generated for this occurrence of the problem.",
            ["detail"] = "Human-readable explanation specific to this occurrence of the problem.",
            ["instance"] = "URI reference that identifies this specific occurrence of the problem.",
            ["errors"] = "Validation errors grouped by request field name."
        };

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(ValidationProblemDetails))
        {
            schema.Description = "A standardized error response containing request validation failures.";
        }
        else if (context.Type == typeof(ProblemDetails))
        {
            schema.Description = "A standardized error response describing why an HTTP request failed.";
        }
        else
        {
            return;
        }

        foreach (var property in schema.Properties)
        {
            if (PropertyDescriptions.TryGetValue(property.Key, out var description))
            {
                property.Value.Description = description;
            }
        }
    }
}
