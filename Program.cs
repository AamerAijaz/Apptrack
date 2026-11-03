using Apptrack.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.EntityFrameworkCore;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

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

var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
{
    ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
    Scopes = scopes,
    DataStore = new NullDataStore(),
});

app.MapGet("/api/auth", (HttpContext ctx) =>
{
    var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    ctx.Response.Cookies.Append("oauth_state", state, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        MaxAge = TimeSpan.FromMinutes(10),
    });

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

app.MapGet("/api/auth/callback", async (string? code, string? state, string? error, HttpContext ctx) =>
{
    try
    {
        if (!string.IsNullOrEmpty(error)) throw new Exception($"Google returned error: {error}");
        if (string.IsNullOrEmpty(code)) throw new Exception($"Authorization code undefined");

        var expectedState = ctx.Request.Cookies["oauth_state"];
        if (string.IsNullOrEmpty(expectedState) || state != expectedState)
            throw new Exception("Invalid OAuth State");
        ctx.Response.Cookies.Delete("oauth_state");

        var token = await flow.ExchangeCodeForTokenAsync("temp-user", code, redirectUri, CancellationToken.None);
        var credential = new UserCredential(flow, "temp-user", token);
        var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Apptrack"
        });

        var profile = await gmail.Users.GetProfile("me").ExecuteAsync();
        Console.WriteLine($"Authorized {profile.EmailAddress}, refresh token present: {token.RefreshToken != null}");

        return Results.Redirect("/api/auth/success");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error during oauth callback: {ex}");
        return Results.Redirect("/api/auth/failure");
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();