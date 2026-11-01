using System.Security.Claims;
using System.Text.Encodings.Web;
using MealSeal.Api.Data;
using MealSeal.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MealSeal.Api.Security;

/// <summary>
/// Verifies MealSeal's own ES256-signed access tokens (see <see cref="AuthTokenService"/>) and
/// loads the matching active <see cref="UserProfile"/> to build role/restaurant claims — the same
/// shape the rest of the API (role policies, <c>restaurant_id</c> claim lookups) already expects.
/// This replaces the previous Firebase ID-token verification; nothing downstream of authentication
/// had to change.
/// </summary>
public sealed class AccessTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AuthTokenService tokens,
    MealSealDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = authorization[7..].Trim();
        if (token.Length == 0) return AuthenticateResult.Fail("Missing bearer token.");

        try
        {
            var userId = tokens.ValidateAccessToken(token);
            if (userId is null) return AuthenticateResult.Fail("Invalid or expired access token.");

            var user = await db.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == userId && x.Active, Context.RequestAborted);
            if (user is null) return AuthenticateResult.Fail("No active MealSeal account for this token.");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, user.Role)
            };
            if (!string.IsNullOrWhiteSpace(user.RestaurantId))
                claims.Add(new Claim("restaurant_id", user.RestaurantId));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Access token verification failed.");
            return AuthenticateResult.Fail("Invalid or expired access token.");
        }
    }
}
