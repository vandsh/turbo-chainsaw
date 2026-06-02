namespace TestProject.Middleware;

public class ApiKeyMiddleware {
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration) {
        _next = next;
        _apiKey = configuration["FileBrowser:ApiKey"]
            ?? throw new InvalidOperationException("FileBrowser:ApiKey is not configured.");
    }

    public async Task InvokeAsync(HttpContext context) {
        var path = context.Request.Path.Value ?? "";

        // Only protect /api/* routes (except /api/files/shared which uses HMAC auth)
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api/files/shared", StringComparison.OrdinalIgnoreCase)) {
            if (!context.Request.Headers.TryGetValue("X-Api-Key", out var providedKey)
                && !context.Request.Query.TryGetValue("apikey", out providedKey)) {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("API key required.");
                return;
            }

            if (!string.Equals(providedKey, _apiKey, StringComparison.Ordinal)) {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Invalid API key.");
                return;
            }
        }

        await _next(context);
    }
}
