using System.Diagnostics;
using System.Security.Claims;
using GrotsPizzeria.Server.Downstream;
using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Server.Endpoints;

/// <summary>The BFF surface for the UI. Each endpoint delegates to one of the polyglot microservices.</summary>
internal static class PizzeriaEndpoints
{
    private static readonly string[] PaymentMethods = ["card", "cash"];

    public static IEndpointRouteBuilder MapPizzeriaEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/menu", async (MenuClient menu, CancellationToken ct) =>
            (await menu.GetMenuAsync(ct)).ToHttpResult());

        var orders = api.MapGroup("/orders").RequireAuthorization();

        orders.MapPost("", async (PlaceOrderRequest request, ClaimsPrincipal user, OrdersClient client, CancellationToken ct) =>
        {
            if (Validate(request) is { } problem)
            {
                return problem;
            }

            var result = await client.PlaceOrderAsync(UserId(user), user.Identity!.Name!, request, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : result.ToHttpResult();
        });

        orders.MapGet("", async (ClaimsPrincipal user, OrdersClient client, CancellationToken ct) =>
            (await client.GetOrdersAsync(UserId(user), ct)).ToHttpResult());

        orders.MapGet("/{id}", async (string id, ClaimsPrincipal user, OrdersClient client, CancellationToken ct) =>
        {
            var result = await client.GetOrderAsync(id, ct);
            return IsOwnedBy(result, user) ? result.ToHttpResult() : NotFound();
        });

        orders.MapGet("/{id}/tracking", async (string id, ClaimsPrincipal user, OrdersClient orders, DeliveryClient delivery, CancellationToken ct) =>
        {
            if (!IsOwnedBy(await orders.GetOrderAsync(id, ct), user))
            {
                return NotFound();
            }

            Activity.Current?.SetTag("grot.order.id", id);
            return (await delivery.GetTrackingAsync(id, ct)).ToHttpResult();
        });

        return app;
    }

    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static bool IsOwnedBy(DownstreamResult<Order> result, ClaimsPrincipal user) =>
        result.Value is { } order && order.UserId == UserId(user);

    private static IResult NotFound() =>
        Results.Json(new ProblemResponse("Order not found", StatusCodes.Status404NotFound), statusCode: StatusCodes.Status404NotFound);

    private static IResult? Validate(PlaceOrderRequest request)
    {
        string? detail = null;

        if (string.IsNullOrWhiteSpace(request.Address) || request.Address.Length > 200)
        {
            detail = "A delivery address of up to 200 characters is required.";
        }
        else if (request.Items is not { Count: >= 1 and <= 20 } || request.Items.Any(i => i.Quantity is < 1 or > 20 || string.IsNullOrWhiteSpace(i.PizzaId)))
        {
            detail = "Orders need between 1 and 20 lines, each with a quantity between 1 and 20.";
        }
        else if (request.Payment is null || !PaymentMethods.Contains(request.Payment.Method))
        {
            detail = "The payment method must be 'card' or 'cash'.";
        }
        else if (request.Payment.Method == "card" && !(request.Payment.CardLast4 is { Length: 4 } digits && digits.All(char.IsAsciiDigit)))
        {
            detail = "Card payments need the last 4 digits of the card.";
        }

        return detail is null
            ? null
            : Results.Json(new ProblemResponse("Invalid order", StatusCodes.Status400BadRequest, detail), statusCode: StatusCodes.Status400BadRequest);
    }
}
