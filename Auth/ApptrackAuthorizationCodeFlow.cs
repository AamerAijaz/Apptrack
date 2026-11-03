using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;

namespace Apptrack.Auth;

public sealed class ApptrackAuthorizationCodeFlow(GoogleAuthorizationCodeFlow.Initializer initializer) : GoogleAuthorizationCodeFlow(initializer)
{
    public Task<TokenResponse> ExchangeCodeWithoutStoringAsync(
        string userId,
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var request = CreateAuthorizationCodeTokenRequest(userId, code, redirectUri);
        return FetchTokenAsync(userId, request, cancellationToken);
    }
}
