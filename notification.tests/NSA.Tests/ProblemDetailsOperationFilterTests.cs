using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using NSA.Presentation.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NSA.Tests;

public sealed class ProblemDetailsOperationFilterTests
{
    [Fact]
    public void Path_only_operation_does_not_infer_400_and_preserves_explicit_error_metadata()
    {
        var explicitNotFound = new OpenApiResponse
        {
            Description = "A domain-specific missing record.",
            Headers =
            {
                ["X-Reason"] = new OpenApiHeader { Description = "Machine-readable reason." }
            },
            Content =
            {
                ["application/vnd.nsa.error+json"] = new OpenApiMediaType
                {
                    Schema = SchemaReference("DomainError")
                }
            }
        };
        var operation = CreateOperation(new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Path,
            Required = true
        });
        operation.Responses["404"] = explicitNotFound;

        Apply(operation);

        Assert.False(operation.Responses.ContainsKey("400"));
        Assert.Same(explicitNotFound, operation.Responses["404"]);
        Assert.Equal("A domain-specific missing record.", explicitNotFound.Description);
        Assert.True(explicitNotFound.Headers.ContainsKey("X-Reason"));
        Assert.True(explicitNotFound.Content.ContainsKey("application/vnd.nsa.error+json"));
        Assert.Equal(
            "ProblemDetails",
            explicitNotFound.Content["application/problem+json"].Schema.Reference.Id);
    }

    [Theory]
    [InlineData(ParameterLocation.Query)]
    [InlineData(ParameterLocation.Header)]
    public void Bindable_query_or_header_parameter_infers_validation_problem_400(ParameterLocation location)
    {
        var operation = CreateOperation(new OpenApiParameter
        {
            Name = "value",
            In = location
        });

        Apply(operation);

        var response = operation.Responses["400"];
        Assert.Equal("The request is invalid.", response.Description);
        Assert.Equal(
            "ValidationProblemDetails",
            response.Content["application/problem+json"].Schema.Reference.Id);
    }

    [Fact]
    public void Request_body_infers_validation_400_and_problem_details_415()
    {
        var operation = CreateOperation();
        operation.RequestBody = new OpenApiRequestBody();

        Apply(operation);

        Assert.Equal(
            "ValidationProblemDetails",
            operation.Responses["400"].Content["application/problem+json"].Schema.Reference.Id);
        Assert.Equal(
            "ProblemDetails",
            operation.Responses["415"].Content["application/problem+json"].Schema.Reference.Id);
    }

    [Fact]
    public void Existing_problem_details_media_type_is_not_overwritten()
    {
        var customSchema = SchemaReference("CustomValidationEnvelope");
        var operation = CreateOperation(new OpenApiParameter
        {
            Name = "filter",
            In = ParameterLocation.Query
        });
        operation.Responses["400"] = new OpenApiResponse
        {
            Description = "Explicit validation response.",
            Content =
            {
                ["application/problem+json"] = new OpenApiMediaType { Schema = customSchema }
            }
        };

        Apply(operation);

        Assert.Equal("Explicit validation response.", operation.Responses["400"].Description);
        Assert.Same(customSchema, operation.Responses["400"].Content["application/problem+json"].Schema);
    }

    private static OpenApiOperation CreateOperation(params OpenApiParameter[] parameters)
    {
        var operation = new OpenApiOperation();
        operation.Responses["200"] = new OpenApiResponse { Description = "Success" };
        foreach (var parameter in parameters)
        {
            operation.Parameters.Add(parameter);
        }

        return operation;
    }

    private static void Apply(OpenApiOperation operation)
    {
        var context = new OperationFilterContext(
            new ApiDescription(),
            new ReferencingSchemaGenerator(),
            new SchemaRepository(),
            typeof(ProblemDetailsOperationFilterTests).GetMethod(
                nameof(DummyOperation),
                BindingFlags.NonPublic | BindingFlags.Static)!);

        new ProblemDetailsOperationFilter().Apply(operation, context);
    }

    private static OpenApiSchema SchemaReference(string id) =>
        new()
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.Schema,
                Id = id
            }
        };

    private static void DummyOperation()
    {
    }

    private sealed class ReferencingSchemaGenerator : ISchemaGenerator
    {
        public OpenApiSchema GenerateSchema(
            Type modelType,
            SchemaRepository schemaRepository,
            MemberInfo? memberInfo = null,
            ParameterInfo? parameterInfo = null,
            ApiParameterRouteInfo? routeInfo = null) => SchemaReference(modelType.Name);
    }
}
