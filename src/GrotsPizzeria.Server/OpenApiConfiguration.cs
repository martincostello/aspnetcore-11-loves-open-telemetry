using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GrotsPizzeria.Server;

/// <summary>
/// The OpenAPI document of the BFF (served at /openapi.json, and shown by the page at /swagger): what the API is, and that the
/// operations that need a signed-in user are secured by the cookie that signing in sets. Microsoft.AspNetCore.OpenApi generates the
/// rest from the endpoints and the types they use.
/// </summary>
internal static class OpenApiConfiguration
{
    public const string CookieScheme = "cookieAuth";

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;

            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new()
                {
                    Title = "grots-web",
                    Version = "1.0.0",
                    Description =
                        "The back end of the UI of Grot's Pizzeria (a back-end-for-front-end): accounts, and the API the UI uses to show the menu, " +
                        "place orders and track their delivery. It calls the menu, orders, checkout and delivery services on the UI's behalf, " +
                        "and passes their errors on.\n\n" +
                        "Signing in (or registering) sets a cookie, which is what the operations that need a signed-in user check. Amounts are in " +
                        "the currency the menu prices things in, which every price says (`currency`). Everything that is not under `/api` is the UI itself.",
                };

                document.Tags = new HashSet<OpenApiTag>
                {
                    new() { Name = "Account", Description = "Register, sign in and out, and see who is signed in." },
                    new() { Name = "Menu", Description = "What is on the menu, and what it costs now." },
                    new() { Name = "Orders", Description = "Place orders, look at them and track their delivery. Only a user's own orders are visible to them." },
                    new() { Name = "Settings", Description = "What the UI needs to know at startup." },
                    new() { Name = "Health", Description = "Whether the service is running." },
                };

                document.Components ??= new();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[CookieScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Cookie,
                    Name = "grots.auth",
                    Description = "The cookie that is set when signing in or registering (`POST /api/auth/login`).",
                };

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;

                if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(CookieScheme, context.Document)] = [],
                    });
                }

                return Task.CompletedTask;
            });
        });
}
