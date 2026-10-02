using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace UserService.Authentication;

public sealed class EndpointSecurityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }
        var authorization = metadata.OfType<IAuthorizeData>().ToArray();
        if (authorization.Length == 0)
        {
            return;
        }
        var scheme = authorization.Any(item => item.AuthenticationSchemes == ServiceApiKeyDefaults.AuthenticationScheme)
            ? ServiceApiKeyDefaults.AuthenticationScheme : "Bearer";
        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = scheme }
                }] = Array.Empty<string>()
            }
        ];
    }
}
