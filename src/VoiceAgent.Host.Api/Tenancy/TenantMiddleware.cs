using Microsoft.AspNetCore.Http;

namespace VoiceAgent.Host.Api.Tenancy;

public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;
    public const string HeaderName = "X-Tenant-Id";

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, TenantContext tenant)
    {
        // For MVP: require tenant header on most endpoints except /v1/tenants (create).
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/v1/tenants", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue(HeaderName, out var val) || !Guid.TryParse(val.ToString(), out var tid))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = $"Missing/invalid {HeaderName}" });
            return;
        }

        tenant.TenantId = tid;
        await _next(ctx);
    }
}
