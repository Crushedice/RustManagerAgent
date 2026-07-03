namespace RusticalandOPS.Api.Middleware;

public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public AuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Allow /health, /ui, and / without auth
        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/ui") ||
            context.Request.Path == "/")
        {
            await _next(context);
            return;
        }

        var apiKey = RustOpsEnv.FirstNonEmptyEnvironment("RUSTMGR_API_KEY", "RUSTOPS_API_KEY") ?? "changeme";
        var providedKey = context.Request.Headers["X-API-Key"].ToString();

        if (string.IsNullOrWhiteSpace(providedKey) || !FixedTimeEquals(providedKey, apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            return;
        }

        await _next(context);
    }

    // Constant-time comparison so the key can't be recovered byte-by-byte via timing.
    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
