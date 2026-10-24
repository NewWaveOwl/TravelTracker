using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TravelTracker.Api.Authentication;

/**
 * Describes protected operations while leaving anonymous health explicitly public.
 */
public sealed class BearerSecurityOperationFilter : IOperationFilter
{
    /**
     * Adds bearer requirements and the endpoint's required permission to OpenAPI.
     * @param operation The operation being documented.
     * @param context Its controller metadata and schema context.
     */
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any()) return;
        operation.Security = [new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer")] = []
        }];
        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid bearer token." });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Required permission is missing." });
        var policies = metadata.OfType<IAuthorizeData>().Select(data => data.Policy).Where(policy => policy is not null);
        operation.Description = $"{operation.Description} Required permission: {string.Join(", ", policies)}.".Trim();
    }
}
