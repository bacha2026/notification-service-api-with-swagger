using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NSA.Presentation.OpenApi;

/// <summary>Adds the configured bearer scheme only to operations protected by <see cref="AuthorizeAttribute"/>.</summary>
public sealed class BearerSecurityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var isAuthorized = context.MethodInfo.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any()
            || context.MethodInfo.DeclaringType?.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any() == true;
        if (!isAuthorized)
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }] = []
        });
    }
}
