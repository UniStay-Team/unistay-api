using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace UniStay.Api;

public sealed class ApiSecurityDocument : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Session"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "Opaque session token",
            Description = "Token returned by auth/login/. Expires in 24 hours; revoked by logout or password reset."
        };
        foreach (var path in document.Paths.Keys.Where(p => p.StartsWith("/api/") && !p.EndsWith('/')).ToArray())
        {
            var item = document.Paths[path]; document.Paths.Remove(path); document.Paths.Add(path + "/", item);
        }
        foreach (var (path, item) in document.Paths)
        {
            if (path.StartsWith("/api/v1/") && (!path.StartsWith("/api/v1/auth/") || path.Contains("logout")))
                foreach (var entry in item.Operations ?? [])
                {
                    var operation = entry.Value;
                    if (path.StartsWith("/api/v1/admin/")) DescribeAdmin(path, entry.Key.ToString(), operation);

                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Session", document)] = [] });
                    if (entry.Key.ToString().Equals("Get", StringComparison.OrdinalIgnoreCase) && (path.TrimEnd('/') == "/api/v1/listings" || path.TrimEnd('/') == "/api/v1/listings/{id}")) operation.Security.Add(new());
                }
        }
        return Task.CompletedTask;
    }

    private static void DescribeAdmin(string path, string method, OpenApiOperation operation)
    {
        operation.Responses ??= new();
        foreach (var status in new[] { "400", "401", "403", "404", "405", "409", "429", "500" })
            operation.Responses.TryAdd(status, new OpenApiResponse
            {
                Description = status == "400" ? "Invalid fields, filters or page; no partial changes." : "Error envelope: error.status and error.details.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new()
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Required = new HashSet<string> { "error" },
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Object,
                                    Required = new HashSet<string> { "status", "details" },
                                    Properties = new Dictionary<string, IOpenApiSchema>
                                    {
                                        ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                                        ["details"] = new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.String }, new OpenApiSchema { Type = JsonSchemaType.Object, AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } } }] }
                                    }
                                }
                            }
                        }
                    }
                }
            });
        if (method.Equals("Patch", StringComparison.OrdinalIgnoreCase))
            operation.Description = path.Contains("profile")
                ? "Partial update: university (0..160), bio (0..1000), schedule ('',early,late), smoking (boolean/null), cleanliness ('',tidy,relaxed). Unknown/read-only fields, null strings and empty object: 400. Consent is read-only. Changes and audit are atomic; unchanged requests do not add audit records."
                : "Partial update: name (trimmed 1..120), is_active (boolean). Other fields and empty object: 400. Admin blocking/unblocking: 409. Blocking revokes sessions without changing applications. Changes and audit are atomic; unchanged requests do not add audit records.";
        if (!method.Equals("Get", StringComparison.OrdinalIgnoreCase) || path.Contains('{') || path.EndsWith("me/")) return;
        var resource = path.TrimEnd('/').Split('/').Last();
        string[] filters = resource switch
        {
            "users" => ["search", "role", "is_active", "email_verified"],
            "listings" => ["search", "city", "owner_id", "status"],
            "photos" => ["listing_id"],
            "applications" => ["student_id", "listing_id", "status"],
            "application-events" => ["application_id", "actor_id"],
            _ => []
        };
        operation.Description = "Fixed page size 20. Unknown/repeated or invalid filters: 400. Missing positive page: 404. Filters combine with AND. Stable ordering follows admin-api-v1.";
        operation.Parameters ??= [];
        foreach (var name in filters.Prepend("page"))
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = ParameterLocation.Query,
                Required = false,
                Description = name == "page" ? "Positive integer, default 1." : name.EndsWith("_id") ? "Positive entity ID." : name is "is_active" or "email_verified" ? "true or false" : "See admin-api-v1 filter values.",
                Schema = new OpenApiSchema { Type = name == "page" || name.EndsWith("_id") ? JsonSchemaType.Integer : name is "is_active" or "email_verified" ? JsonSchemaType.Boolean : JsonSchemaType.String }
            });
    }
}
