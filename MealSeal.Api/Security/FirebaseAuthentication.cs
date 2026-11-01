using System.Security.Claims;
using System.Text.Encodings.Web;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using MealSeal.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MealSeal.Api.Security;

public interface IFirebaseTokenVerifier
{
    Task<string> VerifyAsync(string token, CancellationToken cancellationToken);
}

public sealed class FirebaseTokenVerifier : IFirebaseTokenVerifier, IDisposable
{
    private readonly Lazy<FirebaseApp> _app;

    public FirebaseTokenVerifier(IOptions<FirebaseOptions> options, IWebHostEnvironment environment)
    {
        var settings = options.Value;
        _app = new Lazy<FirebaseApp>(() =>
        {
            GoogleCredential credential;
            if (!string.IsNullOrWhiteSpace(settings.ServiceAccountJson))
                credential = CredentialFactory.FromJson<ServiceAccountCredential>(settings.ServiceAccountJson).ToGoogleCredential();
            else if (!string.IsNullOrWhiteSpace(settings.ServiceAccountPath))
            {
                var path = Path.IsPathRooted(settings.ServiceAccountPath)
                    ? settings.ServiceAccountPath
                    : Path.Combine(environment.ContentRootPath, settings.ServiceAccountPath);
                credential = CredentialFactory.FromFile<ServiceAccountCredential>(path).ToGoogleCredential();
            }
            else
                credential = GoogleCredential.GetApplicationDefault();

            return FirebaseApp.Create(new AppOptions
            {
                Credential = credential,
                ProjectId = settings.ProjectId
            }, $"MealSeal-{Guid.NewGuid():N}");
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<string> VerifyAsync(string token, CancellationToken cancellationToken)
    {
        var decoded = await FirebaseAuth.GetAuth(_app.Value).VerifyIdTokenAsync(token, true, cancellationToken);
        return decoded.Uid;
    }

    public void Dispose()
    {
        if (_app.IsValueCreated) _app.Value.Delete();
    }
}

public sealed class FirebaseAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IFirebaseTokenVerifier verifier,
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
            var uid = await verifier.VerifyAsync(token, Context.RequestAborted);
            var user = await db.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.FirebaseUid == uid && x.Active, Context.RequestAborted);
            if (user is null) return AuthenticateResult.Fail("No active API profile exists for this Firebase user.");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, uid),
                new(ClaimTypes.Role, user.Role)
            };
            if (!string.IsNullOrWhiteSpace(user.RestaurantId))
                claims.Add(new Claim("restaurant_id", user.RestaurantId));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Firebase token verification failed.");
            return AuthenticateResult.Fail("Invalid or expired Firebase token.");
        }
    }
}
