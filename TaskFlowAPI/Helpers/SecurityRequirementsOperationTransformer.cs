using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TaskFlowAPI.Helpers;

public class SecurityRequirementsOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor?.EndpointMetadata;

        if (metadata == null)
        {
            return Task.CompletedTask;
        }

        var hasAllowAnonymous = metadata.OfType<AllowAnonymousAttribute>().Any();
        if (hasAllowAnonymous)
        {
            return Task.CompletedTask;
        }

        var hasAuthorize = metadata.OfType<AuthorizeAttribute>().Any();
        if (hasAuthorize)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(
                    JwtBearerDefaults.AuthenticationScheme, context.Document)] = new List<string>()
            });
        }

        return Task.CompletedTask;
    }
}