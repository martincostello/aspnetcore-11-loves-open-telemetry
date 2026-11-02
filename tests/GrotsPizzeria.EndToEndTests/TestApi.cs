using System.Text.Json;

namespace GrotsPizzeria.EndToEndTests;

/// <summary>An HttpClient (with its own cookies) whose calls end when the test does.</summary>
internal sealed class TestApi(HttpClient http) : IDisposable
{
    public Task<HttpResponseMessage> GetAsync(string path) => http.GetAsync(path, TestContext.Current.CancellationToken);

    public Task<string> GetStringAsync(string path) => http.GetStringAsync(path, TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> PostAsync(string path, HttpContent content) => http.PostAsync(path, content, TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> PostAsJsonAsync<T>(string path, T value, JsonSerializerOptions options) =>
        http.PostAsJsonAsync(path, value, options, TestContext.Current.CancellationToken);

    public void Dispose() => http.Dispose();
}
