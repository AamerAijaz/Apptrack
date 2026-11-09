using Apptrack.Auth;
using Apptrack.Services;

namespace Apptrack.Endpoints;

public sealed record NoticeLaneDto(string Status, IReadOnlyList<NoticeCard> Notices, int Limit);

public sealed record NoticeBoardDto(string Email, IReadOnlyList<NoticeLaneDto> Lanes);

public sealed record NoticeLanePageDto(string Email, string Status, IReadOnlyList<NoticeCard> Notices, int Limit);

public static class NoticesEndpoint
{
    public static IEndpointRouteBuilder MapNotices(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notices", GetAsync);
        app.MapGet("/api/notices/lane", GetLaneAsync);
        return app;
    }

    public static async Task<IResult> GetAsync(
        HttpContext ctx, SessionCookie session, NoticesService notices, CancellationToken ct)
    {
        if (session.Read(ctx.Request) is not Guid userId)
            return Results.Unauthorized();

        string? email = null;
        var lanes = new List<NoticeLaneDto>(NoticeStatus.All.Count);
        foreach (var status in NoticeStatus.All)
        {
            var page = await notices.GetNotices(userId, status, NoticesService.BoardLimit, 0, ct);
            if (page is null)
            {
                session.Clear(ctx.Response);
                return Results.Unauthorized();
            }

            email ??= page.Email;
            lanes.Add(new NoticeLaneDto(status.Label, page.Notices, NoticesService.BoardLimit));
        }

        ctx.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new NoticeBoardDto(email!, lanes));
    }

    public static async Task<IResult> GetLaneAsync(
        HttpContext ctx,
        SessionCookie session,
        NoticesService notices,
        string? application_status,
        int? limit,
        int? offset,
        CancellationToken ct)
    {
        if (session.Read(ctx.Request) is not Guid userId)
            return Results.Unauthorized();

        var status = NoticeStatus.FromLabel(application_status);
        if (status is null || limit is null || offset is null || !IsPage(limit.Value, offset.Value))
            return Results.BadRequest();

        var page = await notices.GetNotices(userId, status, limit.Value, offset.Value, ct);
        if (page is null)
        {
            session.Clear(ctx.Response);
            return Results.Unauthorized();
        }

        ctx.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new NoticeLanePageDto(page.Email, status.Label, page.Notices, limit.Value));
    }

    private static bool IsPage(int limit, int offset) =>
        limit >= 1 && limit <= NoticesService.MaxLimit && offset >= 0;
}
