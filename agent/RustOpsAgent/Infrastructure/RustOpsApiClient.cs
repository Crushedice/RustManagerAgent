using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RustOpsAgent.Core.Contracts;

namespace RustOpsAgent.Infrastructure;

internal sealed class RustOpsApiClient : IDisposable
{
    // Default per-request budget for ordinary calls (status, health, command exec, etc.).
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;

    public RustOpsApiClient(ApiSettings settings)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
            // Timeouts are enforced per request via a linked CancellationTokenSource so that
            // long-running operations (e.g. server update, which the API runs for up to ~25
            // minutes via steamcmd) can opt into a larger budget instead of being capped at
            // the 20s default that fits short calls.
            Timeout = Timeout.InfiniteTimeSpan
        };

        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
    }

    public async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        RustOpsSentry.AddBreadcrumb($"GET {path}", "agent.api");
        using var scope = CreateRequestScope(cancellationToken, timeout);
        using var response = await SendAsync(ct => _http.GetAsync(path.TrimStart('/'), ct), scope, "GET", path);
        var body = await response.Content.ReadAsStringAsync(scope.Token);
        if (!response.IsSuccessStatusCode)
        {
            RustOpsSentry.AddBreadcrumb($"GET {path} failed with {(int)response.StatusCode}.", "agent.api");
            throw new InvalidOperationException($"API GET {path} failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        return JsonDocument.Parse(body);
    }

    public async Task<JsonDocument> PostAsync(string path, object? payload, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        RustOpsSentry.AddBreadcrumb($"POST {path}", "agent.api");
        var json = payload is null ? "{}" : JsonSerializer.Serialize(payload, JsonDefaults.Default);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var scope = CreateRequestScope(cancellationToken, timeout);
        using var response = await SendAsync(ct => _http.PostAsync(path.TrimStart('/'), content, ct), scope, "POST", path);
        var body = await response.Content.ReadAsStringAsync(scope.Token);
        if (!response.IsSuccessStatusCode)
        {
            RustOpsSentry.AddBreadcrumb($"POST {path} failed with {(int)response.StatusCode}.", "agent.api");
            throw new InvalidOperationException($"API POST {path} failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        return JsonDocument.Parse(body);
    }

    public async Task<JsonDocument> PutAsync(string path, object? payload, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        RustOpsSentry.AddBreadcrumb($"PUT {path}", "agent.api");
        var json = payload is null ? "{}" : JsonSerializer.Serialize(payload, JsonDefaults.Default);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var scope = CreateRequestScope(cancellationToken, timeout);
        using var response = await SendAsync(ct => _http.PutAsync(path.TrimStart('/'), content, ct), scope, "PUT", path);
        var body = await response.Content.ReadAsStringAsync(scope.Token);
        if (!response.IsSuccessStatusCode)
        {
            RustOpsSentry.AddBreadcrumb($"PUT {path} failed with {(int)response.StatusCode}.", "agent.api");
            throw new InvalidOperationException($"API PUT {path} failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        return JsonDocument.Parse(body);
    }

    public async Task<JsonDocument> DeleteAsync(string path, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        RustOpsSentry.AddBreadcrumb($"DELETE {path}", "agent.api");
        using var scope = CreateRequestScope(cancellationToken, timeout);
        using var response = await SendAsync(ct => _http.DeleteAsync(path.TrimStart('/'), ct), scope, "DELETE", path);
        var body = await response.Content.ReadAsStringAsync(scope.Token);
        if (!response.IsSuccessStatusCode)
        {
            RustOpsSentry.AddBreadcrumb($"DELETE {path} failed with {(int)response.StatusCode}.", "agent.api");
            throw new InvalidOperationException($"API DELETE {path} failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        return string.IsNullOrWhiteSpace(body) || body == "null"
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(body);
    }

    // Builds a CancellationToken that trips on either the caller's token or a per-request
    // timeout (DefaultRequestTimeout unless an override is supplied).
    private static RequestScope CreateRequestScope(CancellationToken cancellationToken, TimeSpan? timeout)
    {
        var effective = timeout ?? DefaultRequestTimeout;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(effective);
        return new RequestScope(linked, cancellationToken, effective);
    }

    // Runs the request and translates a timeout-induced cancellation into a clear
    // TimeoutException, so callers don't misread it as the caller cancelling.
    private static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, RequestScope scope, string method, string path)
    {
        try
        {
            return await send(scope.Token);
        }
        catch (OperationCanceledException) when (!scope.CallerToken.IsCancellationRequested)
        {
            RustOpsSentry.AddBreadcrumb($"{method} {path} timed out after {scope.Timeout.TotalSeconds:0}s.", "agent.api");
            throw new TimeoutException(
                $"API {method} {path} timed out after {scope.Timeout.TotalSeconds:0}s.");
        }
    }

    private readonly struct RequestScope : IDisposable
    {
        private readonly CancellationTokenSource _source;

        public RequestScope(CancellationTokenSource source, CancellationToken callerToken, TimeSpan timeout)
        {
            _source = source;
            CallerToken = callerToken;
            Timeout = timeout;
        }

        public CancellationToken Token => _source.Token;
        public CancellationToken CallerToken { get; }
        public TimeSpan Timeout { get; }

        public void Dispose() => _source.Dispose();
    }

    public void Dispose() => _http.Dispose();
}
