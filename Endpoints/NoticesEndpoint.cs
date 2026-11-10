using System.Text.Json;
using Apptrack.Auth;
using Apptrack.Services;

namespace Apptrack.Endpoints;

public sealed record NoticeLaneDto(string Status, IReadOnlyList<NoticeCard> Notices, int Limit);

public sealed record NoticeBoardDto(string Email, IReadOnlyList<NoticeLaneDto> Lanes);

public sealed record NoticeLanePageDto(string Email, string Status, IReadOnlyList<NoticeCard> Notices, int Limit);

public sealed record AddedNoticeDto(string Status, NoticeCard Notice);

public static class NoticesEndpoint
{
    private sealed record HandNoticeBody(string? Company, string? Role, string? Status, string? Key);

    public static IEndpointRouteBuilder MapNotices(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notices", GetAsync);
        app.MapGet("/api/notices/lane", GetLaneAsync);
        app.MapPost("/api/notices", AddAsync);
        app.MapPatch("/api/notices/{id:guid}", MoveAsync);
        return app;
    }

    private sealed record MoveNoticeBody(string? Status);

    /// <summary>
    /// Cookie user only. 401 clears the cookie when the person is missing.
    /// 400 leaves the cookie. 404 is a notice the person does not have.
    /// 200 is the move and a repeat move to the same stage.
    /// </summary>
    public static async Task<IResult> MoveAsync(
        HttpContext ctx, SessionCookie session, NoticesService notices, Guid id, CancellationToken ct)
    {
        if (session.Read(ctx.Request) is not Guid userId)
            return Results.Unauthorized();

        MoveNoticeBody? body;
        try
        {
            body = await ctx.Request.ReadFromJsonAsync<MoveNoticeBody>(ct);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        var status = NoticeStatus.FromLabel(body?.Status);
        if (status is null)
            return Results.BadRequest();

        switch (await notices.Move(userId, id, status, ct))
        {
            case MoveNoticeResult.MissingPerson:
                session.Clear(ctx.Response);
                return Results.Unauthorized();
            case MoveNoticeResult.NotFound:
                return Results.NotFound();
            case MoveNoticeResult.Stored stored:
                ctx.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new AddedNoticeDto(stored.Status.Label, stored.Card));
            default:
                throw new InvalidOperationException("Moving a notice produced an unknown result.");
        }
    }

    /// <summary>
    /// Cookie user only. 401 clears the cookie when the person is missing.
    /// 400 and 409 leave the cookie. 200 is create and replay.
    /// </summary>
    public static async Task<IResult> AddAsync(
        HttpContext ctx, SessionCookie session, NoticesService notices, CancellationToken ct)
    {
        if (session.Read(ctx.Request) is not Guid userId)
            return Results.Unauthorized();

        HandNoticeBody? body;
        try
        {
            body = await ctx.Request.ReadFromJsonAsync<HandNoticeBody>(ct);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        var incoming = IncomingNotice.TryHand(body?.Company, body?.Role, body?.Status, body?.Key);
        if (incoming is null)
            return Results.BadRequest();

        switch (await notices.Add(userId, incoming, ct))
        {
            case AddNoticeResult.MissingPerson:
                session.Clear(ctx.Response);
                return Results.Unauthorized();
            case AddNoticeResult.Retired:
                return Results.Conflict();
            case AddNoticeResult.Stored stored:
                ctx.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new AddedNoticeDto(stored.Status.Label, stored.Card));
            default:
                throw new InvalidOperationException("Adding a notice produced an unknown result.");
        }
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
