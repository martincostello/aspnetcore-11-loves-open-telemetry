using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Client.Services;

/// <summary>Typed wrapper over the BFF's <c>/api</c> surface. Failures come back as a <see cref="ProblemResponse"/>.</summary>
public sealed class ApiClient(HttpClient http)
{
    public Task<ApiResult<ClientConfig>> GetConfigAsync() => SendAsync<ClientConfig>(HttpMethod.Get, "api/config");

    public Task<ApiResult<IReadOnlyList<MenuItem>>> GetMenuAsync() => SendAsync<IReadOnlyList<MenuItem>>(HttpMethod.Get, "api/menu");

    public Task<ApiResult<Order>> PlaceOrderAsync(PlaceOrderRequest request) => SendAsync<Order>(HttpMethod.Post, "api/orders", request);

    public Task<ApiResult<IReadOnlyList<Order>>> GetOrdersAsync() => SendAsync<IReadOnlyList<Order>>(HttpMethod.Get, "api/orders");

    public Task<ApiResult<Order>> GetOrderAsync(string id) => SendAsync<Order>(HttpMethod.Get, $"api/orders/{Uri.EscapeDataString(id)}");

    public Task<ApiResult<Tracking>> GetTrackingAsync(string id) => SendAsync<Tracking>(HttpMethod.Get, $"api/orders/{Uri.EscapeDataString(id)}/tracking");

    public Task<ApiResult<UserInfo>> GetCurrentUserAsync() => SendAsync<UserInfo>(HttpMethod.Get, "api/auth/me");

    public Task<ApiResult<UserInfo>> LoginAsync(LoginRequest request) => SendAsync<UserInfo>(HttpMethod.Post, "api/auth/login", request);

    public Task<ApiResult<UserInfo>> RegisterAsync(RegisterRequest request) => SendAsync<UserInfo>(HttpMethod.Post, "api/auth/register", request);

    public Task<ApiResult<object>> LogoutAsync() => SendAsync<object>(HttpMethod.Post, "api/auth/logout");

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string uri, object? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await http.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var hasBody = response.Content.Headers.ContentLength is not 0 && response.StatusCode != System.Net.HttpStatusCode.NoContent;
                return new(hasBody ? await response.Content.ReadFromJsonAsync<T>() : default, null);
            }

            var problem = await TryReadProblemAsync(response)
                ?? new ProblemResponse("Something went wrong", (int)response.StatusCode, response.ReasonPhrase);

            return new(default, problem);
        }
        catch (HttpRequestException ex)
        {
            return new(default, new ProblemResponse("The pizzeria is unreachable", 0, ex.Message));
        }
    }

    private static async Task<ProblemResponse?> TryReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemResponse>();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
