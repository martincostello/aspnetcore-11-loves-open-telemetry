using System.Diagnostics;
using System.Net;
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
            return Owned(result, user) ?? result.ToHttpResult();
        });

        orders.MapGet("/{id}/tracking", async (string id, ClaimsPrincipal user, OrdersClient orders, DeliveryClient delivery, CancellationToken ct) =>
        {
            if (Owned(await orders.GetOrderAsync(id, ct), user) is { } refused)
            {
                return refused;
            }

            Activity.Current?.SetTag("grot.order.id", id);
            return (await delivery.GetTrackingAsync(id, ct)).ToHttpResult();
        });

        return app;
    }

    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Null if the order was found and is the user's. Otherwise the response to give: "not found" when there is no such order or it
    /// is someone else's (the two look the same, so ids cannot be probed), and the downstream failure when the orders service
    /// could not say (so an outage is not reported as the order not existing).
    /// </summary>
    private static IResult? Owned(DownstreamResult<Order> result, ClaimsPrincipal user) =>
        result.Value is { } order
            ? (order.UserId == UserId(user) ? null : NotFound())
            : result.StatusCode == HttpStatusCode.NotFound ? NotFound() : result.ToHttpResult();

    private static IResult NotFound() =>
        Results.Json(new ProblemResponse("Order not found", StatusCodes.Status404NotFound), statusCode: StatusCodes.Status404NotFound);

    private static IResult? Validate(PlaceOrderRequest request)
    {
        string? detail = null;

        if (string.IsNullOrWhiteSpace(request.Address) || request.Address.Length > 200)
        {
            detail = "A delivery address of up to 200 characters is required.";
        }
        else if (request.Items is not { Count: >= 1 and <= OrderLimits.MaxLines } || request.Items.Any(i => i is null || i.Quantity is < 1 or > OrderLimits.MaxQuantity || string.IsNullOrWhiteSpace(i.PizzaId)))
        {
            detail = $"Orders need between 1 and {OrderLimits.MaxLines} lines, each with a quantity between 1 and {OrderLimits.MaxQuantity}.";
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
