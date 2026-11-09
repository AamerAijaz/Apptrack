using Apptrack.Auth;
using Apptrack.Services;

namespace Apptrack.Endpoints;

public static class SessionProfileEndpoint
{
    public static IEndpointRouteBuilder MapSessionProfile(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/session", GetAsync);
        return app;
    }

    public static async Task<IResult> GetAsync(HttpContext ctx, SessionCookie session, SessionProfileService profiles, CancellationToken ct)
    {
        if (session.Read(ctx.Request) is not Guid userId)
            return Results.Unauthorized();

        var profile = await profiles.LoadAsync(userId, ct);
        if (profile is null)
        {
            session.Clear(ctx.Response);
            return Results.Unauthorized();
        }

        ctx.Response.Headers.CacheControl = "no-store";
        return Results.Ok(profile);
    }
}
