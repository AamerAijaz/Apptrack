using Apptrack.Auth;
using Apptrack.Services;

namespace Apptrack.Endpoints;

public static class NoticesEndpoint
{
    public static IEndpointRouteBuilder MapNotices(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notices", GetAsync);
        return app;
    }

    public static async Task<IResult> GetAsync(HttpContext ctx, SessionCookie session, NoticesService notices, CancellationToken ct)
    {
        if (session.Read(ctx.Request) is not Guid userId)
            return Results.Unauthorized();

        var page = await notices.LoadAsync(userId, ct);
        if (page is null)
        {
            session.Clear(ctx.Response);
            return Results.Unauthorized();
        }

        ctx.Response.Headers.CacheControl = "no-store";
        return Results.Ok(page);
    }
}
