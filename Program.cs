using Apptrack.Auth;
using Apptrack.Data;
using Apptrack.Endpoints;
using Apptrack.Entities;
using Apptrack.Services;
using Apptrack.Services.NoticeExtraction;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<IDataStore, GoogleTokenDataStore>();

var clientId = builder.Configuration["Google:ClientId"];
var clientSecret = builder.Configuration["Google:ClientSecret"];
var redirectUri = builder.Configuration["Google:RedirectUri"];

if (clientId == null || clientSecret == null || redirectUri == null)
{
    throw new Exception("One or more required configuration parameters not set!");
}

var scopes = new[]
{
    GmailService.ScopeConstants.GmailReadonly,
    "https://www.googleapis.com/auth/userinfo.email",
};

builder.Services.AddSingleton(sp => new ApptrackAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
{
    ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
    Scopes = scopes,
    DataStore = sp.GetRequiredService<IDataStore>(),
}));
builder.Services.AddSingleton<IMailboxClientFactory, GmailMailboxClientFactory>();
builder.Services.AddSingleton<IApplicationNoticeExtractor>(sp =>
    new AiNoticeExtractor(
        sp.GetRequiredService<IConfiguration>(),
        logger: sp.GetRequiredService<ILogger<AiNoticeExtractor>>()));
builder.Services.AddScoped<GmailSyncService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDataProtection();
builder.Services.AddSingleton(WebPages.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<SessionCookie>();
builder.Services.AddScoped<NoticesService>();

var app = builder.Build();
var flow = app.Services.GetRequiredService<ApptrackAuthorizationCodeFlow>();

app.MapGet("/api/auth", (HttpContext ctx) =>
{
    var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    ctx.Response.Cookies.Append("oauth_state", state, SessionCookie.Options(TimeSpan.FromMinutes(10)));

    var url = new GoogleAuthorizationCodeRequestUrl(new Uri(GoogleAuthConsts.AuthorizationUrl))
    {
        ClientId = clientId,
        RedirectUri = redirectUri,
        Scope = string.Join(" ", scopes),
        AccessType = "offline",
        Prompt = "consent",
        State = state,
    }.Build();

    return Results.Redirect(url.AbsoluteUri);
});

app.MapGet("/api/auth/callback", async (string? code, string? state, string? error, HttpContext ctx, AppDbContext db, GmailSyncService sync, SessionCookie session, WebPages pages) =>
{
    try
    {
        if (!string.IsNullOrEmpty(error)) throw new Exception($"Google returned error: {error}");
        if (string.IsNullOrEmpty(code)) throw new Exception($"Authorization code undefined");

        var expectedState = ctx.Request.Cookies["oauth_state"];
        if (string.IsNullOrEmpty(expectedState) || state != expectedState)
            throw new Exception("Invalid OAuth State");
        ctx.Response.Cookies.Delete("oauth_state", SessionCookie.DeleteOptions());

        // By default the GoogleAuthorizationCodeFlow stores the token in the DataStore if provided.
        // Here, we exchange the code for the token without storing.
        var token = await flow.ExchangeCodeWithoutStoringAsync("temp-user", code, redirectUri, CancellationToken.None);

        // Create a new gmail service for user.
        // We use 'temp-user' as the userId, since this could be a returning user.
        var credential = new UserCredential(flow, "temp-user", token);
        var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Apptrack"
        });

        var profile = await gmail.Users.GetProfile("me").ExecuteAsync();
        var emailAddress = profile.EmailAddress ?? throw new Exception("Gmail profile had no email address");
        var provider = "gmail";

        if (string.IsNullOrEmpty(token.AccessToken))
            throw new InvalidOperationException("Access token is required.");
        if (token.ExpiresInSeconds is not long expiresInSeconds)
            throw new InvalidOperationException("Token expiry is required.");

        var issuedUtc = GoogleTokenDataStore.AsUtc(token.IssuedUtc);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == emailAddress && u.Provider == provider);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            if (user is null)
            {
                if (string.IsNullOrEmpty(token.RefreshToken))
                    throw new InvalidOperationException("Refresh token is required.");

                user = new User(Guid.CreateVersion7(), emailAddress, provider, DateTime.UtcNow);
                db.Users.Add(user);
                db.GoogleAccessTokens.Add(new GoogleAccessToken(
                    user.Id,
                    token.AccessToken,
                    token.RefreshToken,
                    issuedUtc,
                    expiresInSeconds,
                    DateTime.UtcNow));
            }
            else
            {
                var existing = await db.GoogleAccessTokens.FirstOrDefaultAsync(t => t.UserId == user.Id);
                if (existing is null)
                {
                    if (string.IsNullOrEmpty(token.RefreshToken))
                        throw new InvalidOperationException("Refresh token is required.");

                    db.GoogleAccessTokens.Add(new GoogleAccessToken(
                        user.Id,
                        token.AccessToken,
                        token.RefreshToken,
                        issuedUtc,
                        expiresInSeconds,
                        DateTime.UtcNow));
                }
                else
                {
                    existing.UpdateTokens(token.AccessToken, token.RefreshToken, issuedUtc, expiresInSeconds);
                }
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        await sync.StartWatchAsync(user.Id);
        session.Issue(ctx.Response, user.Id);
        return Results.Redirect(pages.Notices.AbsoluteUri);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error during oauth callback: {ex}");
        return Results.Redirect(pages.ConnectFailed.AbsoluteUri);
    }
});

app.MapNotices();

app.MapPost("/api/applications/gmail/webhook", async (PubSubPush push, GmailSyncService sync) =>
{
    try
    {
        await sync.ProcessPushAsync(push);
        return Results.Ok();
    }
    catch (GmailSyncRetryException ex)
    {
        Console.Error.WriteLine($"Error processing webhook: {ex}");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();
